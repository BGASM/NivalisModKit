using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using NivalisModKit;
using SCG = System.Collections.Generic;

namespace QuantityTester;

// Example of Purchasing.OrderQuantity: multiplies how much managers buy, using only kit events.
// Test harness for the pipeline running alongside Order Fix. Off by default.
[BepInPlugin("will.nivalis.quantitytester", "Quantity Tester", "0.1.0")]
[BepInDependency(ModKit.Guid)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource L;
    static ConfigEntry<float> Multiplier;
    static ConfigEntry<bool> PlayerOnly;

    // Per recipe: item name -> (game quantity, target, bought)
    static readonly SCG.Dictionary<string, (int game, int target, int bought)> Changed = new();

    public override void Load()
    {
        L = Log;
        var enabled = Config.Bind("General", "Enabled", false,
            "Multiply manager ingredient orders. Subscribing turns on the kit's purchasing pipeline.");
        Multiplier = Config.Bind("General", "Multiplier", 2.0f, "Order quantity multiplier.");
        PlayerOnly = Config.Bind("General", "PlayerOnly", true, "Only change orders for the player's venues.");

        if (!enabled.Value)
        {
            L.LogInfo("Quantity Tester loaded, disabled");
            return;
        }

        Purchasing.OrderQuantity += OnQuantity;
        Purchasing.Decision += OnDecision;
        GameEvents.BuyIngredientsStarting += _ => Changed.Clear();
        GameEvents.BuyIngredientsFinished += OnFinished;
        L.LogInfo($"Quantity Tester loaded, Multiplier = {Multiplier.Value}, PlayerOnly = {PlayerOnly.Value}");
    }

    static void OnQuantity(OrderQuantityContext ctx)
    {
        if (PlayerOnly.Value && (ctx.Area == null || !ctx.Area.PlayerOwned)) return;
        ctx.Quantity = (int)Math.Ceiling(ctx.Quantity * Multiplier.Value);
        Changed[NameOf(ctx.Item)] = (ctx.GameQuantity, ctx.Quantity, 0);
    }

    static void OnDecision(PurchaseDecisionArgs d)
    {
        if (d.Result != PurchaseResult.Bought) return;
        string item = NameOf(d.Offer.Item);
        if (Changed.TryGetValue(item, out var c))
            Changed[item] = (c.game, c.target, c.bought + d.Amount);
    }

    static void OnFinished(BuyIngredientsArgs a)
    {
        foreach (var kv in Changed)
            L.LogInfo($"Quantity {NameOf(a.Area?.Venue)} / {kv.Key}: game {kv.Value.game}, " +
                      $"target {kv.Value.target}, bought {kv.Value.bought}");
        Changed.Clear();
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
