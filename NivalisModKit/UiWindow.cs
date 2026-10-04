using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Nivalis;
using Nivalis.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NivalisModKit;

/// <summary>Which game window a <see cref="KitWindow"/> is built from.</summary>
public enum WindowStyle
{
    /// <summary>
    /// A small popup (the game's confirm popup, 600 wide): title, a list of rows, a button row. For short
    /// tools and prompts. Closes with Escape or a footer button.
    /// </summary>
    Popup,

    /// <summary>
    /// A large panel (the Settings screen's frame): title, close X, a scrolling list of rows and a button row.
    /// For full mod windows such as settings.
    /// </summary>
    Panel,
}

public static partial class Ui
{
    /// <summary>
    /// Creates a window of your own, built from the game's parts so it matches the game's look, with rows
    /// you add (text, buttons, toggles, sliders). It behaves like a game screen: shows the cursor and closes
    /// with Escape. Call <see cref="KitWindow.Show"/> when it's ready. Returns null if the game's parts
    /// aren't loaded yet (call it in gameplay, not on the start menu).
    /// </summary>
    /// <example><code>
    /// var w = Ui.CreateWindow("My Mod", WindowStyle.Panel);
    /// w.AddText("Choose a vendor mode.");
    /// w.AddToggle("Verbose logging", verbose, v => verbose = v);
    /// w.AddSlider("Distance weight", 0, 1, weight, v => weight = v, "0.00");
    /// w.AddFooterButton("Close", w.Hide);
    /// w.Show();
    /// </code></example>
    /// <remarks>Experimental. Keyboard and controller navigation don't reach the rows yet.</remarks>
    public static KitWindow CreateWindow(string title, WindowStyle style = WindowStyle.Panel)
    {
        try { return KitWindow.Create(title, style); }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Ui.CreateWindow({style}): {e.Message}");
            return null;
        }
    }
}

/// <summary>A window made by <see cref="Ui.CreateWindow"/>. Main thread only.</summary>
public sealed class KitWindow
{
    /// <summary>The window's root GameObject.</summary>
    public GameObject Root { get; private set; }

    /// <summary>Where rows go (a vertical list; in a Panel, inside a scroll view).</summary>
    public Transform Content { get; private set; }

    /// <summary>The window's screen (a game <c>UIPanel</c>): shows the cursor, closes with Escape.</summary>
    public UIPanel Panel { get; private set; }

    /// <summary>The style it was built from.</summary>
    public WindowStyle Style { get; private set; }

    /// <summary>True while shown.</summary>
    public bool IsOpen
    {
        get { try { return Panel != null && Panel.IsVisible; } catch { return false; } }
    }

    /// <summary>Raised after the window is hidden (its X, Escape, a footer Close, or <see cref="Hide"/>).</summary>
    public event Action Closed;

    CanvasGroup group;
    bool fixContentHeight;   // Popup: the game's popup layout doesn't size Content for rows; set it from its rows
    TMP_Text titleText, textTemplate;
    GameObject buttonTemplate;
    Transform footer;
    readonly List<object> keep = new();   // delegates the game holds by pointer

    // Game scripts removed from copies: they would run their own logic on the copy.
    static readonly string[] WindowScripts =
        { "SettingsPanel", "GameplaySettingsUI", "GraphicsSettingsUI", "AudioSettingsUI", "ControlsSettingsUI",
          "OnControllerEventListener", "WindowToggleController", "GenericPopup", "GenericPopupThreeButtons",
          "UI_TitleFrameAnimation" };
    static readonly string[] RowScripts = { "BetterContentSizeFitter", "GamepadAutoScroll", "ManualUINavigation" };

    internal static KitWindow Create(string title, WindowStyle style)
    {
        var w = new KitWindow { Style = style };
        if (style == WindowStyle.Popup) w.BuildPopup(); else w.BuildPanel();
        w.SetTitle(title);

        foreach (var loc in w.Root.GetComponentsInChildren<LocalizedStaticUILabel>(true))
            UnityEngine.Object.DestroyImmediate(loc);

        // The window's screen: a plain game UIPanel (cursor, Escape to close).
        var panel = w.Root.GetComponent<UIPanel>() ?? w.Root.AddComponent<UIPanel>();
        // A UIPanel fades the CanvasGroup its prefab points it at; a new one points nowhere, and the game's
        // popups hide by fading to alpha 0. Point it at the copy's group so Show fades the window in.
        var group = w.Root.GetComponent<CanvasGroup>() ?? w.Root.AddComponent<CanvasGroup>();
        panel._canvasGroup = group;
        w.group = group;
        panel.requiresMouse = true;
        panel.closeWithCancel = true;
        panel.pauseTimeWhenOpen = false;
        w.Panel = panel;
        Il2CppSystem.Action onHide = (Action)(() =>
        {
            w.Closed?.Invoke();
            // Switch the hidden window off (after the fade), so it costs nothing while closed.
            Scheduler.AfterSeconds(0.5f, () => { try { if (w.Root != null && !w.IsOpen) w.Root.SetActive(false); } catch { } });
        });
        w.keep.Add(onHide);
        panel.add_OnHideEvent(onHide);
        return w;
    }

    // Copies a game screen under an inactive holder (so none of the copy's scripts wake up), strips the
    // game's scripts, and returns the copy, still inactive under the holder.
    GameObject CopyScreen(GameObject template, string name)
    {
        var holder = new GameObject("Kit_WindowHolder");
        holder.SetActive(false);
        holder.transform.SetParent(WindowLayer(template), false);
        var go = UnityEngine.Object.Instantiate(template, holder.transform);
        go.name = name;
        StripScripts(go, WindowScripts);
        return go;
    }

    // Where kit windows live: the canvas the game's own popup dialog is in, which is visible in gameplay.
    // A template's own parent can be inside a hidden screen (the Settings screen's nested popups), and a
    // copy there would inherit that screen's invisibility.
    static Transform WindowLayer(GameObject template) =>
        PopupDialog._instance?.transform.parent ?? template.transform.parent;

    void Finish(GameObject go, GameObject template)
    {
        var holder = go.transform.parent.gameObject;
        go.transform.SetParent(WindowLayer(template), false);
        UnityEngine.Object.Destroy(holder);
        Root = go;
        go.SetActive(true);
        var panel = go.GetComponent<UIPanel>();   // stripped above; added by Create
        if (panel != null) panel.SetInvisibleImmediate();
    }

    // ---------- Popup: GenericPopup ----------

    void BuildPopup()
    {
        var template = Ui.Find("GenericPopup")?.gameObject ?? throw new Exception("the game's popup isn't loaded (call in gameplay)");
        var go = CopyScreen(template, "Kit_PopupWindow");
        var body = go.transform.Find("GenericPopup") ?? go.transform;

        titleText = body.Find("P_Element_TitleFrame/Title")?.GetComponent<TMP_Text>();
        var content = body.Find("Content") ?? throw new Exception("popup has no Content");
        textTemplate = content.Find("Text")?.GetComponent<TMP_Text>();
        if (textTemplate != null) textTemplate.gameObject.SetActive(false);
        // Content grows with its rows instead of a fixed height.
        var cle = content.GetComponent<LayoutElement>();
        if (cle != null) { cle.preferredHeight = -1f; cle.minHeight = 40f; cle.flexibleHeight = 0f; }
        var clayout = content.GetComponent<VerticalLayoutGroup>();
        // The game ships these layouts switched off (its popup script sizes its one text itself).
        if (clayout != null) { clayout.enabled = true; clayout.spacing = Math.Max(clayout.spacing, 8f); clayout.childControlHeight = true; clayout.childForceExpandHeight = false; clayout.childControlWidth = true; clayout.childForceExpandWidth = true; }
        var ble = body.GetComponent<LayoutElement>();
        if (ble != null) { ble.preferredHeight = -1f; }
        var blayout = body.GetComponent<VerticalLayoutGroup>();
        if (blayout != null) { blayout.enabled = true; blayout.childControlHeight = true; blayout.childForceExpandHeight = false; blayout.childControlWidth = true; blayout.childForceExpandWidth = true; }
        fixContentHeight = true;
        // Place it ourselves: the root (a popup nested in the Settings screen) keeps that screen's anchoring,
        // which put the copy in a corner. Root fills the screen; the body is centred and sizes to its rows.
        var outer = go.GetComponent<VerticalLayoutGroup>();
        if (outer != null && body != go.transform) outer.enabled = false;
        var rootRt = go.transform.TryCast<RectTransform>();
        if (rootRt != null)
        {
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
        }
        var bodyRt = body.TryCast<RectTransform>();
        if (bodyRt != null && body != go.transform)
        {
            bodyRt.anchorMin = bodyRt.anchorMax = new Vector2(0.5f, 0.5f);
            bodyRt.pivot = new Vector2(0.5f, 0.5f);
            bodyRt.anchoredPosition = Vector2.zero;
            bodyRt.sizeDelta = new Vector2(Math.Max(bodyRt.sizeDelta.x, 600f), bodyRt.sizeDelta.y);
        }
        // The body may already have a size fitter set not to control height: take it either way.
        var bfit = body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>();
        bfit.enabled = true;
        bfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        Content = content;

        var buttons = body.Find("Buttons");
        if (buttons != null)
        {
            footer = buttons;
            buttons.gameObject.SetActive(true);
            var accept = buttons.Find("AcceptButton");
            buttonTemplate = accept?.gameObject;
            for (int i = buttons.childCount - 1; i >= 0; i--)
            {
                var child = buttons.GetChild(i).gameObject;
                if (child != buttonTemplate) UnityEngine.Object.DestroyImmediate(child);
            }
            if (buttonTemplate != null) buttonTemplate.SetActive(false);
        }
        Finish(go, template);
    }

    // ---------- Panel: the Settings screen's frame ----------

    void BuildPanel()
    {
        var template = Ui.Find("SettingsPanel")?.gameObject ?? throw new Exception("the Settings screen isn't loaded (call in gameplay)");
        var go = CopyScreen(template, "Kit_PanelWindow");
        var frame = go.transform.Find("FrameWrapper") ?? throw new Exception("Settings copy has no FrameWrapper");

        // Keep the Gameplay page (title, scroll view, footer); drop the other pages, the tab bar and popups.
        foreach (var name in new[] { "GraphicsSettings", "AudioSettings", "ControlsSettings",
                     "ConfirmTextureQualityChangePopup", "ApplyingMessage", "P_TogglesGroup Variant" })
        {
            var t = frame.Find(name);
            if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
        }
        for (int i = go.transform.childCount - 1; i >= 0; i--)
        {
            var child = go.transform.GetChild(i);
            if (child != frame) UnityEngine.Object.DestroyImmediate(child.gameObject);   // e.g. ThreeButtonPopup
        }

        titleText = frame.Find("P_Element_TitleFrame/Title")?.GetComponent<TMP_Text>();
        var page = frame.Find("GameplaySettings") ?? throw new Exception("Settings copy has no GameplaySettings page");
        page.gameObject.SetActive(true);
        var pageGroup = page.GetComponent<CanvasGroup>();
        if (pageGroup != null) { pageGroup.alpha = 1f; pageGroup.interactable = true; pageGroup.blocksRaycasts = true; }

        var content = page.Find("MainSettings/Scrollview/Viewport/Content") ?? throw new Exception("Settings copy has no scroll content");
        // A text template: a settings label, kept hidden.
        var label = content.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
        if (label != null)
        {
            var t = UnityEngine.Object.Instantiate(label.gameObject, page);
            t.name = "Kit_TextTemplate";
            t.SetActive(false);
            textTemplate = t.GetComponent<TMP_Text>();
            textTemplate.enableWordWrapping = true;
            textTemplate.alignment = TextAlignmentOptions.TopLeft;
        }
        for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
        var clayout = content.GetComponent<VerticalLayoutGroup>();
        if (clayout != null) { clayout.spacing = Math.Max(clayout.spacing, 8f); clayout.childControlWidth = true; clayout.childForceExpandWidth = true; clayout.childControlHeight = true; clayout.childForceExpandHeight = false; }
        Content = content;

        var buttons = page.Find("ButtonsWrapper");
        if (buttons != null)
        {
            footer = buttons;
            buttonTemplate = buttons.GetComponentInChildren<Button>(true)?.gameObject;
            for (int i = buttons.childCount - 1; i >= 0; i--)
            {
                var child = buttons.GetChild(i).gameObject;
                if (buttonTemplate == null || child != buttonTemplate) UnityEngine.Object.DestroyImmediate(child);
            }
            if (buttonTemplate != null) buttonTemplate.SetActive(false);
        }

        // The close X.
        var close = frame.Find("P_Element_CloseBtn");
        var closeButton = close?.GetComponentInChildren<Button>(true);
        if (closeButton != null)
        {
            closeButton.onClick = new Button.ButtonClickedEvent();
            UnityAction c = (Action)Hide;
            keep.Add(c);
            closeButton.onClick.AddListener(c);
        }
        Finish(go, template);
    }

    static void StripScripts(GameObject go, string[] names)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            string n;
            try { n = mb.GetIl2CppType().Name; } catch { continue; }
            if (Array.IndexOf(names, n) >= 0) UnityEngine.Object.DestroyImmediate(mb);
        }
    }

    // ---------- showing ----------

    /// <summary>Shows the window.</summary>
    public void Show()
    {
        try
        {
            if (!Root.activeSelf) Root.SetActive(true);
            Root.transform.SetAsLastSibling();   // above the screens next to it
            Panel.Show();
            if (group != null) { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; }   // in case no fade ran
            Ui.Relayout(Content.gameObject);
            RebuildFromRoot();
            Scheduler.NextFrame(RebuildFromRoot);
            open.Add(this);
            if (open.Count == 1) KitLoop.Tick += Tick;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"KitWindow.Show: {e.Message}"); }
    }

    void RebuildFromRoot()
    {
        try
        {
            var rt = Root?.transform.TryCast<RectTransform>();
            if (rt == null) { if (!loggedLayout) { loggedLayout = true; KitPlugin.L.LogInfo($"KitWindow {Style}: root has no RectTransform"); } return; }
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            if (!fixContentHeight && !loggedLayout)
            {
                loggedLayout = true;
                var c = Content?.TryCast<RectTransform>();
                KitPlugin.L.LogInfo($"KitWindow {Style}: root {rt.rect.width:0}x{rt.rect.height:0}, content {(c == null ? "?" : $"{c.rect.width:0}x{c.rect.height:0}")}");
            }
            if (fixContentHeight)
            {
                var crt = Content.TryCast<RectTransform>();
                var clayout = Content.GetComponent<VerticalLayoutGroup>();
                if (crt != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
                    float h = LayoutUtility.GetPreferredHeight(crt);
                    var cle = Content.GetComponent<LayoutElement>() ?? Content.gameObject.AddComponent<LayoutElement>();
                    cle.preferredHeight = Math.Max(h, 40f);
                    cle.minHeight = Math.Max(h, 40f);
                    // Rebuild the body itself: a rebuild from the root stops at the root (no layout there).
                    var bodyRt = Content.parent?.TryCast<RectTransform>();
                    if (bodyRt != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(bodyRt);
                        // The game's popup script placed the title, text and buttons itself; without it nothing
                        // stacks them. If the layout didn't give Content its height, stack the body ourselves.
                        if (crt.rect.height < h - 1f) StackPopupBody(bodyRt, crt, Math.Max(h, 40f));
                    }
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                    if (!loggedLayout)
                    {
                        loggedLayout = true;
                        KitPlugin.L.LogInfo($"KitWindow {Style}: content preferred height {h:0}, layout " +
                            (clayout == null ? "none" : $"enabled={clayout.enabled} controlW={clayout.childControlWidth} controlH={clayout.childControlHeight} " +
                             $"padding={clayout.padding.left},{clayout.padding.right},{clayout.padding.top},{clayout.padding.bottom}") +
                            $", content now {crt.rect.width:0}x{crt.rect.height:0}");
                    }
                }
            }
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"KitWindow layout: {e.Message}"); }
    }
    bool loggedLayout;
    static bool loggedRow, loggedToggle, loggedBody;

    // Lays the popup body's children out top to bottom (title, content, buttons) and sizes the body to fit.
    static void StackPopupBody(RectTransform body, RectTransform content, float contentHeight)
    {
        const float pad = 16f, gap = 8f;
        if (!loggedBody)
        {
            loggedBody = true;
            var comps = string.Join(", ", body.GetComponents<Component>().Select(c => { try { return c.GetIl2CppType().Name; } catch { return "?"; } }));
            KitPlugin.L.LogInfo($"KitWindow Popup body '{body.name}' components: {comps}; stacking children manually");
        }
        var vlg = body.GetComponent<VerticalLayoutGroup>();
        if (vlg != null) vlg.enabled = false;
        var fit = body.GetComponent<ContentSizeFitter>();
        if (fit != null) fit.enabled = false;

        float width = Math.Max(body.rect.width, 600f);
        float y = pad;
        for (int i = 0; i < body.childCount; i++)
        {
            var child = body.GetChild(i).TryCast<RectTransform>();
            if (child == null || !child.gameObject.activeSelf) continue;
            if (child.name == "Background")
            {
                // Fills the whole body.
                child.anchorMin = Vector2.zero; child.anchorMax = Vector2.one;
                child.offsetMin = Vector2.zero; child.offsetMax = Vector2.zero;
                continue;
            }
            float ch = child == content ? contentHeight : Math.Max(child.rect.height, LayoutUtility.GetPreferredHeight(child));
            if (ch <= 0f) continue;
            bool narrow = child != content && child.rect.width > 0f && child.rect.width < width - 1f;
            float cw = narrow ? child.rect.width : width - 2f * pad;
            if (child.name.Contains("TitleFrame")) { cw = width; }
            child.anchorMin = child.anchorMax = new Vector2(0.5f, 1f);
            child.pivot = new Vector2(0.5f, 1f);
            child.sizeDelta = new Vector2(cw, ch);
            child.anchoredPosition = new Vector2(0f, child.name.Contains("TitleFrame") ? 0f : -y);
            y = (child.name.Contains("TitleFrame") ? ch : y + ch) + gap;
        }
        body.sizeDelta = new Vector2(width, y - gap + pad);
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    // ---------- per frame, only while a kit window is open ----------

    static readonly List<KitWindow> open = new();
    static IntPtr raisedTooltip;

    static void Tick()
    {
        open.RemoveAll(w => w.Root == null || !w.IsOpen);
        if (open.Count == 0) { KitLoop.Tick -= Tick; return; }
        RaiseTooltip();
        ScrollWithWheel();
    }

    // The game draws tooltips on a layer below kit windows. Give a kit element's tooltip its own canvas on top.
    static void RaiseTooltip()
    {
        try
        {
            if (!Singleton<Nivalis.CraftingSystem.HoverTooltipUiManager>.InstanceExist(out var mgr) || mgr == null) return;
            var tip = mgr._currentHoverOver;
            if (tip == null) { raisedTooltip = IntPtr.Zero; return; }
            if (tip.Pointer == raisedTooltip) return;
            var source = mgr._source;
            if (source == null || !open.Any(w => source.transform.IsChildOf(w.Root.transform))) return;
            var canvas = tip.GetComponent<Canvas>() ?? tip.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;
            raisedTooltip = tip.Pointer;
        }
        catch { }
    }

    // The game scrolls a screen's lists only for screens that hand it their scroll views; scroll ours here.
    static void ScrollWithWheel()
    {
        try
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            float dy = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(dy) < 0.01f) return;
            Vector2 pos = mouse.position.ReadValue();
            foreach (var w in open)
                foreach (var sr in w.Root.GetComponentsInChildren<ScrollRect>(false))
                {
                    var rt = sr.transform.TryCast<RectTransform>();
                    if (rt == null || !RectTransformUtility.RectangleContainsScreenPoint(rt, pos, null)) continue;
                    var content = sr.content;
                    var view = sr.viewport ?? rt;
                    float overflow = (content != null ? content.rect.height : 0f) - view.rect.height;
                    if (overflow <= 1f) return;
                    // one wheel notch (120) scrolls about 60 UI units
                    float step = dy / 120f * 60f / overflow;
                    sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition + step);
                    return;
                }
        }
        catch { }
    }

    /// <summary>Hides the window (keep it to show again).</summary>
    public void Hide()
    {
        try { Panel.Hide(); } catch (Exception e) { KitPlugin.L.LogWarning($"KitWindow.Hide: {e.Message}"); }
        if (group != null) { group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false; }
    }

    /// <summary>Hides and destroys the window.</summary>
    public void Close()
    {
        Hide();
        try { UnityEngine.Object.Destroy(Root, 0.5f); } catch { }
    }

    /// <summary>Sets the title.</summary>
    public void SetTitle(string title)
    {
        if (titleText != null) titleText.text = title ?? "";
    }

    // ---------- rows ----------

    /// <summary>Adds a paragraph of text. Returns the label (change its text later as you like).</summary>
    public TMP_Text AddText(string text)
    {
        if (textTemplate == null) return null;
        var go = UnityEngine.Object.Instantiate(textTemplate.gameObject, Content);
        go.name = "Kit_Text";
        Upright(go);
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        go.SetActive(true);
        var t = go.GetComponent<TMP_Text>();
        t.text = text ?? "";
        Ui.Relayout(go);
        return t;
    }

    /// <summary>Adds a button in the list. Returns its GameObject.</summary>
    public GameObject AddButton(string label, Action onClick) => AddButtonTo(Content, label, onClick, ListButtonTemplate() ?? buttonTemplate);

    // Buttons copy the game's popup-dialog option button: a plain button that works anywhere. (The Settings
    // footer button carries oversized hover gradients and never received clicks, in the list or the footer.)
    static GameObject ListButtonTemplate()
    {
        try { return PopupDialog._instance?.buttonTemplate?.gameObject; } catch { return null; }
    }

    /// <summary>Adds a button to the bottom row.</summary>
    public GameObject AddFooterButton(string label, Action onClick) => AddButtonTo(footer ?? Content, label, onClick, ListButtonTemplate() ?? buttonTemplate);

    GameObject AddButtonTo(Transform parent, string label, Action onClick, GameObject template)
    {
        if (template == null) return null;
        var go = UnityEngine.Object.Instantiate(template, parent);
        go.name = "Kit_Button_" + label;
        if (parent == Content)
        {
            Upright(go);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleHeight = 0f;
            le.preferredHeight = Height(template, 40f);
        }
        go.SetActive(true);
        Ui.MakeLive(go);
        // Hover decorations reaching outside the button must not catch clicks meant for others.
        foreach (var img in go.GetComponentsInChildren<Image>(true))
            if (img.gameObject != go) img.raycastTarget = false;
        var b = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true);
        if (b != null)
        {
            b.interactable = true;   // templates can be disabled (Settings' "Restore default" while at defaults)
            b.onClick = new Button.ButtonClickedEvent();
            UnityAction a = (Action)(() =>
            {
                if (onClick == null) return;
                try { onClick(); }
                catch (Exception e) { KitPlugin.L.LogError($"KitWindow button '{label}': {e}"); }
            });
            keep.Add(a);
            b.onClick.AddListener(a);
        }
        Ui.SetText(go, label);
        return go;
    }

    /// <summary>
    /// Adds a labelled on/off switch (a copy of the game's settings toggle, without its settings saving).
    /// <paramref name="onChange"/> runs when the player flips it. Returns the Toggle, or null if the game's
    /// settings rows aren't loaded.
    /// </summary>
    public Toggle AddToggle(string label, bool value, Action<bool> onChange)
    {
        var template = FindSettingRow<ToggleSettingUI>();
        if (template == null) return null;
        var go = CopyRow(template.gameObject, label, out var row);
        var toggleRef = row?.TryCast<ToggleSettingUI>()?.toggle;
        if (row != null) UnityEngine.Object.DestroyImmediate(row);
        var toggle = toggleRef ?? go.GetComponentInChildren<Toggle>(true);
        if (toggle == null) return null;
        toggle.interactable = true;   // templates can be disabled (gamepad-only settings rows without a gamepad)
        if (!loggedToggle)
        {
            loggedToggle = true;
            KitPlugin.L.LogInfo($"KitWindow toggle '{label}': type {toggle.GetIl2CppType().Name}, enabled={toggle.enabled}, " +
                                $"group={(toggle.group != null ? toggle.group.name : "none")}, isOn={toggle.isOn}, on row={toggle.gameObject == go}");
        }
        toggle.group = null;   // copies must not join the game's toggle groups (a group can veto switching)
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.SetIsOnWithoutNotify(value);
        UnityAction<bool> a = (Action<bool>)(v =>
        {
            if (onChange == null) return;
            try { onChange(v); }
            catch (Exception e) { KitPlugin.L.LogError($"KitWindow toggle '{label}': {e}"); }
        });
        keep.Add(a);
        toggle.onValueChanged.AddListener(a);
        go.SetActive(true);
        Ui.Relayout(go);
        return toggle;
    }

    /// <summary>
    /// Adds a labelled slider (a copy of the game's settings slider, without its settings saving).
    /// <paramref name="format"/> formats the value shown next to it (e.g. "0", "0.00", "0%");
    /// <paramref name="wholeNumbers"/> snaps to integers. Returns the Slider, or null if the game's settings
    /// rows aren't loaded.
    /// </summary>
    public Slider AddSlider(string label, float min, float max, float value, Action<float> onChange,
        string format = "0.##", bool wholeNumbers = false)
    {
        var template = FindSettingRow<SliderSettingUI>();
        if (template == null) return null;
        var go = CopyRow(template.gameObject, label, out var row);
        var settings = row?.TryCast<SliderSettingUI>();
        var sliderRef = settings?.slider;
        var valueText = settings?.valueText;
        if (row != null) UnityEngine.Object.DestroyImmediate(row);
        var slider = sliderRef ?? go.GetComponentInChildren<Slider>(true);
        if (slider == null) return null;
        slider.interactable = true;
        slider.onValueChanged = new Slider.SliderEvent();
        slider.wholeNumbers = wholeNumbers;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        if (valueText != null) valueText.text = value.ToString(format);
        UnityAction<float> a = (Action<float>)(v =>
        {
            if (valueText != null) valueText.text = v.ToString(format);
            if (onChange == null) return;
            try { onChange(v); }
            catch (Exception e) { KitPlugin.L.LogError($"KitWindow slider '{label}': {e}"); }
        });
        keep.Add(a);
        slider.onValueChanged.AddListener(a);
        go.SetActive(true);
        Ui.Relayout(go);
        return slider;
    }

    // ---------- helpers ----------

    static void Upright(GameObject go)
    {
        go.transform.localRotation = Quaternion.identity;
        var sc = go.transform.localScale;
        go.transform.localScale = new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z));
    }

    static float Height(GameObject template, float fallback)
    {
        var rt = template.transform.TryCast<RectTransform>();
        float h = rt == null ? 0f : rt.rect.height;
        return h > 1f ? h : fallback;
    }

    // A loaded settings row of the given kind (the Settings screen is in the scene, hidden).
    static T FindSettingRow<T>() where T : SettingUIElementBase
    {
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
        {
            var row = o.TryCast<T>();
            if (row != null && row.gameObject.scene.IsValid()) return row;
        }
        return null;
    }

    // Copies a settings row into Content (inactive), strips its settings scripts, sets the label.
    GameObject CopyRow(GameObject template, string label, out SettingUIElementBase row)
    {
        var holder = new GameObject("Kit_RowHolder");
        holder.SetActive(false);
        holder.transform.SetParent(Content, false);
        var go = UnityEngine.Object.Instantiate(template, holder.transform);
        go.name = "Kit_Row_" + label;
        foreach (var loc in go.GetComponentsInChildren<LocalizedStaticUILabel>(true))
            UnityEngine.Object.DestroyImmediate(loc);
        row = go.GetComponent<SettingUIElementBase>();
        StripScripts(go, RowScripts);
        // Rows can be greyed out (gamepad-only settings): restore their look and make them usable.
        foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true)) cg.alpha = 1f;
        Ui.MakeLive(go);
        var fitter = go.GetComponent<ContentSizeFitter>();
        if (fitter != null) UnityEngine.Object.DestroyImmediate(fitter);
        Upright(go);
        // Rows stretch across the window at their own height.
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.flexibleHeight = 0f;
        le.preferredHeight = Height(template, 45f);
        // The row's label: its first text that isn't inside the control.
        var texts = go.GetComponentsInChildren<TMP_Text>(true);
        var labelText = texts.FirstOrDefault(t => t.GetComponentInParent<Selectable>() == null || t.transform.parent == go.transform)
                        ?? texts.FirstOrDefault();
        if (labelText != null) labelText.text = label ?? "";

        // In the game the row's own size fitter (removed above) drives its horizontal layout; without it the
        // children keep the hidden template's zero size. Let the row's layout size them: the label takes the
        // spare width, other children their template width.
        var hlg = go.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null)
        {
            if (!loggedRow)
            {
                loggedRow = true;
                KitPlugin.L.LogInfo($"KitWindow row '{label}': layout enabled={hlg.enabled} controlW={hlg.childControlWidth} " +
                                    $"controlH={hlg.childControlHeight} children={go.transform.childCount}");
            }
            hlg.enabled = true;   // the game may leave it to its own size fitter (removed above)
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i);
                var cle = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
                if (labelText != null && child == labelText.transform)
                {
                    cle.flexibleWidth = 1f;
                    cle.minWidth = 120f;
                }
                else
                {
                    var src = template.transform.childCount > i ? template.transform.GetChild(i).TryCast<RectTransform>() : null;
                    float w = src != null && src.rect.width > 1f ? src.rect.width : Math.Max(cle.preferredWidth, 200f);
                    cle.preferredWidth = Math.Min(w, 400f);
                    cle.flexibleWidth = 0f;
                }
            }
        }
        go.transform.SetParent(Content, false);
        UnityEngine.Object.Destroy(holder);
        return go;
    }
}
