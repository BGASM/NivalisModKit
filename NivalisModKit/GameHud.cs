using System.Collections.Generic;
using Nivalis;

namespace NivalisModKit;

public static partial class Ui
{
    /// <summary>
    /// How visible a HUD overlay should be now, 0..1: the game's own HUD visibility and fade (so dialogue and
    /// cutscenes hide it), and 0 while a menu is open (<see cref="IsMenuOpen"/>) or menu mode is requested. Multiply your
    /// overlay's alpha by it every frame.
    /// </summary>
    [Experimental("New in 0.4.")]
    public static float GameHudAlpha => GameHud.Alpha();

    /// <summary>True when a HUD overlay should show (<see cref="GameHudAlpha"/> above 0).</summary>
    [Experimental("New in 0.4.")]
    public static bool IsGameHudVisible => GameHud.Alpha() > 0f;

    /// <summary>
    /// True while one of the game's screens is open over play: any that takes the mouse (pause menu, journal, shops,
    /// venue screens), the travel map and the loading screen. The game's HUD stays up underneath those, so its own
    /// visibility isn't enough.
    /// </summary>
    [Experimental("New in 0.4.")]
    public static bool IsMenuOpen => GameHud.MenuOpen();
}

internal static class GameHud
{
    static readonly List<UIPanel> openMenus = new();

    // Screens over play that don't take the mouse: the travel map (fast travel) and the loading screen.
    static readonly HashSet<string> Covering = new() { "MapUI", "MapTileMenu", "LoadingScreenUI" };

    internal static void Install()
    {
        GameEvents.PanelShown += a =>
        {
            var p = a.Panel;
            if (p != null && (p.requiresMouse || Covering.Contains(a.Name)) && !openMenus.Exists(m => m != null && m.Pointer == p.Pointer))
                openMenus.Add(p);
        };
        GameEvents.PanelHidden += a =>
        {
            var p = a.Panel;
            if (p != null) openMenus.RemoveAll(m => m == null || m.Pointer == p.Pointer);
        };
    }

    // Tracked panels still visible (rechecked, in case a close went unreported).
    internal static bool MenuOpen()
    {
        openMenus.RemoveAll(m => m == null || !m.IsVisible);
        return openMenus.Count > 0;
    }

    internal static float Alpha()
    {
        if (!GameEvents.IsInGame || Ui.IsMenuModeRequested || MenuOpen()) return 0f;
        try
        {
            var hud = HeadsUpDisplayUI.Instance;
            if (hud == null || !hud.IsVisible) return 0f;
            var cg = hud._canvasGroup;
            return cg != null ? cg.alpha : 1f;
        }
        catch { return 0f; }
    }
}
