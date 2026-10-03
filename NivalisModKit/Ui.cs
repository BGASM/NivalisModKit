using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Nivalis;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Locale.UI;
using Nivalis.Localization;
using Nivalis.UI;
using Nivalis.UI.InGameMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisModKit;

/// <summary>
/// The game's UI: which screens are open, and the game's own notifications and dialogs.
/// Screens are <c>UIPanel</c> MonoBehaviours placed in the scene (prefabs), shown and hidden by the
/// game; <see cref="GameEvents.PanelShown"/> and <see cref="GameEvents.PanelHidden"/> report them.
/// Call these from the main thread (event handlers, Update, the Scheduler).
/// </summary>
public static partial class Ui
{
    /// <summary>True while the game's UI is shown (the player can hide it for screenshots).</summary>
    public static bool IsVisible
    {
        get { try { return UIManager.IsVisible; } catch { return false; } }
    }

    /// <summary>True while one of the game's popup dialogs is on screen.</summary>
    public static bool IsDialogOpen
    {
        get { try { return PopupDialog.IsPopupVisible; } catch { return false; } }
    }

    /// <summary>
    /// The panel's type name, e.g. <c>ShopUiVendorPanel</c> or <c>EndOfDayWindow</c>. Stable across
    /// game builds as long as the class keeps its name; use it to recognise screens.
    /// </summary>
    public static string NameOf(UIPanel panel)
    {
        if (panel == null) return null;
        try { return panel.GetIl2CppType().Name; }
        catch { return null; }
    }

    /// <summary>Every panel that is currently visible. Searches all loaded screens: call when needed, not every frame.</summary>
    public static IReadOnlyList<UIPanel> OpenPanels
    {
        get
        {
            var open = new List<UIPanel>();
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<UIPanel>()))
                {
                    var p = o.TryCast<UIPanel>();
                    if (p != null && p.IsVisible) open.Add(p);
                }
            }
            catch { }
            return open;
        }
    }

    /// <summary>
    /// The first loaded panel of the given type name (see <see cref="NameOf"/>), open or not, or null.
    /// <c>"Type:ObjectName"</c> picks a specific one (e.g. <c>"MainMenuUI:P_PauseMenuUI"</c>).
    /// Includes inactive panels. Searches all loaded screens: call once and keep the result, not every frame.
    /// </summary>
    public static UIPanel Find(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        // "Type:ObjectName" picks one of several panels of a type (e.g. MainMenuUI:P_PauseMenuUI).
        string objectName = null;
        int colon = typeName.IndexOf(':');
        if (colon > 0) { objectName = typeName.Substring(colon + 1); typeName = typeName.Substring(0, colon); }
        try
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<UIPanel>()))
            {
                var p = o.TryCast<UIPanel>();
                if (p == null || NameOf(p) != typeName) continue;
                if (objectName != null && p.gameObject.name != objectName) continue;
                return p;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Shows a notification through the game's own notification feed (the toasts at the screen edge).
    /// Returns false if the game isn't ready to show one.
    /// </summary>
    public static bool Notify(string header, string text)
    {
        try
        {
            if (!Singleton<NotificationManager>.InstanceExist(out var nm) || nm == null) return false;
            nm.CreateMessage(header ?? "", text ?? "", null);
            return true;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Ui.Notify: {e.Message}");
            return false;
        }
    }

    // ---------- adding to screens: clone an existing element ----------

    /// <summary>
    /// The buttons under a panel (inactive ones included), with their paths and label text, to pick a
    /// template for <see cref="CloneButton"/>. The dev bridge shows the same at <c>/ui?panel=Name</c>.
    /// </summary>
    public static IReadOnlyList<(Button button, string path, string label)> ButtonsIn(UIPanel panel)
    {
        var found = new List<(Button, string, string)>();
        if (panel == null) return found;
        try
        {
            Transform root = panel.transform;
            foreach (var b in panel.GetComponentsInChildren<Button>(true))
                found.Add((b, PathOf(b.transform, root), LabelOf(b)));
        }
        catch { }
        return found;
    }

    /// <summary>The button's label text, or null if it has none.</summary>
    public static string LabelOf(Button button)
    {
        try { return button?.GetComponentInChildren<TMP_Text>(true)?.text; }
        catch { return null; }
    }

    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var cur = t; cur != null && cur != root; cur = cur.parent) parts.Add(cur.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>The text labels under a panel (inactive included), with paths and current text: templates for <see cref="CloneText"/>.</summary>
    public static IReadOnlyList<(TMP_Text text, string path, string value)> TextsIn(UIPanel panel)
    {
        var found = new List<(TMP_Text, string, string)>();
        if (panel == null) return found;
        try
        {
            Transform root = panel.transform;
            foreach (var t in panel.GetComponentsInChildren<TMP_Text>(true))
                found.Add((t, PathOf(t.transform, root), t.text));
        }
        catch { }
        return found;
    }

    /// <summary>The child at a path under a panel (as listed by <see cref="ButtonsIn"/> or <see cref="TextsIn"/>), or null.</summary>
    public static GameObject FindChild(UIPanel panel, string path)
    {
        try { return panel?.transform.Find(path)?.gameObject; }
        catch { return null; }
    }

    /// <summary>
    /// Sets the first text label on (or under) an element, then <see cref="Relayout"/>s it so elements that
    /// size themselves to their text (most buttons) fit the new text. Returns false if it has none.
    /// </summary>
    public static bool SetText(GameObject element, string text)
    {
        try
        {
            var t = element?.GetComponentInChildren<TMP_Text>(true);
            if (t == null) return false;
            t.text = text ?? "";
            Relayout(element);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Makes a copied element usable: the game often ships templates disabled or tuned for its own script
    /// (a greyed "Restore default" button, gamepad-only settings rows, layouts its script drives). Switches on
    /// every control (button, toggle, slider) and makes it interactable, lets the copy's canvas groups take clicks (alpha left
    /// as is: some are hidden on purpose), and switches on the copy's own top-level layout. Clone,
    /// CloneButton and window rows call it; call it yourself on copies you make another way.
    /// </summary>
    public static void MakeLive(GameObject element)
    {
        if (element == null) return;
        try
        {
            foreach (var sel in element.GetComponentsInChildren<Selectable>(true))
            {
                sel.enabled = true;        // settings toggles ship with the component itself switched off
                sel.interactable = true;
            }
            foreach (var cg in element.GetComponentsInChildren<CanvasGroup>(true)) { cg.interactable = true; cg.blocksRaycasts = true; }
            var layout = element.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = true;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Ui.MakeLive: {e.Message}"); }
    }

    // Elements that size themselves to their label (a ContentSizeFitter fitting width, like menu buttons)
    // don't always pick up a copy's new text: the game sets their width elsewhere (localization). Measure
    // the label and give the width to the element's LayoutElement, which the fitter respects.
    static void FitWidthToText(GameObject element)
    {
        var fitter = element.GetComponent<ContentSizeFitter>();
        if (fitter == null || fitter.horizontalFit != ContentSizeFitter.FitMode.PreferredSize) return;
        var text = element.GetComponentInChildren<TMP_Text>(true);
        if (text == null) return;
        float padding = 0f;
        var group = element.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (group != null) padding = group.padding.left + group.padding.right;
        float width = Mathf.Ceil(text.GetPreferredValues(text.text).x) + padding;
        var le = element.GetComponent<LayoutElement>() ?? element.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        var rt = element.transform.TryCast<RectTransform>();
        if (rt != null) rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
    }

    /// <summary>
    /// Recomputes an element's layout after you change it: refreshes its text, rebuilds its own layout
    /// (size fitters), then its parent's (layout groups), now and again next frame. Clone, CloneButton and
    /// SetText call it; call it yourself after changing a copy in other ways.
    /// </summary>
    public static void Relayout(GameObject element)
    {
        if (element == null) return;
        void Run()
        {
            try
            {
                if (element == null) return;
                foreach (var t in element.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate(false, false);
                var rt = element.transform.TryCast<RectTransform>();
                if (rt == null) return;
                FitWidthToText(element);
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                var parentRect = rt.parent?.TryCast<RectTransform>();
                if (parentRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }
            catch { }
        }
        Run();
        Scheduler.NextFrame(Run);
    }

    /// <summary>
    /// Copies any UI element (a row, a label, a whole group) so it matches the game's look. The copy goes
    /// right after <paramref name="template"/> (or at the end of <paramref name="parent"/>), active, with localization
    /// removed so the game doesn't overwrite its text. Inside a layout group (menus, lists) the group places
    /// the copy; elsewhere it takes the template's size and position (see the offset overload).
    /// Buttons inside keep the template's click actions; use <see cref="CloneButton"/> for a new action.
    /// Returns the copy, or null on failure. Remove it with <c>UnityEngine.Object.Destroy</c>.
    /// </summary>
    /// <remarks>Experimental. Layout groups place the copy automatically; elsewhere set its position.</remarks>
    public static GameObject Clone(GameObject template, Transform parent = null, string name = null) =>
        Clone(template, Vector2.zero, parent, name);

    /// <summary>
    /// <see cref="Clone(GameObject, Transform, string)"/>, then moves the copy by <paramref name="offset"/>
    /// (UI units, +y up) from the template's position. Use it where no layout group places the copy, or it
    /// sits exactly on top of the template. <see cref="Below"/> gives an offset one template-height down.
    /// </summary>
    public static GameObject Clone(GameObject template, Vector2 offset, Transform parent = null, string name = null)
    {
        if (template == null) return null;
        try
        {
            var go = UnityEngine.Object.Instantiate(template, parent ?? template.transform.parent);
            go.name = "Kit_" + (name ?? template.name);
            // Next to its template (not at the end of the parent, where other kinds of element may sit).
            if (parent == null) go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            foreach (var loc in go.GetComponentsInChildren<LocalizedStaticUILabel>(true))
                UnityEngine.Object.Destroy(loc);
            MakeLive(go);
            go.SetActive(true);

            var src = template.transform.TryCast<RectTransform>();   // IL2CPP wrappers: TryCast, not "is"
            var dst = go.transform.TryCast<RectTransform>();
            var parentRect = dst?.parent?.TryCast<RectTransform>();
            bool inLayout = parentRect != null && parentRect.GetComponent<LayoutGroup>() != null;
            if (inLayout)
            {
                // A layout group owns its children's position and size (with the copy's own size
                // fitter): leave them alone and let Relayout size the copy, then the group place it.
                Relayout(go);
            }
            else if (src != null && dst != null)
            {
                // Free-standing: match the template, then move by the offset (or it covers the template).
                dst.anchorMin = src.anchorMin;
                dst.anchorMax = src.anchorMax;
                dst.pivot = src.pivot;
                dst.sizeDelta = src.sizeDelta;
                dst.localScale = src.localScale;
                dst.anchoredPosition = src.anchoredPosition + offset;
            }
            return go;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Ui.Clone: {e.Message}");
            return null;
        }
    }

    /// <summary>An offset that places a copy directly below <paramref name="template"/> (its height, plus a gap).</summary>
    public static Vector2 Below(GameObject template, float gap = 2f)
    {
        try
        {
            var rt = template?.transform.TryCast<RectTransform>();
            return rt == null ? Vector2.zero : new Vector2(0f, -(rt.rect.height + gap));
        }
        catch { return Vector2.zero; }
    }

    /// <summary>Copies a text label (see <see cref="Clone(GameObject, Vector2, Transform, string)"/>) and sets its text. Returns the new label, or null.</summary>
    public static TMP_Text CloneText(TMP_Text template, string text, Vector2 offset = default, Transform parent = null)
    {
        var go = Clone(template?.gameObject, offset, parent, "Text");
        var t = go?.GetComponent<TMP_Text>();
        if (t != null) t.text = text ?? "";
        return t;
    }

    /// <summary>
    /// Adds a button to a screen by copying an existing one (see <see cref="Clone(GameObject, Transform, string)"/>): your label, and
    /// <paramref name="onClick"/> instead of the template's action. Returns the new button's GameObject, or null.
    /// </summary>
    /// <remarks>Experimental. Keyboard and controller navigation don't reach the copy yet.</remarks>
    public static GameObject CloneButton(Button template, string label, Action onClick, Transform parent = null)
    {
        var go = Clone(template?.gameObject, parent, label ?? "Button");
        if (go == null) return null;
        try
        {
            if (label != null) SetText(go, label);
            var button = go.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                string name = label ?? "button";
                button.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    if (onClick == null) return;
                    try { onClick(); }
                    catch (Exception e) { KitPlugin.L.LogError($"Ui.CloneButton '{name}': {e}"); }
                }));
            }
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Ui.CloneButton: {e.Message}"); }
        return go;
    }

    /// <summary>
    /// Gives an element the game's hover tooltip (the <c>HoverOverText</c> component the game uses), or
    /// changes its text. The element needs a raycast-target graphic (an Image or text) to receive the
    /// pointer, and must be on a screen that shows the cursor (menus, windows); the in-game HUD has no
    /// cursor, so tooltips there never show. Returns false on failure.
    /// </summary>
    public static bool Tooltip(GameObject element, string text)
    {
        if (element == null) return false;
        try
        {
            var hover = element.GetComponent<HoverOverText>() ?? element.AddComponent<HoverOverText>();
            hover.SetText(text ?? "");
            return true;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Ui.Tooltip: {e.Message}");
            return false;
        }
    }

    // The game holds button callbacks by pointer; keep the managed side alive until the next dialog.
    static readonly List<object> dialogDelegates = new();

    /// <summary>
    /// Shows one of the game's popup dialogs with your own buttons. Each button closes the dialog and
    /// runs its action (exceptions are caught and logged); the player can also close it with its X. With no buttons the game shows its default.
    /// Returns false if the dialog couldn't be shown.
    /// </summary>
    /// <remarks>
    /// Calling <c>Dialog</c> while one is open (for example from a button with closeOnClick false)
    /// replaces its text and buttons in place, for multi-step dialogs.
    /// </remarks>
    /// <example><code>
    /// Ui.Dialog("Order Fix", "Switch vendor mode to Cheapest?",
    ///     ("Yes", () => SetMode(SortMode.Cheapest)),
    ///     ("No",  null));
    /// </code></example>
    public static bool Dialog(string title, string message, params (string label, Action action)[] buttons) =>
        Dialog(title, message, true, buttons);

    /// <summary>
    /// The same as the other <c>Dialog</c>, with a choice: when
    /// <paramref name="closeOnClick"/> is false the dialog stays open after a button runs, and the player
    /// closes it with its X (useful for buttons like "Next" or toggles).
    /// </summary>
    public static bool Dialog(string title, string message, bool closeOnClick, params (string label, Action action)[] buttons)
    {
        try
        {
            dialogDelegates.Clear();
            Il2CppStringArray labels = null;
            Il2CppReferenceArray<Il2CppSystem.Action> actions = null;
            if (buttons != null && buttons.Length > 0)
            {
                labels = new Il2CppStringArray(buttons.Length);
                actions = new Il2CppReferenceArray<Il2CppSystem.Action>(buttons.Length);
                for (int i = 0; i < buttons.Length; i++)
                {
                    var (label, action) = buttons[i];
                    string name = label ?? "";
                    // The game's buttons only run their action; closing is the action's job. Close
                    // first, so an action can open another dialog.
                    Il2CppSystem.Action a = (Action)(() =>
                    {
                        if (closeOnClick)
                        {
                            try { PopupDialog._instance?.Hide(); }
                            catch (Exception e) { KitPlugin.L.LogWarning($"Ui.Dialog: could not close: {e.Message}"); }
                        }
                        if (action == null) return;
                        try { action(); }
                        catch (Exception e) { KitPlugin.L.LogError($"Ui.Dialog button '{name}': {e}"); }
                    });
                    dialogDelegates.Add(a);
                    labels[i] = name;
                    actions[i] = a;
                }
            }
            // Already open (e.g. a button opening the next step): replace the contents in place.
            // The game would otherwise queue the new dialog until this one closes, and its immediate
            // path adds buttons without clearing the old ones.
            bool replace = false;
            try
            {
                var open = PopupDialog._instance;
                if (open != null && PopupDialog.IsPopupVisible) { open.ClearButtons(); replace = true; }
            }
            catch { }
            PopupDialog.Display(title ?? "", message ?? "", labels, actions, replace);
            return true;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Ui.Dialog: {e.Message}");
            return false;
        }
    }

    // ---------- opening the game's screens ----------

    /// <summary>Opens the in-game menu on a tab (Inventory, Journal, Characters, Skills, Achievements, Recipes, FishDatabase, Venue).</summary>
    public static bool OpenMenu(InGameMenuTab tab)
    {
        try
        {
            var menu = InGameMenuUi.Instance;
            if (menu == null) return false;
            menu.OpenOnTab(tab);
            return true;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Ui.OpenMenu: {e.Message}"); return false; }
    }

    /// <summary>Opens the journal on a quest.</summary>
    public static bool OpenJournal(Nivalis.Quest quest)
    {
        try
        {
            var menu = InGameMenuUi.Instance;
            if (menu == null || quest == null) return false;
            menu.OpenJournalOnQuest(quest);
            return true;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Ui.OpenJournal: {e.Message}"); return false; }
    }

    /// <summary>Opens the city map.</summary>
    public static bool OpenMap()
    {
        try
        {
            var map = MapUI.Instance;
            if (map == null) return false;
            map.Open();
            return true;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Ui.OpenMap: {e.Message}"); return false; }
    }

    /// <summary>Opens a venue's management window on a tab (Overview, Inventory, Reviews, Staff).</summary>
    public static bool OpenVenue(VenueAreaGhost area, VenueWindow.Tab tab = VenueWindow.Tab.Overview)
    {
        try
        {
            var window = Find(nameof(VenueWindow))?.TryCast<VenueWindow>();
            var venue = area?.Venue;
            if (window == null || venue == null) return false;
            window.Open(venue, tab, false, null);
            return true;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Ui.OpenVenue: {e.Message}"); return false; }
    }

    // ---------- the radial (action) wheel ----------

    static readonly List<object> radialDelegates = new();

    /// <summary>True while the game's radial wheel is open.</summary>
    public static bool IsRadialOpen
    {
        get { try { return RadialMenuUI.instance?.IsOpen ?? false; } catch { return false; } }
    }

    /// <summary>
    /// Adds an action to the game's radial wheel while it's open (for example from a
    /// <see cref="GameEvents.PanelShown"/> handler for <c>RadialMenuUI</c>, after the game added its own).
    /// The icon can be null. Returns false if the wheel isn't open.
    /// </summary>
    public static bool AddRadialAction(string label, Action action, Sprite icon = null)
    {
        try
        {
            var wheel = RadialMenuUI.instance;
            if (wheel == null || !wheel.IsOpen) return false;
            wheel.AddAction(icon, label ?? "", icon != null, Wrap($"Ui radial '{label}'", action, radialDelegates), true);
            return true;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Ui.AddRadialAction: {e.Message}"); return false; }
    }

    /// <summary>
    /// Opens the game's radial wheel with your actions (plus the game's close slot). Icons are blank.
    /// Returns false if the wheel couldn't open.
    /// </summary>
    /// <remarks>Experimental: the game opens the wheel for greenhouse modules; this opens it on its own.</remarks>
    public static bool RadialMenu(params (string label, Action action)[] actions)
    {
        try
        {
            var wheel = RadialMenuUI.instance;
            if (wheel == null) return false;
            radialDelegates.Clear();
            wheel.Clear();
            foreach (var (label, action) in actions ?? Array.Empty<(string, Action)>())
                wheel.AddAction(null, label ?? "", false, Wrap($"Ui radial '{label}'", action, radialDelegates), true);
            wheel.AddCloseAction();
            wheel.Open();
            return true;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Ui.RadialMenu: {e.Message}"); return false; }
    }

    static Il2CppSystem.Action Wrap(string what, Action action, List<object> keep)
    {
        Il2CppSystem.Action a = (Action)(() =>
        {
            if (action == null) return;
            try { action(); }
            catch (Exception e) { KitPlugin.L.LogError($"{what}: {e}"); }
        });
        keep.Add(a);
        return a;
    }
}
