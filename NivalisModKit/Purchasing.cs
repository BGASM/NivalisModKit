using System;
using System.Collections.Generic;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.Economy;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;

namespace NivalisModKit;

/// <summary>
/// Manager ingredient purchasing. Once any mod subscribes to <see cref="VendorOrdering"/> or
/// <see cref="OrderQuantity"/>, the kit takes over each recipe's purchases: it collects the vendor
/// offers the game makes, lets handlers set the quantity and the vendor order, then buys only up to
/// that quantity, stopping at the first failed purchase (almost always the venue out of money).
/// With no subscribers the kit leaves purchasing to the game.
/// </summary>
public static class Purchasing
{
    static Action<VendorOrderingContext> ordering;
    static Action<OrderQuantityContext> quantity;

    /// <summary>
    /// Reorder, or remove from, the vendors offered for one ingredient. Raised once per ingredient
    /// per recipe, after <see cref="OrderQuantity"/>. Handlers run in the order added, each seeing the
    /// previous one's result. Adding offers isn't allowed; an invalid list is discarded and logged.
    /// </summary>
    public static event Action<VendorOrderingContext> VendorOrdering
    {
        add { ordering += value; Added(nameof(VendorOrdering), value, ordering); }
        remove { ordering -= value; }
    }

    /// <summary>
    /// Change how many of one ingredient to buy for a recipe. Raised once per ingredient per recipe.
    /// Handlers run in the order added, each seeing the previous one's <c>Quantity</c>.
    /// </summary>
    public static event Action<OrderQuantityContext> OrderQuantity
    {
        add { quantity += value; Added(nameof(OrderQuantity), value, quantity); }
        remove { quantity -= value; }
    }

    /// <summary>
    /// Raised for each vendor offer as the pipeline buys, skips it, or a purchase fails. Read-only;
    /// subscribing doesn't turn the pipeline on.
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

/// <summary>One vendor's offer for an ingredient, as the game made it.</summary>
public sealed class VendorOffer
{
    /// <summary>The vendor.</summary>
    public Vendor Vendor { get; }

    /// <summary>The ingredient.</summary>
    public ItemType Item { get; }

    /// <summary>Price per item, before the venue's barter discount. int.MaxValue if unknown.</summary>
    public int Price { get; }

    /// <summary>The vendor's stock of the item.</summary>
    public int Stock { get; }

    /// <summary>District hops from the buying venue, or <see cref="World.Unreachable"/>.</summary>
    public int Hops { get; }

    /// <summary>What the game asked this vendor for: min(stock, game quantity, money limit / price).</summary>
    public int Amount { get; }

    /// <summary>
    /// The most this vendor can supply within the venue's per-recipe money limit: min(stock,
    /// limit / price). Used instead of <see cref="Amount"/> when OrderQuantity raises the quantity.
    /// </summary>
    public int MaxAmount { get; }

    /// <summary>The order the game offered vendors in, 0 first.</summary>
    public int Sequence { get; }

    // Raw copies of the game's call, replayed when the pipeline buys. Freed after each recipe.
    internal IntPtr VendorPtr, Req, Temp, Method;
    internal float Discount;

    internal VendorOffer(Vendor vendor, ItemType item, int price, int stock, int hops,
        int amount, int maxAmount, int sequence)
    {
        Vendor = vendor;
        Item = item;
        Price = price;
        Stock = stock;
        Hops = hops;
        Amount = amount;
        MaxAmount = maxAmount;
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

    /// <summary>The ingredient.</summary>
    public ItemType Item { get; }

    /// <summary>The venue's district, or null.</summary>
    public WorldLocation VenueLocation { get; }

    /// <summary>How many the game wanted.</summary>
    public int GameQuantity { get; }

    /// <summary>How many to buy. Starts at <see cref="GameQuantity"/>; negative values count as 0.</summary>
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

    /// <summary>How many will be bought, after OrderQuantity.</summary>
    public int Quantity { get; }

    /// <summary>
    /// The offers, in buying order. Starts in the game's order. Reorder in place, remove offers,
    /// or assign a new list of the same offers.
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
    /// <summary>Not needed: the quantity was already filled, or the vendor can supply none.</summary>
    Skipped,
    /// <summary>The game refused the purchase, usually for lack of money. The recipe's buying stops.</summary>
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
