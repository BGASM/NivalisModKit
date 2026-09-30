using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Nivalis.CraftingSystem;
using Nivalis.GhostSystem.CustomerLoop;

namespace NivalisModKit;

// Installs one patch per event, each in its own try, so a game update that breaks one
// target leaves the rest working. Every event goes through Install.
static class EventPatches
{
    static Harmony harmony;
    static HashSet<string> simulated;
    static int attempted;

    internal static void InstallAll()
    {
        harmony = new Harmony(ModKit.Guid);
        simulated = new HashSet<string>(
            KitPlugin.SimulateMissing.Value.Split(',').Select(s => s.Trim()).Where(s => s != ""));

        // The target type is passed as a function so a type missing from the interop
        // assemblies fails inside that event's try, not the whole installer.
        Install(nameof(GameEvents.BuyIngredientsStarting),
            () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryPurchaseIngredients),
            prefix: nameof(BuyStartPrefix));

        // Last, so it runs after Order Fix flushes its deferred purchases.
        Install(nameof(GameEvents.BuyIngredientsFinished),
            () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryPurchaseIngredients),
            postfix: nameof(BuyFinishPostfix), priority: Priority.Last);

        KitPlugin.L.LogInfo($"Events: {GameEvents.Live.Count} of {attempted} live");
    }

    static void Install(string ev, Func<Type> type, string method,
        string prefix = null, string postfix = null, int priority = Priority.Normal)
    {
        attempted++;
        try
        {
            string name = simulated.Contains(ev) ? method + "_SimulatedMissing" : method;
            Type t = type() ?? throw new Exception("target type not found");
            MethodBase target = AccessTools.Method(t, name)
                ?? throw new Exception($"{t.Name}.{name} not found");

            harmony.Patch(target, prefix: Hm(prefix, priority), postfix: Hm(postfix, priority));
            GameEvents.Live.Add(ev);
            KitPlugin.L.LogInfo($"Event {ev}: live");
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Event {ev}: missing ({e.Message})");
        }
    }

    static HarmonyMethod Hm(string name, int priority) =>
        name == null ? null : new HarmonyMethod(typeof(EventPatches), name) { priority = priority };

    // ---------- patch bodies: never throw into game code ----------

    static void BuyStartPrefix(VenueAreaGhost __instance, IRecipe recipe)
    {
        try { GameEvents.RaiseBuyIngredientsStarting(new BuyIngredientsArgs(__instance, recipe, false)); }
        catch (Exception e) { KitPlugin.L.LogError($"BuyStartPrefix: {e}"); }
    }

    static void BuyFinishPostfix(VenueAreaGhost __instance, IRecipe recipe, bool __result)
    {
        try { GameEvents.RaiseBuyIngredientsFinished(new BuyIngredientsArgs(__instance, recipe, __result)); }
        catch (Exception e) { KitPlugin.L.LogError($"BuyFinishPostfix: {e}"); }
    }
}
