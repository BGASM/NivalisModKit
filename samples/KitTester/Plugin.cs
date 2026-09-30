using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using NivalisModKit;

namespace KitTester;

// Logs every kit event. Doubles as the regression test after game updates:
// every kit feature should be exercised here.
[BepInPlugin("will.nivalis.kittester", "Kit Tester", "0.1.0")]
[BepInDependency(ModKit.Guid)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource L;

    static readonly string[] Events =
    {
        nameof(GameEvents.BuyIngredientsStarting),
        nameof(GameEvents.BuyIngredientsFinished),
    };

    public override void Load()
    {
        L = Log;
        L.LogInfo($"Kit Tester loaded against {ModKit.Name} {ModKit.Version}");

        foreach (string ev in Events)
            L.LogInfo($"IsAvailable({ev}) = {GameEvents.IsAvailable(ev)}");

        GameEvents.BuyIngredientsStarting += a =>
            L.LogInfo($"BuyIngredientsStarting: {NameOf(a.Area?.Venue)} / {RecipeName(a)}");
        GameEvents.BuyIngredientsFinished += a =>
            L.LogInfo($"BuyIngredientsFinished: {NameOf(a.Area?.Venue)} / {RecipeName(a)} bought={a.Bought}");
    }

    static string RecipeName(BuyIngredientsArgs a)
    {
        try { return NameOf(a.Recipe?.Output.type); }
        catch { return "?"; }
    }

    // Unity objects print as "name (Type)"; keep the name.
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
