using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using HarmonyLib;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.Economy;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using UnityEngine;
using IL2List = Il2CppSystem.Collections.Generic.List<Nivalis.InventorySystem.ItemInstanceData>;

namespace NivalisModKit;

// Moved from Manager Order Fix 1.1. The game's TryPurchaseIngredients calls Vendor.BuyItem once
// per vendor per ingredient. The detour collects those calls instead of running them; when the
// recipe's purchasing ends, FlushAll replays them in the chosen order, only up to the quantity.
static unsafe class PurchasePipeline
{
    internal static bool Installed;
    static bool attempted;

    // BuyItem(in ShopTradeRequest, float, ref BasicTemp). By-ref structs, so a native hook, not Harmony.
    const string BuyItemField =
        "NativeMethodInfoPtr_BuyItem_Public_Void_byref_ShopTradeRequest_Single_byref_BasicTemp_0";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void BuyItemFn(IntPtr self, IntPtr request, float discount, IntPtr tempItem, IntPtr method);

    static BuyItemFn Original;
    static INativeDetour Detour;
    static Harmony harmony;

    static int OffCustomer, OffItemType, OffFreshness, OffAmount, OffStackCount;
    static int ReqSize, TempSize;

    // ---------- per-recipe context ----------
    static IntPtr CurrentArea = IntPtr.Zero;
    static VenueAreaGhost CurrentAreaObj;
    static IRecipe CurrentRecipe;
    static WorldLocation CurrentVenueLoc;
    static float CurrentLimit;
    static readonly Dictionary<IntPtr, int> ToBuy = new();     // item -> game quantity
    static readonly Dictionary<IntPtr, int> Bought = new();    // item -> bought so far
    static readonly Dictionary<IntPtr, List<VendorOffer>> PendingByItem = new();
    static readonly List<IntPtr> PendingOrder = new();
    static int Seq;

    // ---------- session cache ----------
    static readonly Dictionary<IntPtr, WorldLocation> VendorLoc = new();

    internal static void EnsureInstalled()
    {
        if (attempted) return;
        attempted = true;
        try
        {
            OffCustomer   = StructLayout.FieldOffset<ShopTradeRequest>("customer");
            OffItemType   = StructLayout.FieldOffset<ShopTradeRequest>("itemType");
            OffFreshness  = StructLayout.FieldOffset<ShopTradeRequest>("freshness");
            OffAmount     = StructLayout.FieldOffset<ShopTradeRequest>("amount");
            OffStackCount = StructLayout.FieldOffset<ItemStack.BasicTemp>("StackCount");
            ReqSize       = StructLayout.Size<ShopTradeRequest>();
            TempSize      = StructLayout.Size<ItemStack.BasicTemp>();
            IntPtr buyItem = NativeHook.MethodPointer<Vendor>(BuyItemField);

            var recipe = AccessTools.Method(typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryPurchaseIngredients))
                ?? throw new Exception("VenueAreaGhost.TryPurchaseIngredients not found");
            var purchase = AccessTools.Method(typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryMakePurchase))
                ?? throw new Exception("VenueAreaGhost.TryMakePurchase not found");

            harmony = new Harmony(ModKit.Guid + ".purchasing");
            harmony.Patch(recipe, prefix: Hm(nameof(RecipePrefix)),
                                  postfix: Hm(nameof(RecipePostfix), Priority.First));
            harmony.Patch(purchase, postfix: Hm(nameof(PurchasePostfix)));

            // Last, so a failure here leaves Installed false and the patches above inert.
            Detour = NativeHook.Install<BuyItemFn>(buyItem, BuyItemHook, out Original);
            Installed = true;

            KitPlugin.L.LogInfo("Purchasing pipeline: live");
            KitPlugin.L.LogInfo($"Hooked BuyItem at 0x{buyItem.ToInt64():X}; customer={OffCustomer} " +
                                $"itemType={OffItemType} freshness={OffFreshness} amount={OffAmount} " +
                                $"stackCount={OffStackCount} reqSize={ReqSize} tempSize={TempSize}");
        }
        catch (Exception e)
        {
            KitPlugin.L.LogError($"Purchasing pipeline: missing ({e.Message})");
        }
    }

    static HarmonyMethod Hm(string name, int priority = Priority.Normal) =>
        new(typeof(PurchasePipeline), name) { priority = priority };

    // ---------- helpers ----------

    static int BoughtOf(IntPtr item) => Bought.TryGetValue(item, out int b) ? b : 0;

    static int HopsTo(IntPtr vendor)
    {
        if (CurrentVenueLoc == null) return World.Unreachable;
        if (!VendorLoc.TryGetValue(vendor, out WorldLocation loc))
        {
            try { loc = new Vendor(vendor).Location?.Location; }
            catch { loc = null; }
            VendorLoc[vendor] = loc;
        }
        return loc == null ? World.Unreachable : World.Hops(CurrentVenueLoc, loc);
    }

    // The game's own cap from TryPurchaseIngredients: FloorToInt(limit / RoundToInt(cost * barter)).
    static int LimitCap(int price, float discount, int stock)
    {
        if (price <= 0 || price == int.MaxValue) return stock;
        int barterPrice = Mathf.RoundToInt(price * discount);
        return barterPrice <= 0 ? stock : Math.Min(stock, Mathf.FloorToInt(CurrentLimit / barterPrice));
    }

    // ---------- the hook: collect ----------

    static void BuyItemHook(IntPtr self, IntPtr request, float discount, IntPtr tempItem, IntPtr method)
    {
        try
        {
            if (CurrentArea == IntPtr.Zero || request == IntPtr.Zero || tempItem == IntPtr.Zero ||
                *(IntPtr*)(request + OffCustomer) != CurrentArea)
            {
                Original(self, request, discount, tempItem, method);
                return;
            }

            // The game's quantity for this item: 1.0 put the full need in every call's temp stack;
            // patch 1 sizes it to this vendor's amount, min(stock, need, budget cap). Nothing is
            // bought while calls are deferred, so each call asks for the full need capped by that
            // vendor; the largest across the round is the need (works for both builds).
            IntPtr item = *(IntPtr*)(request + OffItemType);
            int asked = Math.Max(*(int*)(tempItem + OffStackCount), *(int*)(request + OffAmount));
            if (!ToBuy.TryGetValue(item, out int known) || asked > known) ToBuy[item] = asked;

            Defer(self, request, discount, tempItem, method, item);
            // the purchase happens in FlushAll, in the chosen order
        }
        catch (Exception e)
        {
            KitPlugin.L.LogError($"Purchasing BuyItemHook: {e.Message}");
            try { Original(self, request, discount, tempItem, method); } catch { }
        }
    }

    static void Defer(IntPtr vendor, IntPtr request, float discount, IntPtr tempItem, IntPtr method, IntPtr item)
    {
        if (!PendingByItem.TryGetValue(item, out var list))
        {
            list = new List<VendorOffer>();
            PendingByItem[item] = list;
            PendingOrder.Add(item);
        }

        var v = new Vendor(vendor);
        var it = list.Count > 0 ? list[0].Item : new ItemType(item);

        int price;
        try { price = v.GetItemBuyCost(it, *(FoodFreshness*)(request + OffFreshness)); }
        catch { price = int.MaxValue; }

        int stock;
        try { stock = v.container?.GetItemCount(it) ?? 0; }
        catch { stock = 0; }

        var offer = new VendorOffer(v, it, price, stock, HopsTo(vendor),
            *(int*)(request + OffAmount), LimitCap(price, discount, stock), Seq++)
        {
            VendorPtr = vendor, Method = method, Discount = discount,
            Req = Marshal.AllocHGlobal(ReqSize),
            Temp = Marshal.AllocHGlobal(TempSize),
        };
        Buffer.MemoryCopy((void*)request, (void*)offer.Req, ReqSize, ReqSize);
        Buffer.MemoryCopy((void*)tempItem, (void*)offer.Temp, TempSize, TempSize);
        list.Add(offer);
    }

    // ---------- extension points ----------

    static int RaiseQuantity(ItemType item, int gameQuantity)
    {
        var handlers = Purchasing.QuantityHandlers;
        var ctx = new OrderQuantityContext(CurrentAreaObj, CurrentRecipe, item, CurrentVenueLoc, gameQuantity);
        if (handlers == null) return gameQuantity;

        foreach (Delegate d in handlers.GetInvocationList())
        {
            int before = ctx.Quantity;
            try { ((Action<OrderQuantityContext>)d)(ctx); }
            catch (Exception e)
            {
                ctx.Quantity = before;
                GameEvents.LogFailure("Purchasing.OrderQuantity", d, e);
            }
            if (ctx.Quantity < 0) ctx.Quantity = 0;
        }
        return ctx.Quantity;
    }

    static List<VendorOffer> RaiseOrdering(ItemType item, List<VendorOffer> offers, int quantity)
    {
        var handlers = Purchasing.OrderingHandlers;
        if (handlers == null) return offers;

        var allowed = new HashSet<VendorOffer>(offers);
        var ctx = new VendorOrderingContext(CurrentAreaObj, CurrentRecipe, item, CurrentVenueLoc, quantity, offers);

        foreach (Delegate d in handlers.GetInvocationList())
        {
            var before = new List<VendorOffer>(ctx.Offers);
            try
            {
                ((Action<VendorOrderingContext>)d)(ctx);
                if (!IsValid(ctx.Offers, allowed))
                {
                    ctx.Offers = before;
                    string who = d.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
                    KitPlugin.L.LogError($"Purchasing.VendorOrdering handler in {who} returned a list with " +
                                         "null, duplicate, or added offers; its change was discarded");
                }
            }
            catch (Exception e)
            {
                ctx.Offers = before;
                GameEvents.LogFailure("Purchasing.VendorOrdering", d, e);
            }
        }
        return ctx.Offers;
    }

    static bool IsValid(List<VendorOffer> list, HashSet<VendorOffer> allowed)
    {
        if (list == null) return false;
        var seen = new HashSet<VendorOffer>();
        foreach (var o in list)
            if (o == null || !allowed.Contains(o) || !seen.Add(o)) return false;
        return true;
    }

    static void Decide(VendorOffer offer, PurchaseResult result, int amount, int count) =>
        Purchasing.RaiseDecision(new PurchaseDecisionArgs(CurrentAreaObj, CurrentRecipe, offer, result, amount, count));

    // ---------- the flush: order and buy ----------

    static bool FlushAll()
    {
        bool boughtAny = false;
        try
        {
            foreach (IntPtr item in PendingOrder)
            {
                var pending = PendingByItem[item];
                int gameQuantity = ToBuy[item];
                int target = RaiseQuantity(pending[0].Item, gameQuantity);
                var offers = RaiseOrdering(pending[0].Item, new List<VendorOffer>(pending), target);

                foreach (var o in offers)
                {
                    int remaining = target - BoughtOf(item);
                    int n = Math.Min(target > gameQuantity ? o.MaxAmount : o.Amount, remaining);
                    if (n <= 0)
                    {
                        Decide(o, PurchaseResult.Skipped, 0, offers.Count);
                        continue;
                    }

                    *(int*)(o.Req + OffAmount) = n;
                    *(int*)(o.Temp + OffStackCount) = remaining;

                    Original(o.VendorPtr, o.Req, o.Discount, o.Temp, o.Method);

                    if (target - BoughtOf(item) < remaining)
                    {
                        boughtAny = true;
                        Decide(o, PurchaseResult.Bought, n, offers.Count);
                    }
                    else
                    {
                        // TryMakePurchase refused, almost always because the venue is out of money.
                        // A failed BuyItem puts newly created items into the vendor's stock, so stop
                        // here, the same point where the game's own loop breaks on low cash.
                        Decide(o, PurchaseResult.Failed, n, offers.Count);
                        return boughtAny;
                    }
                }
            }
        }
        catch (Exception e)
        {
            KitPlugin.L.LogError($"Purchasing FlushAll: {e.Message}");
        }
        return boughtAny;
    }

    static void Reset()
    {
        foreach (var list in PendingByItem.Values)
            foreach (var o in list)
            {
                if (o.Req != IntPtr.Zero) Marshal.FreeHGlobal(o.Req);
                if (o.Temp != IntPtr.Zero) Marshal.FreeHGlobal(o.Temp);
                o.Req = o.Temp = IntPtr.Zero;
            }
        PendingByItem.Clear();
        PendingOrder.Clear();
        Seq = 0;
        CurrentArea = IntPtr.Zero;
        CurrentAreaObj = null;
        CurrentRecipe = null;
        CurrentVenueLoc = null;
        ToBuy.Clear();
        Bought.Clear();
    }

    // ---------- recipe context (Harmony, safe signatures) ----------

    static void RecipePrefix(VenueAreaGhost __instance, IRecipe recipe, float limit)
    {
        try
        {
            Reset();
            if (!Installed || !Purchasing.Active || __instance == null) return;
            CurrentArea = __instance.Pointer;
            CurrentAreaObj = __instance;
            CurrentRecipe = recipe;
            CurrentLimit = limit;
            try { CurrentVenueLoc = __instance.Venue?.Location; }
            catch { CurrentVenueLoc = null; }
        }
        catch (Exception e) { KitPlugin.L.LogError($"Purchasing RecipePrefix: {e.Message}"); }
    }

    static void RecipePostfix(ref bool __result)
    {
        try
        {
            if (CurrentArea != IntPtr.Zero && PendingOrder.Count > 0 && FlushAll())
                __result = true;
        }
        catch (Exception e) { KitPlugin.L.LogError($"Purchasing RecipePostfix: {e.Message}"); }
        finally { Reset(); }
    }

    static void PurchasePostfix(VenueAreaGhost __instance, ItemType stackType, IL2List boughtInstances, bool __result)
    {
        try
        {
            if (!__result || CurrentArea == IntPtr.Zero || stackType == null) return;
            if (__instance == null || __instance.Pointer != CurrentArea) return;

            IntPtr key = stackType.Pointer;
            Bought[key] = BoughtOf(key) + (boughtInstances?.Count ?? 0);
        }
        catch (Exception e) { KitPlugin.L.LogError($"Purchasing PurchasePostfix: {e.Message}"); }
    }
}
