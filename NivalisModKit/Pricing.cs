using System;
using HarmonyLib;
using Nivalis.Economy;
using Nivalis.InventorySystem;

namespace NivalisModKit;

/// <summary>
/// Change vendor prices. Handlers adjust <see cref="PriceContext.Price"/>; the result is what the
/// shop shows and what is actually charged or paid, for the player and for venue managers alike.
/// Nothing is installed until a mod subscribes. Prices are recalculated on every lookup, so a
/// handler can change its rules at any time (for example from a live-reloaded setting).
/// </summary>
public static class Pricing
{
    static Action<PriceContext> buy, sell;
    static bool attempted, installed;

    /// <summary>The price a vendor charges per item when someone buys from it.</summary>
    public static event Action<PriceContext> BuyPrice
    {
        add { buy += value; Added(nameof(BuyPrice), value); }
        remove { buy -= value; }
    }

    /// <summary>The price a vendor pays per item when the player sells to it.</summary>
    public static event Action<PriceContext> SellPrice
    {
        add { sell += value; Added(nameof(SellPrice), value); }
        remove { sell -= value; }
    }

    /// <summary>True once the price hooks installed. False before any mod subscribes, or if they failed.</summary>
    public static bool IsAvailable => installed;

    static void Added(string name, Delegate handler)
    {
        string who = handler?.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
        KitPlugin.L.LogInfo($"Pricing.{name}: handler added by {who}");
        try { Install(); }
        catch (Exception e) { KitPlugin.L.LogError($"Pricing: missing ({e.Message})"); }
    }

    static void Install()
    {
        if (attempted) return;
        attempted = true;
        try
        {
            var args = new[] { typeof(ItemType), typeof(FoodFreshness) };
            var buyCost = AccessTools.Method(typeof(Vendor), nameof(Vendor.GetItemBuyCost), args)
                ?? throw new Exception("Vendor.GetItemBuyCost not found");
            var sellCost = AccessTools.Method(typeof(Vendor), nameof(Vendor.GetItemSellCost), args)
                ?? throw new Exception("Vendor.GetItemSellCost not found");

            var harmony = new Harmony(ModKit.Guid + ".pricing");
            harmony.Patch(buyCost, postfix: new HarmonyMethod(typeof(Pricing), nameof(BuyPostfix)));
            harmony.Patch(sellCost, postfix: new HarmonyMethod(typeof(Pricing), nameof(SellPostfix)));
            installed = true;
            KitPlugin.L.LogInfo("Pricing: live");
        }
        catch (Exception e) { KitPlugin.L.LogError($"Pricing: missing ({e.Message})"); }
    }

    static void BuyPostfix(Vendor __instance, ItemType itemType, FoodFreshness freshness, ref int __result) =>
        Apply(buy, nameof(BuyPrice), __instance, itemType, freshness, ref __result);

    static void SellPostfix(Vendor __instance, ItemType itemType, FoodFreshness freshness, ref int __result) =>
        Apply(sell, nameof(SellPrice), __instance, itemType, freshness, ref __result);

    // Each handler sees the previous one's price; one that throws is skipped and logged.
    static void Apply(Action<PriceContext> handlers, string name, Vendor vendor, ItemType item,
        FoodFreshness freshness, ref int price)
    {
        if (handlers == null || price <= 0) return;
        try
        {
            var ctx = new PriceContext(vendor, item, freshness, price);
            foreach (Delegate d in handlers.GetInvocationList())
            {
                int before = ctx.Price;
                try { ((Action<PriceContext>)d)(ctx); }
                catch (Exception e)
                {
                    ctx.Price = before;
                    GameEvents.LogFailure($"Pricing.{name}", d, e);
                }
            }
            price = Math.Max(ctx.Price, 1);
        }
        catch (Exception e) { KitPlugin.L.LogError($"Pricing.{name}: {e.Message}"); }
    }
}

/// <summary>Arguments for <see cref="Pricing.BuyPrice"/> and <see cref="Pricing.SellPrice"/>.</summary>
public sealed class PriceContext
{
    /// <summary>The vendor.</summary>
    public Vendor Vendor { get; }

    /// <summary>The item.</summary>
    public ItemType Item { get; }

    /// <summary>The item's freshness.</summary>
    public FoodFreshness Freshness { get; }

    /// <summary>The game's price, before any handler.</summary>
    public int GamePrice { get; }

    /// <summary>The price per item. Set it to change the price; values below 1 become 1.</summary>
    public int Price { get; set; }

    internal PriceContext(Vendor vendor, ItemType item, FoodFreshness freshness, int price)
    {
        Vendor = vendor;
        Item = item;
        Freshness = freshness;
        GamePrice = price;
        Price = price;
    }
}
