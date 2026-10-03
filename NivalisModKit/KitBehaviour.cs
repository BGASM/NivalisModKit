using System;
using UnityEngine;

namespace NivalisModKit;

// The kit's Unity component, on BepInEx's persistent manager object (BasePlugin.AddComponent).
// Runs kit work on the main thread each frame. Kept free of static members: Il2CppInterop
// injects this class into the game, and the work list lives in KitLoop instead.
internal class KitBehaviour : MonoBehaviour
{
    public KitBehaviour(IntPtr ptr) : base(ptr) { }

    public void Update() => KitLoop.Run();
}

// Per-frame work for kit services (config reload now; the Phase 7 scheduler later).
// Each action runs in its own try so one failure doesn't stop the rest.
internal static class KitLoop
{
    internal static event Action Tick;

    internal static void Run()
    {
        Perf.OnFrame();
        var tick = Tick;
        if (tick == null) return;
        foreach (Action a in tick.GetInvocationList())
        {
            long t = Perf.Start();
            try { a(); }
            catch (Exception e) { KitPlugin.L.LogError($"Kit tick {a.Method.DeclaringType?.Name}.{a.Method.Name}: {e}"); }
            if (Perf.On) Perf.Stop($"tick {a.Method.DeclaringType?.Name}.{a.Method.Name}", t);
        }
    }
}
