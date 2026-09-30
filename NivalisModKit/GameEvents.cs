using System;

namespace NivalisModKit;

/// <summary>Game events raised by the kit. Subscribe in your plugin's Load.</summary>
public static partial class GameEvents
{
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
