using System;
using System.Collections.Generic;
using System.Linq;
using Nivalis;
using UnityEngine;

namespace NivalisModKit;

/// <summary>
/// The kit's "Mods" button in the pause menu, shared by every mod. Clicking it opens the mod browser: by default
/// the kit's own, which lists each mod's settings and the pages mods add with <see cref="AddPage"/>. A mod can
/// replace the browser with its own (<see cref="SetBrowser"/>). Main thread only.
/// </summary>
/// <example><code>
/// // A page of your own in the kit's browser:
/// ModMenu.AddPage(MyPluginInfo.Guid, "My Mod", w =>
/// {
///     w.AddText("Hello from My Mod.");
///     w.AddButton("Do the thing", DoTheThing);
/// });
/// </code></example>
public static class ModMenu
{
    /// <summary>The owner name of the kit's built-in browser.</summary>
    public const string KitBrowser = ModKit.Guid;

    /// <summary>A page a mod added to the browser.</summary>
    public sealed class Page
    {
        /// <summary>The mod that added it (its GUID).</summary>
        public string Owner { get; internal set; }
        /// <summary>Shown in the browser's list.</summary>
        public string Title { get; internal set; }
        /// <summary>Fills a window that has been cleared for this page.</summary>
        public Action<KitWindow> Build { get; internal set; }
    }

    static readonly List<Page> pages = new();
    static readonly Dictionary<string, Action> browsers = new();   // owner -> open, in registration order
    static readonly List<string> browserOrder = new();

    static readonly HashSet<string> listed = new();

    /// <summary>
    /// Lists all of your mod's settings in the kit's browser (by default only settings tagged with
    /// <see cref="ModSetting"/> appear). Tag settings to hide them or mark them read-only, advanced or restart-only.
    /// Call in your plugin's Load with your plugin GUID.
    /// </summary>
    public static void ListSettings(string pluginGuid)
    {
        if (!string.IsNullOrEmpty(pluginGuid)) listed.Add(pluginGuid);
    }

    /// <summary>True if the mod called <see cref="ListSettings"/>.</summary>
    public static bool IsListed(string pluginGuid) => pluginGuid != null && listed.Contains(pluginGuid);

    /// <summary>Pages added by mods, in the order they were added.</summary>
    public static IReadOnlyList<Page> Pages => pages;

    /// <summary>
    /// Adds (or replaces) your page in the browser. <paramref name="build"/> fills a cleared window each time the
    /// player opens the page; a Back button to the list is added for you.
    /// </summary>
    public static void AddPage(string owner, string title, Action<KitWindow> build)
    {
        if (string.IsNullOrEmpty(owner) || build == null) return;
        var p = pages.FirstOrDefault(x => x.Owner == owner);
        if (p == null) pages.Add(p = new Page { Owner = owner });
        p.Title = string.IsNullOrEmpty(title) ? owner : title;
        p.Build = build;
    }

    /// <summary>Removes your page.</summary>
    public static void RemovePage(string owner) => pages.RemoveAll(p => p.Owner == owner);

    /// <summary>
    /// Replaces the kit's browser: the Mods button calls <paramref name="open"/> instead. Show your window with
    /// <see cref="OpenAsChild"/> so it behaves like the game's own sub-screens. If several mods set a browser, the
    /// one named in the kit's <c>[ModMenu] Browser</c> setting wins, otherwise the last one set; the log names both.
    /// </summary>
    public static void SetBrowser(string owner, Action open)
    {
        if (string.IsNullOrEmpty(owner) || open == null) return;
        string before = BrowserOwner;
        browsers[owner] = open;
        browserOrder.Remove(owner);
        browserOrder.Add(owner);
        if (before != KitBrowser && before != owner)
            KitPlugin.L.LogWarning($"ModMenu: {owner} and {before} both replace the mod browser; using {BrowserOwner}. " +
                                   "Choose with [ModMenu] Browser in the kit's settings.");
        else
            KitPlugin.L.LogInfo($"ModMenu: browser replaced by {owner}");
    }

    /// <summary>Removes your browser; the next one (or the kit's) takes over.</summary>
    public static void ClearBrowser(string owner)
    {
        if (owner == null || !browsers.Remove(owner)) return;
        browserOrder.Remove(owner);
    }

    /// <summary>Who provides the browser now (<see cref="KitBrowser"/> when no mod replaced it).</summary>
    public static string BrowserOwner
    {
        get
        {
            string preferred = KitPlugin.PreferredBrowser?.Value;
            if (!string.IsNullOrEmpty(preferred) && (preferred == KitBrowser || browsers.ContainsKey(preferred))) return preferred;
            return browserOrder.Count > 0 ? browserOrder[^1] : KitBrowser;
        }
    }

    /// <summary>Opens the current browser, as the Mods button does.</summary>
    public static void Open()
    {
        string owner = BrowserOwner;
        try
        {
            if (owner == KitBrowser || !browsers.TryGetValue(owner, out var open)) ConfigBrowser.Open();
            else open();
        }
        catch (Exception e)
        {
            KitPlugin.L.LogError($"ModMenu: browser {owner} failed: {e}");
            if (owner != KitBrowser) { try { ConfigBrowser.Open(); } catch { } }
        }
    }

    /// <summary>
    /// Shows <paramref name="window"/> as a sub-screen of the open pause menu, the way the game opens Settings:
    /// the menu behind it is hidden and stops taking input, Escape closes your window first, and the menu comes
    /// back when it hides. Without an open pause menu it just shows the window.
    /// </summary>
    public static void OpenAsChild(KitWindow window)
    {
        if (window == null) return;
        var menu = OpenPauseMenu();
        if (menu != null)
        {
            try
            {
                menu.OpenAdditionalPanel(window.Panel, menu.menuCanvasGroup);
                // The game only blocks the menu's input; its buttons would show through our window.
                var group = menu.menuCanvasGroup;
                if (group != null)
                {
                    group.alpha = 0f;
                    Action restore = null;
                    restore = () =>
                    {
                        window.Closed -= restore;
                        try { if (group != null) group.alpha = 1f; } catch { }
                    };
                    window.Closed += restore;
                }
            }
            catch (Exception e) { KitPlugin.L.LogWarning($"ModMenu.OpenAsChild: {e.Message}"); }
        }
        window.Show();
    }

    // ---------- the button ----------

    internal const string ButtonName = "Kit_ModsButton";
    static MainMenuUI pauseMenu;

    static MainMenuUI OpenPauseMenu()
    {
        try { return pauseMenu != null && pauseMenu.IsVisible ? pauseMenu : null; }
        catch { return null; }
    }

    internal static void Install()
    {
        GameEvents.PanelShown += a =>
        {
            if (a.Name != nameof(MainMenuUI) || a.Panel == null || !GameEvents.IsInGame) return;
            if (KitPlugin.ModMenuEnabled != null && !KitPlugin.ModMenuEnabled.Value) return;
            var menu = a.Panel.TryCast<MainMenuUI>();
            if (menu == null) return;
            pauseMenu = menu;
            AddButton(menu);
        };
    }

    // Once per menu object: a copy of its Settings button, placed after it.
    static void AddButton(MainMenuUI menu)
    {
        try
        {
            var settings = menu.settingsButton;
            if (settings == null || settings.transform.parent == null) return;
            if (settings.transform.parent.Find(ButtonName) != null) return;
            var go = Ui.CloneButton(settings, KitPlugin.ModMenuLabel?.Value ?? "Mods", Open);
            if (go == null) { KitPlugin.L.LogWarning("ModMenu: could not add the Mods button"); return; }
            go.name = ButtonName;
            KitPlugin.L.LogInfo($"ModMenu: Mods button added to {menu.gameObject.name}");
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"ModMenu: {e.Message}"); }
    }
}
