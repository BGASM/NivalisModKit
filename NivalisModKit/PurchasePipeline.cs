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
using Il2CppInterop.Runtime;

namespace NivalisModKit;

// The game's TryPurchaseIngredients (patch 4 on): for each ingredient below the dish's supply target
// it asks EconomyManager.GetVendorsByItem for (vendor, stock, price) sorted cheapest first, then buys
// min(stock, need) from each in turn until the need is met, within the money and the venue's
// restock budget. The pipeline sits on three of those calls: GetSupplyTarget (OrderQuantity),
// GetVendorsByItem (VendorOrdering, by reordering the game's list in place) and Vendor.BuyItem
// (Decision). The game still does all the buying.
static unsafe class PurchasePipeline
{
    internal static bool Installed;
    static bool attempted;

    // GetVendorsByItem returns a ListPool.Handle struct (by hidden pointer, first) and fills an out
    // list; BuyItem takes by-ref structs. Native hooks, not Harmony.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr VendorsFn(IntPtr ret, IntPtr self, IntPtr item, IntPtr* results, IntPtr method);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate byte BuyItemFn(IntPtr self, IntPtr request, float discount, IntPtr tempItem, IntPtr method);

    const string BuyItemField = "NativeMethodInfoPtr_BuyItem_Public_Boolean_byref_ShopTradeRequest_Single_byref_BasicTemp_0";

    static VendorsFn OriginalVendors;
    static BuyItemFn OriginalBuy;
    static INativeDetour VendorsDetour, BuyDetour;
    static Harmony harmony;

    static int OffCustomer, OffItemType, OffAmount;

    // ---------- per-recipe context ----------
    static IntPtr CurrentArea = IntPtr.Zero;
    static VenueAreaGhost CurrentAreaObj;
    static IRecipe CurrentRecipe;
    static WorldLocation CurrentVenueLoc;
    static int CurrentTarget = -1;      // the supply target, after OrderQuantity; -1 until asked
    static bool targetRaised;

    // The ingredient being bought: its offers in buying order, and which have been reported.
    static List<VendorOffer> offers;
    static readonly HashSet<VendorOffer> reported = new();

    // ---------- session cache ----------
    static readonly Dictionary<IntPtr, WorldLocation> VendorLoc = new();

    internal static void EnsureInstalled()
    {
        if (attempted) return;
        attempted = true;
        try
        {
            OffCustomer = StructLayout.FieldOffset<ShopTradeRequest>("customer");
            OffItemType = StructLayout.FieldOffset<ShopTradeRequest>("itemType");
            OffAmount   = StructLayout.FieldOffset<ShopTradeRequest>("amount");
            IntPtr vendors = NativeHook.MethodPointer<EconomyManager>(nameof(EconomyManager.GetVendorsByItem));
            // BuyItem has overloads (the player's purchases use others); this is the managers' one.
            IntPtr buyItem = NativeHook.MethodPointer<Vendor>(BuyItemField);

            var recipe = AccessTools.Method(typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryPurchaseIngredients))
                ?? throw new Exception("VenueAreaGhost.TryPurchaseIngredients not found");
            var target = AccessTools.Method(typeof(VenueAreaGhost), nameof(VenueAreaGhost.GetSupplyTarget))
                ?? throw new Exception("VenueAreaGhost.GetSupplyTarget not found");

            harmony = new Harmony(ModKit.Guid + ".purchasing");
            harmony.Patch(recipe, prefix: Hm(nameof(RecipePrefix)),
                                  postfix: Hm(nameof(RecipePostfix), Priority.First));
            harmony.Patch(target, postfix: Hm(nameof(TargetPostfix)));

            // Last, so a failure here leaves Installed false and the patches above inert.
            VendorsDetour = NativeHook.Install<VendorsFn>(vendors, VendorsHook, out OriginalVendors);
            BuyDetour = NativeHook.Install<BuyItemFn>(buyItem, BuyItemHook, out OriginalBuy);
            Installed = true;

            KitPlugin.L.LogInfo("Purchasing pipeline: live");
        }
        catch (Exception e)
        {
            KitPlugin.L.LogError($"Purchasing pipeline: missing ({e.Message})");
        }
    }

    static HarmonyMethod Hm(string name, int priority = Priority.Normal) =>
        new(typeof(PurchasePipeline), name) { priority = priority };

    // ---------- helpers ----------

    static int HopsTo(Vendor vendor)
    {
        if (CurrentVenueLoc == null) return World.Unreachable;
        if (!VendorLoc.TryGetValue(vendor.Pointer, out WorldLocation loc))
        {
            try { loc = vendor.Location?.Location; }
            catch { loc = null; }
            VendorLoc[vendor.Pointer] = loc;
        }
        return loc == null ? World.Unreachable : World.Hops(CurrentVenueLoc, loc);
    }

    static void Decide(VendorOffer offer, PurchaseResult result, int amount)
    {
        reported.Add(offer);
        Purchasing.RaiseDecision(new PurchaseDecisionArgs(CurrentAreaObj, CurrentRecipe, offer, result, amount,
            offers?.Count ?? 0));
    }

    // The previous ingredient's offers the game never bought from: the need was met, or it stopped
    // (out of money or restock budget).
    static void FlushSkipped()
    {
        if (offers == null) return;
        foreach (var o in offers)
            if (!reported.Contains(o)) Decide(o, PurchaseResult.Skipped, 0);
        offers = null;
        reported.Clear();
    }

    // ---------- OrderQuantity: the supply target ----------

    static void TargetPostfix(VenueAreaGhost __instance, ItemType item, ref int __result)
    {
        try
        {
            if (CurrentArea == IntPtr.Zero || __instance == null || __instance.Pointer != CurrentArea) return;
            if (!targetRaised)
            {
                targetRaised = true;
                CurrentTarget = RaiseQuantity(item, __result);
            }
            __result = CurrentTarget;
        }
        catch (Exception e) { KitPlugin.L.LogError($"Purchasing TargetPostfix: {e.Message}"); }
    }

    static int RaiseQuantity(ItemType dish, int gameTarget)
    {
        var handlers = Purchasing.QuantityHandlers;
        if (handlers == null) return gameTarget;
        var ctx = new OrderQuantityContext(CurrentAreaObj, CurrentRecipe, dish, CurrentVenueLoc, gameTarget);

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

    // ---------- VendorOrdering: the game's vendor list ----------

    static IntPtr VendorsHook(IntPtr ret, IntPtr self, IntPtr item, IntPtr* results, IntPtr method)
    {
        IntPtr r = OriginalVendors(ret, self, item, results, method);
        try
        {
            if (CurrentArea != IntPtr.Zero && item != IntPtr.Zero && results != null && *results != IntPtr.Zero)
                Reorder(new ItemType(item), *results);
        }
        catch (Exception e) { KitPlugin.L.LogError($"Purchasing VendorsHook: {e.Message}"); }
        return r;
    }

    // List<ValueTuple<Vendor, int, int>>, read and written in place: Il2CppInterop's wrappers misread this struct
    // (ints come back as garbage) and List.Add through them writes null vendors. Layout from the runtime, once.
    static bool layoutKnown;
    static int OffItems, OffSize, OffVersion = -1, ElemSize, OffVendor, OffStock, OffPrice;
    static readonly int ArrayData = 4 * IntPtr.Size;   // Il2CppArray: klass, monitor, bounds, max_length

    static int FieldOffset(IntPtr klass, string name)
    {
        IntPtr f = IL2CPP.il2cpp_class_get_field_from_name(klass, name);
        if (f == IntPtr.Zero) throw new Exception($"field {name} not found");
        return (int)IL2CPP.il2cpp_field_get_offset(f);
    }

    static void LearnLayout(IntPtr list, IntPtr items)
    {
        IntPtr listClass = IL2CPP.il2cpp_object_get_class(list);
        OffItems = FieldOffset(listClass, "_items");
        OffSize = FieldOffset(listClass, "_size");
        try { OffVersion = FieldOffset(listClass, "_version"); } catch { OffVersion = -1; }
        IntPtr arrayClass = IL2CPP.il2cpp_object_get_class(items);
        ElemSize = IL2CPP.il2cpp_array_element_size(arrayClass);
        IntPtr elemClass = IL2CPP.il2cpp_class_get_element_class(arrayClass);
        // A value type's field offsets include the object header; array elements don't have one.
        OffVendor = FieldOffset(elemClass, "Item1") - 2 * IntPtr.Size;
        OffStock = FieldOffset(elemClass, "Item2") - 2 * IntPtr.Size;
        OffPrice = FieldOffset(elemClass, "Item3") - 2 * IntPtr.Size;
        layoutKnown = true;
    }

    struct Entry { public IntPtr Vendor; public int Stock, Price; }

    static void Reorder(ItemType item, IntPtr list)
    {
        FlushSkipped();

        int need = 0;
        try { need = Math.Max(0, CurrentTarget - CurrentAreaObj.JointInventory.GetItemCount(item)); } catch { }

        if (!layoutKnown)
        {
            IntPtr first = *(IntPtr*)(list + FieldOffset(IL2CPP.il2cpp_object_get_class(list), "_items"));
            if (first == IntPtr.Zero) return;
            LearnLayout(list, first);
        }
        int count = *(int*)(list + OffSize);
        IntPtr items = *(IntPtr*)(list + OffItems);
        if (count <= 0 || items == IntPtr.Zero) return;
        byte* data = (byte*)items + ArrayData;

        var entries = new List<Entry>(count);
        var game = new List<VendorOffer>(count);
        for (int i = 0; i < count; i++)
        {
            byte* e = data + i * ElemSize;
            var en = new Entry { Vendor = *(IntPtr*)(e + OffVendor), Stock = *(int*)(e + OffStock), Price = *(int*)(e + OffPrice) };
            entries.Add(en);
            if (en.Vendor == IntPtr.Zero) continue;   // never: the game adds only vendors
            var v = new Vendor(en.Vendor);
            game.Add(new VendorOffer(v, item, en.Price, en.Stock, HopsTo(v), Math.Min(en.Stock, need), i));
        }

        var chosen = RaiseOrdering(item, game, need);
        offers = chosen;
        if (ReferenceEquals(chosen, game) || SameOrder(chosen, game)) return;

        // Write the chosen entries back in order (Sequence is the entry's index), vendor references through the
        // GC write barrier; removed offers shorten the list, and the slots past the end are cleared.
        var order = chosen.Select(o => entries[o.Sequence]).ToList();
        for (int i = 0; i < count; i++)
        {
            byte* e = data + i * ElemSize;
            var en = i < order.Count ? order[i] : default;
            IL2CPP.il2cpp_gc_wbarrier_set_field(items, (IntPtr)(e + OffVendor), en.Vendor);
            *(int*)(e + OffStock) = en.Stock;
            *(int*)(e + OffPrice) = en.Price;
        }
        *(int*)(list + OffSize) = order.Count;
        if (OffVersion >= 0) (*(int*)(list + OffVersion))++;
    }

    static bool SameOrder(List<VendorOffer> a, List<VendorOffer> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static List<VendorOffer> RaiseOrdering(ItemType item, List<VendorOffer> game, int need)
    {
        var handlers = Purchasing.OrderingHandlers;
        if (handlers == null) return game;

        var allowed = new HashSet<VendorOffer>(game);
        var ctx = new VendorOrderingContext(CurrentAreaObj, CurrentRecipe, item, CurrentVenueLoc, need,
            new List<VendorOffer>(game));

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

    // ---------- Decision: each purchase ----------

    static byte BuyItemHook(IntPtr self, IntPtr request, float discount, IntPtr tempItem, IntPtr method)
    {
        byte ok = OriginalBuy(self, request, discount, tempItem, method);
        try
        {
            if (CurrentArea == IntPtr.Zero || offers == null || request == IntPtr.Zero ||
                *(IntPtr*)(request + OffCustomer) != CurrentArea)
                return ok;
            IntPtr item = *(IntPtr*)(request + OffItemType);
            var offer = offers.Find(o => o.Vendor.Pointer == self && o.Item.Pointer == item);
            if (offer != null)
                Decide(offer, ok != 0 ? PurchaseResult.Bought : PurchaseResult.Failed, *(int*)(request + OffAmount));
        }
        catch (Exception e) { KitPlugin.L.LogError($"Purchasing BuyItemHook: {e.Message}"); }
        return ok;
    }

    // ---------- recipe context (Harmony, safe signatures) ----------

    static void Reset()
    {
        offers = null;
        reported.Clear();
        CurrentArea = IntPtr.Zero;
        CurrentAreaObj = null;
        CurrentRecipe = null;
        CurrentVenueLoc = null;
        CurrentTarget = -1;
        targetRaised = false;
    }

    static void RecipePrefix(VenueAreaGhost __instance, IRecipe recipe)
    {
        try
        {
            Reset();
            if (!Installed || !Purchasing.Active || __instance == null) return;
            CurrentArea = __instance.Pointer;
            CurrentAreaObj = __instance;
            CurrentRecipe = recipe;
            try { CurrentVenueLoc = __instance.Venue?.Location; }
            catch { CurrentVenueLoc = null; }
        }
        catch (Exception e) { KitPlugin.L.LogError($"Purchasing RecipePrefix: {e.Message}"); }
    }

    static void RecipePostfix()
    {
        try { if (CurrentArea != IntPtr.Zero) FlushSkipped(); }
        catch (Exception e) { KitPlugin.L.LogError($"Purchasing RecipePostfix: {e.Message}"); }
        finally { Reset(); }
    }
}
