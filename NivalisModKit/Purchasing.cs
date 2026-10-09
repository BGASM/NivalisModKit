using System;
using System.Collections.Generic;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.Economy;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;

namespace NivalisModKit;

/// <summary>
/// Manager ingredient purchasing. The game restocks each dish's ingredients up to the venue's supply
/// target, buying from vendors cheapest first (then most stock) until each ingredient's need is met,
/// within the venue's money and daily restock budget. Once any mod subscribes to
/// <see cref="VendorOrdering"/> or <see cref="OrderQuantity"/>, the kit lets handlers change the
/// supply target and the vendor order; the game still does the buying. With no subscribers the kit
/// leaves purchasing alone.
/// </summary>
/// <remarks>
/// Changed in 0.6.2 for the game's patch 4 restock: <see cref="OrderQuantity"/> now sets the dish's
/// supply target rather than an ingredient's quantity, offers are in the game's new order (cheapest
/// first), and a failed purchase no longer stops the recipe (the game moves to the next vendor).
/// </remarks>
public static class Purchasing
{
    static Action<VendorOrderingContext> ordering;
    static Action<OrderQuantityContext> quantity;

    /// <summary>
    /// Reorder, or remove from, the vendors offered for one ingredient. Raised once per ingredient
    /// the venue is short of, per recipe, after <see cref="OrderQuantity"/>. The game buys from the
    /// offers in this order until the need is met. Handlers run in the order added, each seeing the
    /// previous one's result. Adding offers isn't allowed; an invalid list is discarded and logged.
    /// </summary>
    public static event Action<VendorOrderingContext> VendorOrdering
    {
        add { ordering += value; Added(nameof(VendorOrdering), value, ordering); }
        remove { ordering -= value; }
    }

    /// <summary>
    /// Change a dish's supply target for this restock: the venue tops each of the recipe's
    /// ingredients up to this many (limited by storage space, money and the restock budget). Raised
    /// once per recipe, before any vendor is asked. Handlers run in the order added, each seeing the
    /// previous one's <c>Quantity</c>. Experimental.
    /// </summary>
    public static event Action<OrderQuantityContext> OrderQuantity
    {
        add { quantity += value; Added(nameof(OrderQuantity), value, quantity); }
        remove { quantity -= value; }
    }

    /// <summary>
    /// Raised for each vendor offer: once the game buys from it, a purchase fails, or (when the
    /// ingredient is done) it was skipped. Read-only; subscribing doesn't turn the pipeline on.
    /// </summary>
    public static event Action<PurchaseDecisionArgs> Decision;

    /// <summary>
    /// True once the pipeline's hooks installed. False before any mod subscribes, or if the
    /// hooks failed (logged at startup); handlers then never run.
    /// </summary>
    public static bool IsAvailable => PurchasePipeline.Installed;

    internal static bool Active => ordering != null || quantity != null;
    internal static Action<VendorOrderingContext> OrderingHandlers => ordering;
    internal static Action<OrderQuantityContext> QuantityHandlers => quantity;

    internal static void RaiseDecision(PurchaseDecisionArgs a) =>
        GameEvents.Raise($"Purchasing.{nameof(Decision)}", Decision, a);

    static void Added(string name, Delegate handler, Delegate all)
    {
        string who = handler?.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
        int n = all?.GetInvocationList().Length ?? 0;
        KitPlugin.L.LogInfo($"Purchasing.{name}: handler added by {who}" +
                            (n > 1 ? $" ({n} handlers, run in the order added)" : ""));

        // A missing game type fails when EnsureInstalled is compiled, so catch here,
        // not inside it, to keep that out of the subscriber's Load.
        try { PurchasePipeline.EnsureInstalled(); }
        catch (Exception e) { KitPlugin.L.LogError($"Purchasing pipeline: missing ({e.Message})"); }
    }
}

/// <summary>One vendor's offer for an ingredient, from the game's vendor list.</summary>
public sealed class VendorOffer
{
    /// <summary>The vendor.</summary>
    public Vendor Vendor { get; }

    /// <summary>The ingredient.</summary>
    public ItemType Item { get; }

    /// <summary>Price per item (fresh), before the venue's barter discount.</summary>
    public int Price { get; }

    /// <summary>The vendor's stock of the item.</summary>
    public int Stock { get; }

    /// <summary>District hops from the buying venue, or <see cref="World.Unreachable"/>.</summary>
    public int Hops { get; }

    /// <summary>
    /// The most the game would buy here if this vendor came first: min(stock, the ingredient's need).
    /// The game also limits it by storage space, money and the restock budget.
    /// </summary>
    public int Amount { get; }

    /// <summary>Same as <see cref="Amount"/> since 0.6.2 (the game no longer has a per-recipe money limit).</summary>
    public int MaxAmount => Amount;

    /// <summary>The game's order (cheapest first, then most stock), 0 first.</summary>
    public int Sequence { get; }

    internal VendorOffer(Vendor vendor, ItemType item, int price, int stock, int hops, int amount, int sequence)
    {
        Vendor = vendor;
        Item = item;
        Price = price;
        Stock = stock;
        Hops = hops;
        Amount = amount;
        Sequence = sequence;
    }
}

/// <summary>Arguments for <see cref="Purchasing.OrderQuantity"/>.</summary>
public sealed class OrderQuantityContext
{
    /// <summary>The venue area buying.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The recipe being bought for.</summary>
    public IRecipe Recipe { get; }

    /// <summary>The dish the recipe makes (since 0.6.2; it was the ingredient).</summary>
    public ItemType Item { get; }

    /// <summary>The venue's district, or null.</summary>
    public WorldLocation VenueLocation { get; }

    /// <summary>
    /// The game's supply target for the dish: the venue's setting (Low 2, Medium 5, High 10), or for
    /// Auto its recent sales of the dish (at least 2).
    /// </summary>
    public int GameQuantity { get; }

    /// <summary>
    /// The supply target to use: each ingredient is topped up to this many. Starts at
    /// <see cref="GameQuantity"/>; negative values count as 0.
    /// </summary>
    public int Quantity { get; set; }

    internal OrderQuantityContext(VenueAreaGhost area, IRecipe recipe, ItemType item,
        WorldLocation venueLocation, int gameQuantity)
    {
        Area = area;
        Recipe = recipe;
        Item = item;
        VenueLocation = venueLocation;
        GameQuantity = gameQuantity;
        Quantity = gameQuantity;
    }
}

/// <summary>Arguments for <see cref="Purchasing.VendorOrdering"/>.</summary>
public sealed class VendorOrderingContext
{
    /// <summary>The venue area buying.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The recipe being bought for.</summary>
    public IRecipe Recipe { get; }

    /// <summary>The ingredient.</summary>
    public ItemType Item { get; }

    /// <summary>The venue's district, or null.</summary>
    public WorldLocation VenueLocation { get; }

    /// <summary>The ingredient's need: the supply target (after OrderQuantity) minus what the venue has.</summary>
    public int Quantity { get; }

    /// <summary>
    /// The offers, in buying order. Starts in the game's order (cheapest first, then most stock).
    /// Reorder in place, remove offers, or assign a new list of the same offers.
    /// </summary>
    public List<VendorOffer> Offers { get; set; }

    internal VendorOrderingContext(VenueAreaGhost area, IRecipe recipe, ItemType item,
        WorldLocation venueLocation, int quantity, List<VendorOffer> offers)
    {
        Area = area;
        Recipe = recipe;
        Item = item;
        VenueLocation = venueLocation;
        Quantity = quantity;
        Offers = offers;
    }
}

/// <summary>What the pipeline did with one vendor offer.</summary>
public enum PurchaseResult
{
    /// <summary>Bought <see cref="PurchaseDecisionArgs.Amount"/> items.</summary>
    Bought,
    /// <summary>
    /// Not bought from: the need was already met, or the game stopped buying the ingredient (the
    /// venue couldn't afford this vendor's price, or the restock budget ran out).
    /// </summary>
    Skipped,
    /// <summary>The game refused the purchase. It moves on to the next vendor.</summary>
    Failed,
}

/// <summary>Arguments for <see cref="Purchasing.Decision"/>.</summary>
public sealed class PurchaseDecisionArgs
{
    /// <summary>The venue area buying.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The recipe being bought for.</summary>
    public IRecipe Recipe { get; }

    /// <summary>The offer decided on.</summary>
    public VendorOffer Offer { get; }

    /// <summary>What happened.</summary>
    public PurchaseResult Result { get; }

    /// <summary>Items bought, or attempted for a failure. 0 when skipped.</summary>
    public int Amount { get; }

    /// <summary>Number of offers for this ingredient after VendorOrdering.</summary>
    public int OfferCount { get; }

    internal PurchaseDecisionArgs(VenueAreaGhost area, IRecipe recipe, VendorOffer offer,
        PurchaseResult result, int amount, int offerCount)
    {
        Area = area;
        Recipe = recipe;
        Offer = offer;
        Result = result;
        Amount = amount;
        OfferCount = offerCount;
    }
}
