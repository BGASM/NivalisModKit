using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NivalisModKit;

public static partial class Ui
{
    /// <summary>
    /// Puts the game in menu mode until you dispose the returned handle: mouse cursor on, movement and mouse-look
    /// off, menu keys on, as when one of the game's screens is open. For UI you build yourself (an overlay, a
    /// console); windows from <see cref="CreateWindow"/> already do this. Any number of mods can hold a request; the
    /// game leaves menu mode when the last one is released. Main thread only.
    /// </summary>
    /// <example><code>
    /// IDisposable menu = Ui.RequestMenuMode(MyGuid);   // while my overlay takes input
    /// ...
    /// menu.Dispose();                                   // back to normal play
    /// </code></example>
    /// <remarks>
    /// The game decides menu mode from its open screens (UIManager), so the kit shows an invisible screen that asks
    /// for the mouse while any request is held. It appears in <see cref="GameEvents.PanelShown"/> as "Kit_MenuMode".
    /// </remarks>
    public static IDisposable RequestMenuMode(string owner) => MenuMode.Request(owner);

    /// <summary>True while any mod holds a <see cref="RequestMenuMode"/> request.</summary>
    public static bool IsMenuModeRequested => MenuMode.Held;

    /// <summary>Who holds menu-mode requests now (for diagnostics).</summary>
    public static IReadOnlyList<string> MenuModeOwners => MenuMode.Owners;
}

internal static class MenuMode
{
    sealed class Handle : IDisposable
    {
        internal readonly string Owner;
        internal Handle(string owner) => Owner = owner;
        public void Dispose() => Release(this);
    }

    static readonly List<Handle> held = new();
    static GameObject go;
    static Nivalis.UIPanel screen;
    static bool shown;

    internal static bool Held => held.Count > 0;
    internal static IReadOnlyList<string> Owners => held.Select(h => h.Owner).ToList();

    internal static void Install()
    {
        // The game's UI manager is rebuilt with each scene: tell the new one about a request still held.
        GameEvents.GameReady += Resync;
        GameEvents.GameEnded += () =>
        {
            if (held.Count > 0) KitPlugin.L.LogWarning($"MenuMode: still requested at exit to title by {string.Join(", ", Owners)}");
            Resync();
        };
    }

    internal static IDisposable Request(string owner)
    {
        var h = new Handle(string.IsNullOrEmpty(owner) ? "mod" : owner);
        held.Add(h);
        Apply();
        return h;
    }

    static void Release(Handle h)
    {
        if (held.Remove(h)) Apply();
    }

    static void Apply()
    {
        bool want = held.Count > 0;
        if (want == shown) return;
        if (want && !Build()) return;
        shown = want;
        try { if (want) screen.Show(); else screen.Hide(); }
        catch (Exception e) { KitPlugin.L.LogWarning($"MenuMode: {e.Message}"); }
    }

    static void Resync()
    {
        if (!shown || screen == null) return;
        try { screen.Hide(); } catch { }
        Scheduler.NextFrame(() => { try { if (shown && screen != null) screen.Show(); } catch { } });
    }

    // An empty game screen that requires the mouse, on its own invisible canvas; kept across scenes.
    static bool Build()
    {
        if (screen != null) return true;
        try
        {
            go = new GameObject("Kit_MenuMode");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<RectTransform>();
            go.AddComponent<Canvas>();
            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;
            screen = go.AddComponent<Nivalis.UIPanel>();
            screen._canvasGroup = group;
            screen.requiresMouse = true;
            screen.pauseTimeWhenOpen = false;
            return true;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogError($"MenuMode: could not create the menu-mode screen: {e.Message}");
            if (go != null) UnityEngine.Object.Destroy(go);
            go = null; screen = null;
            return false;
        }
    }
}
