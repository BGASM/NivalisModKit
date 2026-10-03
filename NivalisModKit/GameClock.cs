using System;
using System.Collections.Generic;
using System.Linq;
using Nivalis;
using UnityEngine;

namespace NivalisModKit;

/// <summary>
/// The game clock's speed and pausing, shareable between mods. Each mod sets its own factor under its own
/// owner name; the kit applies the product of all factors, so two mods that each want 2x get 4x, and
/// removing one restores the other's. Factors survive save loads. Main thread only.
/// </summary>
/// <remarks>
/// Clock speed changes how fast game time passes (days, opening hours, curfew); people, animations and
/// cooking run at normal speed, so a venue serves fewer customers per game hour when the clock is faster.
/// Simulation speed is a true fast-forward or slow motion of everything (Unity's time scale); it costs
/// more performance when above 1. The game itself never changes Unity's time scale outside its dev menu.
/// </remarks>
public static class GameClock
{
    /// <summary>Lowest and highest combined clock speed the kit applies.</summary>
    public const float MinClockSpeed = 0.1f, MaxClockSpeed = 20f;

    /// <summary>Lowest and highest combined simulation speed the kit applies.</summary>
    public const float MinSimulationSpeed = 0.25f, MaxSimulationSpeed = 4f;

    static readonly Dictionary<string, float> clockFactors = new(), simFactors = new();
    static float baseTick = -1f;   // the game's own timeTickMultiplier, read once per load

    /// <summary>Raised after the effective clock or simulation speed changes.</summary>
    public static event Action TimeSpeedChanged;

    /// <summary>The combined clock speed factor in effect (1 = the game's normal speed).</summary>
    public static float ClockSpeed => Mathf.Clamp(Product(clockFactors), MinClockSpeed, MaxClockSpeed);

    /// <summary>The combined simulation speed in effect (1 = normal).</summary>
    public static float SimulationSpeed => clampSim(Product(simFactors));

    /// <summary>Game seconds per real second at normal speed (the game's own setting), or -1 before gameplay.</summary>
    public static float BaseGameSecondsPerSecond => baseTick;

    /// <summary>True while the game clock is paused (by the game or any mod).</summary>
    public static bool IsPaused
    {
        get { try { return Manager?.IsPaused ?? false; } catch { return false; } }
    }

    /// <summary>
    /// Sets your clock speed factor (2 = time passes twice as fast). Use your mod's GUID as
    /// <paramref name="owner"/>. Applies immediately in gameplay, otherwise when a save loads.
    /// </summary>
    public static void SetClockSpeed(string owner, float factor)
    {
        if (string.IsNullOrEmpty(owner) || factor <= 0f || float.IsNaN(factor)) return;
        clockFactors[owner] = factor;
        Apply();
    }

    /// <summary>Removes your clock speed factor.</summary>
    public static void ClearClockSpeed(string owner)
    {
        if (owner != null && clockFactors.Remove(owner)) Apply();
    }

    /// <summary>
    /// Sets your simulation speed factor (2 = everything runs twice as fast, 0.5 = slow motion). Use your
    /// mod's GUID as <paramref name="owner"/>.
    /// </summary>
    public static void SetSimulationSpeed(string owner, float factor)
    {
        if (string.IsNullOrEmpty(owner) || factor <= 0f || float.IsNaN(factor)) return;
        simFactors[owner] = factor;
        Apply();
    }

    /// <summary>Removes your simulation speed factor.</summary>
    public static void ClearSimulationSpeed(string owner)
    {
        if (owner != null && simFactors.Remove(owner)) Apply();
    }

    /// <summary>The factors currently set, by owner (for settings screens and diagnostics).</summary>
    public static IReadOnlyDictionary<string, float> ClockFactors => clockFactors;

    /// <inheritdoc cref="ClockFactors"/>
    public static IReadOnlyDictionary<string, float> SimulationFactors => simFactors;

    /// <summary>
    /// Pauses the game clock until the returned handle is disposed. Uses the game's own pause, which any
    /// number of owners can hold: time runs again only when nobody holds one. Returns null outside gameplay.
    /// </summary>
    public static IDisposable Pause(string owner)
    {
        try
        {
            if (Manager == null) return null;
            var lk = TimeOfDayManager.Pause(owner ?? "mod");
            return lk == null ? null : new PauseHandle(lk);
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"GameClock.Pause: {e.Message}");
            return null;
        }
    }

    sealed class PauseHandle : IDisposable
    {
        OverrideableBool.OverrideLock lk;
        public PauseHandle(OverrideableBool.OverrideLock l) => lk = l;
        public void Dispose()
        {
            try { lk?.Release(); } catch { }
            lk = null;
        }
    }

    // ---------- applying ----------

    static TimeOfDayManager Manager => Singleton<TimeOfDayManager>.InstanceExist(out var m) ? m : null;

    static float Product(Dictionary<string, float> f) => f.Values.Aggregate(1f, (a, b) => a * b);
    static float clampSim(float v) => Mathf.Clamp(v, MinSimulationSpeed, MaxSimulationSpeed);

    static float lastClock = 1f, lastSim = 1f;

    // Called after a save loads or a new game starts (the game resets its multiplier then).
    internal static void OnGameStarted()
    {
        baseTick = -1f;
        Apply(force: true);
    }

    static void Apply(bool force = false)
    {
        try
        {
            var m = Manager;
            if (m != null)
            {
                if (baseTick < 0f) baseTick = m.timeTickMultiplier / Math.Max(force ? 1f : lastClock, 0.0001f);
                m.timeTickMultiplier = baseTick * ClockSpeed;
            }
            // Leave Unity's time scale alone unless a mod has set a factor (or one was just removed).
            if (simFactors.Count > 0 || lastSim != 1f) Time.timeScale = SimulationSpeed;

            if (force || ClockSpeed != lastClock || SimulationSpeed != lastSim)
            {
                lastClock = ClockSpeed;
                lastSim = SimulationSpeed;
                KitPlugin.L.LogInfo($"GameClock: clock x{lastClock:0.##} ({Describe(clockFactors)}), simulation x{lastSim:0.##} ({Describe(simFactors)})");
                foreach (Delegate d in TimeSpeedChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
                {
                    try { ((Action)d)(); } catch (Exception e) { GameEvents.LogFailure(nameof(TimeSpeedChanged), d, e); }
                }
            }
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"GameClock: {e.Message}"); }
    }

    static string Describe(Dictionary<string, float> f) =>
        f.Count == 0 ? "none" : string.Join(", ", f.Select(p => $"{p.Key} x{p.Value:0.##}"));
}
