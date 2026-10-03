using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Nivalis.GhostSystem.Ai;
using UnityEngine;

namespace NivalisModKit;

/// <summary>
/// Task priority for characters, staff included. Each character (<c>Person</c>) points to an
/// <c>ActionPriorityList</c> asset: an ordered list of the actions it considers, first = most
/// important. Game patch 2 (October 2, 2026) set the staff order to serve, process ingredient, make meal,
/// take order, deliver, buy, manage complaint, cleanup meal, cleanup trash, cleanup storage.
/// </summary>
/// <remarks>
/// Experimental: the lists are shared assets, so reordering one changes every character that uses it.
/// Changes last until the game restarts. Use the dev bridge's <c>/priorities</c> path to see the lists.
/// </remarks>
public static class Staff
{
    /// <summary>Every loaded priority list.</summary>
    public static IReadOnlyList<ActionPriorityList> PriorityLists
    {
        get
        {
            var lists = new List<ActionPriorityList>();
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<ActionPriorityList>()))
                {
                    var l = o.TryCast<ActionPriorityList>();
                    if (l != null) lists.Add(l);
                }
            }
            catch { }
            return lists;
        }
    }

    /// <summary>The priority list a character uses, or null.</summary>
    public static ActionPriorityList PriorityListOf(Person person)
    {
        try { return person?.ActionPriorityList; }
        catch { return null; }
    }

    /// <summary>The actions in a list, in priority order (first = most important).</summary>
    public static IReadOnlyList<AgentActionType> Order(ActionPriorityList list)
    {
        var order = new List<AgentActionType>();
        try
        {
            var array = list?.actionArray;
            if (array == null) return order;
            for (int i = 0; i < array.Length; i++) order.Add(array[i]);
        }
        catch { }
        return order;
    }

    /// <summary>An action's asset name, e.g. as listed by the bridge's <c>/priorities</c>.</summary>
    public static string NameOf(AgentActionType action)
    {
        try { return action?.name; }
        catch { return null; }
    }

    /// <summary>
    /// Sets a list's order. <paramref name="order"/> must contain exactly the list's current actions,
    /// rearranged; anything else is refused (returns false) so a typo can't drop a task.
    /// </summary>
    public static bool SetOrder(ActionPriorityList list, IEnumerable<AgentActionType> order)
    {
        if (list == null || order == null) return false;
        try
        {
            var current = Order(list);
            var wanted = order.ToList();
            if (wanted.Count != current.Count) return false;
            var have = current.Select(a => a?.Pointer ?? IntPtr.Zero).OrderBy(p => p.ToInt64()).ToList();
            var want = wanted.Select(a => a?.Pointer ?? IntPtr.Zero).OrderBy(p => p.ToInt64()).ToList();
            if (!have.SequenceEqual(want)) return false;

            var array = new Il2CppReferenceArray<AgentActionType>(wanted.Count);
            for (int i = 0; i < wanted.Count; i++) array[i] = wanted[i];
            list.actionArray = array;
            return true;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Staff.SetOrder: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reorders a list by action names (see <see cref="NameOf"/>), most important first. Names not
    /// listed keep their relative order after the named ones. Returns false if a name isn't in the list.
    /// </summary>
    public static bool SetOrder(ActionPriorityList list, params string[] names)
    {
        var current = Order(list);
        var named = new List<AgentActionType>();
        foreach (var n in names ?? Array.Empty<string>())
        {
            var a = current.FirstOrDefault(x => NameOf(x) == n);
            if (a == null) return false;
            if (!named.Contains(a)) named.Add(a);
        }
        return SetOrder(list, named.Concat(current.Where(a => !named.Contains(a))));
    }
}
