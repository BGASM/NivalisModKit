using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Nivalis;
using UnityEngine;

namespace NivalisModKit;

/// <summary>Districts (WorldLocation) and travel distance between them.</summary>
public static class World
{
    /// <summary>Returned by <see cref="Hops"/> when there is no route.</summary>
    public const int Unreachable = -1;

    static List<WorldLocation> locations;
    static readonly Dictionary<IntPtr, Dictionary<IntPtr, int>> HopCache = new();

    /// <summary>All districts. Empty until the game has loaded them (after the title screen).</summary>
    public static IReadOnlyList<WorldLocation> Locations
    {
        get
        {
            if (locations != null) return locations;
            var found = Load();
            if (found.Count > 0) locations = found;   // don't cache an empty list from too early
            return found;
        }
    }

    static List<WorldLocation> Load()
    {
        var list = new List<WorldLocation>();
        try
        {
            if (Singleton<GameSceneManager>.InstanceExist(out var gsm) && gsm.worldLocations != null)
                foreach (var loc in gsm.worldLocations)
                    if (loc != null) list.Add(loc);
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"World: GameSceneManager lookup failed: {e.Message}"); }

        if (list.Count == 0)
        {
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<WorldLocation>()))
                    if (o != null) list.Add(o.Cast<WorldLocation>());
            }
            catch (Exception e) { KitPlugin.L.LogWarning($"World: asset lookup failed: {e.Message}"); }
        }
        return list;
    }

    /// <summary>The district's asset name, e.g. "Meridian Market". Not localized.</summary>
    public static string NameOf(WorldLocation loc)
    {
        if (loc == null) return "?";
        try { return loc.name; }
        catch { return "?"; }
    }

    /// <summary>
    /// Finds a district by asset name or display name, ignoring case and spaces
    /// ("Mountain Town" matches "MountainTown"). Null if none match.
    /// </summary>
    public static WorldLocation Find(string name)
    {
        string want = Squash(name);
        if (want == "") return null;
        foreach (var loc in Locations)
        {
            if (Squash(NameOf(loc)) == want) return loc;
            try { if (Squash(loc.DisplayName) == want) return loc; } catch { }
        }
        return null;
    }

    static string Squash(string s) =>
        s == null ? "" : s.Replace(" ", "").Replace("_", "").ToLowerInvariant();

    /// <summary>
    /// Number of district transitions on the shortest route, 0 for the same district, or
    /// <see cref="Unreachable"/>. Ignores whether districts are unlocked. Cached per start.
    /// </summary>
    public static int Hops(WorldLocation from, WorldLocation to)
    {
        if (from == null || to == null) return Unreachable;
        return HopTable(from).TryGetValue(to.Pointer, out int h) ? h : Unreachable;
    }

    // Breadth-first over WorldLocation.transitions. Same walk as Order Fix 1.0.
    static Dictionary<IntPtr, int> HopTable(WorldLocation start)
    {
        if (HopCache.TryGetValue(start.Pointer, out var table)) return table;

        table = new Dictionary<IntPtr, int> { [start.Pointer] = 0 };
        var queue = new Queue<WorldLocation>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int d = table[cur.Pointer];
            var links = cur.transitions;
            if (links == null) continue;

            for (int i = 0; i < links.Count; i++)
            {
                var to = links[i]?.ToLocation;
                if (to == null || table.ContainsKey(to.Pointer)) continue;
                table[to.Pointer] = d + 1;
                queue.Enqueue(to);
            }
        }

        HopCache[start.Pointer] = table;
        return table;
    }
}
