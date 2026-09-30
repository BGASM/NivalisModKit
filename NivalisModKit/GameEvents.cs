using System;
using System.Collections.Generic;

namespace NivalisModKit;

/// <summary>
/// Game events raised by the kit. Subscribe in your plugin's Load. An event whose patch
/// failed to install never fires; check <see cref="IsAvailable"/> to degrade gracefully.
/// </summary>
public static partial class GameEvents
{
    internal static readonly HashSet<string> Live = new();

    /// <summary>
    /// True if the named event's patch installed and the event will fire.
    /// Pass <c>nameof(GameEvents.SomeEvent)</c>.
    /// </summary>
    public static bool IsAvailable(string name) => name != null && Live.Contains(name);

    /// <summary>A venue area is about to buy ingredients for one recipe.</summary>
    public static event Action<BuyIngredientsArgs> BuyIngredientsStarting;

    /// <summary>
    /// A venue area finished buying ingredients for one recipe. Raised after every other
    /// mod's patches on the purchase, so the purchases are final.
    /// </summary>
    public static event Action<BuyIngredientsArgs> BuyIngredientsFinished;

    internal static void RaiseBuyIngredientsStarting(BuyIngredientsArgs a) =>
        Raise(nameof(BuyIngredientsStarting), BuyIngredientsStarting, a);

    internal static void RaiseBuyIngredientsFinished(BuyIngredientsArgs a) =>
        Raise(nameof(BuyIngredientsFinished), BuyIngredientsFinished, a);

    // Each subscriber runs in its own try, so one failing mod can't stop the others
    // or throw into game code.

    internal static void Raise(string name, Action handler)
    {
        if (handler == null) return;
        foreach (Delegate d in handler.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception e) { LogFailure(name, d, e); }
        }
    }

    internal static void Raise<T>(string name, Action<T> handler, T arg)
    {
        if (handler == null) return;
        foreach (Delegate d in handler.GetInvocationList())
        {
            try { ((Action<T>)d)(arg); }
            catch (Exception e) { LogFailure(name, d, e); }
        }
    }

    static void LogFailure(string name, Delegate d, Exception e)
    {
        string who = d.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
        KitPlugin.L?.LogError($"{name} handler in {who} ({d.Method.DeclaringType?.Name}.{d.Method.Name}) threw: {e}");
    }
}
