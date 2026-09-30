using Nivalis.CraftingSystem;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;

namespace NivalisModKit;

// Event arguments are classes, so fields can be added later without breaking mods.

/// <summary>Arguments for <see cref="GameEvents.BuyIngredientsStarting"/> and <see cref="GameEvents.BuyIngredientsFinished"/>.</summary>
public sealed class BuyIngredientsArgs
{
    /// <summary>The venue area doing the buying.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The recipe being bought for.</summary>
    public IRecipe Recipe { get; }

    /// <summary>
    /// Whether anything was bought, as the game (and any other mod) reported it.
    /// Always false for BuyIngredientsStarting.
    /// </summary>
    public bool Bought { get; }

    internal BuyIngredientsArgs(VenueAreaGhost area, IRecipe recipe, bool bought)
    {
        Area = area;
        Recipe = recipe;
        Bought = bought;
    }
}

/// <summary>Arguments for <see cref="GameEvents.VenueHour"/>.</summary>
public sealed class VenueHourArgs
{
    /// <summary>The venue area that updated.</summary>
    public VenueAreaGhost Area { get; }

    internal VenueHourArgs(VenueAreaGhost area) => Area = area;
}

/// <summary>Arguments for <see cref="GameEvents.IngredientsPurchased"/>.</summary>
public sealed class IngredientsPurchasedArgs
{
    /// <summary>The venue area that bought.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The ingredient bought.</summary>
    public ItemType Item { get; }

    /// <summary>Number of items bought.</summary>
    public int Count { get; }

    /// <summary>Total price paid.</summary>
    public int TotalPrice { get; }

    internal IngredientsPurchasedArgs(VenueAreaGhost area, ItemType item, int count, int totalPrice)
    {
        Area = area;
        Item = item;
        Count = count;
        TotalPrice = totalPrice;
    }
}
