using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using NivalisModKit;
using SCG = System.Collections.Generic;

namespace QuantityTester;

// Example of Purchasing.OrderQuantity: multiplies the supply target managers restock to, using only kit
// events. Test harness for the pipeline running alongside Better Supplier Choice. Off by default.
[BepInPlugin("bgasm.nivalis.quantitytester", "Quantity Tester", "0.1.0")]
[BepInDependency(ModKit.Guid)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource L;
    static ConfigEntry<float> Multiplier;
    static ConfigEntry<bool> PlayerOnly;

    // Per recipe: the dish's supply target (game's, changed) and what was bought, by ingredient.
    static string dish;
    static int gameTarget, target;
    static readonly SCG.Dictionary<string, int> Bought = new();

    public override void Load()
    {
        L = Log;
        var enabled = Config.Bind("General", "Enabled", false,
            "Multiply the supply target managers restock to. Subscribing turns on the kit's purchasing pipeline.");
        Multiplier = Config.Bind("General", "Multiplier", 2.0f, "Supply target multiplier.");
        PlayerOnly = Config.Bind("General", "PlayerOnly", true, "Only change orders for the player's venues.");

        if (!enabled.Value)
        {
            L.LogInfo("Quantity Tester loaded, disabled");
            return;
        }

        Purchasing.OrderQuantity += OnQuantity;
        Purchasing.Decision += OnDecision;
        GameEvents.BuyIngredientsStarting += _ => { dish = null; Bought.Clear(); };
        GameEvents.BuyIngredientsFinished += OnFinished;
        L.LogInfo($"Quantity Tester loaded, Multiplier = {Multiplier.Value}, PlayerOnly = {PlayerOnly.Value}");
    }

    static void OnQuantity(OrderQuantityContext ctx)
    {
        if (PlayerOnly.Value && (ctx.Area == null || !ctx.Area.PlayerOwned)) return;
        ctx.Quantity = (int)Math.Ceiling(ctx.Quantity * Multiplier.Value);
        dish = NameOf(ctx.Item);
        gameTarget = ctx.GameQuantity;
        target = ctx.Quantity;
    }

    static void OnDecision(PurchaseDecisionArgs d)
    {
        if (dish == null || d.Result != PurchaseResult.Bought) return;
        string item = NameOf(d.Offer.Item);
        Bought[item] = (Bought.TryGetValue(item, out int n) ? n : 0) + d.Amount;
    }

    static void OnFinished(BuyIngredientsArgs a)
    {
        if (dish == null) return;
        L.LogInfo($"Quantity {NameOf(a.Area?.Venue)} / {dish}: target {gameTarget} -> {target}; bought " +
                  (Bought.Count == 0 ? "nothing" : string.Join(", ", Bought.Select(kv => $"{kv.Key} x{kv.Value}"))));
        dish = null;
        Bought.Clear();
    }

    static string NameOf(Il2CppSystem.Object o)
    {
        if (o == null) return "?";
        try
        {
            string s = o.ToString();
            int i = s.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : s;
        }
        catch { return "?"; }
    }
}
