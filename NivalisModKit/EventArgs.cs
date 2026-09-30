using Nivalis.CraftingSystem;
using Nivalis.GhostSystem.CustomerLoop;

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
