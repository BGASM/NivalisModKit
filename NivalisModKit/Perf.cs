using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace NivalisModKit;

// Frame timing for diagnosing lag ([Debug] FrameTiming). Measures how long the kit's own work takes
// (event handlers, tuning hooks, patch bodies, per-frame ticks) and logs frames slower than a
// threshold with the kit's share of them. Off by default: then each measured call costs one bool check.
internal static class Perf
{
    internal static bool On;
    internal static float ThresholdMs = 50f;

    sealed class Stat { public double TotalMs, MaxMs, FrameMs; public long Calls; }
    static readonly Dictionary<string, Stat> stats = new();
    static readonly List<string> recentSpikes = new();
    static double frameKitMs;
    static long spikes, suppressed;
    static double worstMs;
    static float lastLogAt = -10f;

    internal static long Start() => On ? Stopwatch.GetTimestamp() : 0;

    internal static void Stop(string name, long start)
    {
        if (!On || start == 0) return;
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        if (!stats.TryGetValue(name, out var s)) stats[name] = s = new Stat();
        s.TotalMs += ms; s.Calls++; s.FrameMs += ms;
        if (ms > s.MaxMs) s.MaxMs = ms;
        frameKitMs += ms;
    }

    // Called at the start of each frame (KitLoop): judges the frame that just ended.
    internal static void OnFrame()
    {
        if (!On) return;
        float frameMs = Time.unscaledDeltaTime * 1000f;
        if (frameMs > ThresholdMs)
        {
            spikes++;
            if (frameMs > worstMs) worstMs = frameMs;
            string top = string.Join(", ", stats.Where(p => p.Value.FrameMs > 0.05)
                .OrderByDescending(p => p.Value.FrameMs).Take(3)
                .Select(p => $"{p.Key} {p.Value.FrameMs:0.0} ms"));
            string line = $"{DateTime.Now:HH:mm:ss} frame {frameMs:0} ms, kit {frameKitMs:0.0} ms" +
                          (top == "" ? "" : $" ({top})") + $" at {GameTime.Hour:00}:{GameTime.Minute:00}";
            recentSpikes.Add(line);
            if (recentSpikes.Count > 30) recentSpikes.RemoveAt(0);
            if (Time.unscaledTime - lastLogAt >= 1f)
            {
                KitPlugin.L.LogInfo($"Slow frame: {line}" + (suppressed > 0 ? $" (+{suppressed} more since last report)" : ""));
                lastLogAt = Time.unscaledTime;
                suppressed = 0;
            }
            else suppressed++;
        }
        frameKitMs = 0;
        foreach (var s in stats.Values) s.FrameMs = 0;

        if (Time.unscaledTime - lastPanelReport >= 60f)
        {
            lastPanelReport = Time.unscaledTime;
            var top = EventPatches.panelCalls.Values.OrderByDescending(p => p.Calls).FirstOrDefault();
            if (top != null && top.Calls > 0)
                KitPlugin.L.LogInfo($"Panel state calls: busiest {top.Name}: {top.Calls} calls, {top.Changes} real changes");
        }
    }
    static float lastPanelReport;

    internal static object Summary() => new
    {
        on = On, thresholdMs = ThresholdMs, slowFrames = spikes, worstFrameMs = Math.Round(worstMs),
        recent = recentSpikes.ToArray(),
        panelStateCalls = EventPatches.PanelCallStats(),
        kitTime = stats.OrderByDescending(p => p.Value.TotalMs).Take(25).Select(p => new
        {
            name = p.Key, calls = p.Value.Calls, totalMs = Math.Round(p.Value.TotalMs, 1),
            avgMs = Math.Round(p.Value.TotalMs / Math.Max(1, p.Value.Calls), 3), maxMs = Math.Round(p.Value.MaxMs, 2),
        }).ToArray(),
    };

    internal static void Reset()
    {
        stats.Clear(); recentSpikes.Clear();
        spikes = suppressed = 0; worstMs = 0; frameKitMs = 0;
    }
}
