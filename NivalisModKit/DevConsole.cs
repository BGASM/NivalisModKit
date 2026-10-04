using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisModKit;

// In-game terminal for DevCommands: press ` ([DevConsole] Key) and a black panel drops from the top of the screen.
// Type "demo style=Panel", Enter. Up/Down recall earlier commands, Tab completes names, "clear" empties the output.
// While the box has focus the game is in menu mode (cursor, no movement); click outside it to play with the console
// still showing, ` to type again, ` again (or Escape) to close. Built from code on its own canvas (no game screen needed), so it also works on the title
// screen. Runs in the game itself (no bridge, no network); off unless [DevConsole] Enabled.
internal static class DevConsole
{
    const float HeightFraction = 0.42f;   // of the screen
    const float SlideSeconds = 0.12f;
    const int MaxLines = 400;
    const string Prompt = "> ";

    static readonly Color Background = new(0.03f, 0.03f, 0.04f, 0.9f);
    static readonly Color InputBack = new(1f, 1f, 1f, 0.06f);
    const string EchoColor = "#7FD1FF", ReplyColor = "#D0D0D0", ErrorColor = "#FF6B6B", NoteColor = "#8A8A8A";

    static GameObject root;
    static RectTransform panel;
    static TMP_Text output;
    static TMP_InputField input;
    static readonly List<string> lines = new();
    static readonly List<string> history = new();
    static int historyIndex = -1;
    static int scroll;          // lines scrolled back from the bottom
    static float shown;         // 0 = hidden, 1 = fully down
    static bool open;
    static IDisposable menuMode;   // held while typing (Ui.RequestMenuMode): cursor on, movement off
    static UnityEngine.InputSystem.Key key;
    static bool ticking;
    static readonly UnityEngine.Events.UnityAction<string> onSubmit = (Action<string>)Submit;          // kept alive
    static readonly UnityEngine.Events.UnityAction<string> onChanged = (Action<string>)TextChanged;

    internal static void Install()
    {
        KitPlugin.ConsoleEnabled.SettingChanged += (_, _) => Apply();
        KitPlugin.ConsoleKey.SettingChanged += (_, _) => Apply();
        Apply();
    }

    static void Apply()
    {
        if (!Enum.TryParse(KitPlugin.ConsoleKey.Value, true, out key))
        {
            KitPlugin.L.LogWarning($"DevConsole: unknown key '{KitPlugin.ConsoleKey.Value}', using Backquote (`)");
            key = UnityEngine.InputSystem.Key.Backquote;
        }
        bool want = KitPlugin.ConsoleEnabled.Value;
        if (want && !ticking) { KitLoop.Tick += Tick; ticking = true; KitPlugin.L.LogInfo($"DevConsole: on, press {key}"); }
        else if (!want && ticking) { KitLoop.Tick -= Tick; ticking = false; SetOpen(false); }
    }

    // ---------- per frame (one key check while closed) ----------

    static void Tick()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        // ` opens; with the console open it returns to the box, or closes it if you're already typing.
        if (kb[key].wasPressedThisFrame)
        {
            if (!open) SetOpen(true);
            else if (Typing()) SetOpen(false);
            else Focus();
        }
        else if (open && kb.escapeKey.wasPressedThisFrame) SetOpen(false);
        if (root == null || (!open && shown <= 0f)) return;

        // Slide.
        float target = open ? 1f : 0f;
        shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime / SlideSeconds);
        panel.anchoredPosition = new Vector2(0f, (1f - shown) * panel.rect.height);
        if (!open && shown <= 0f) { root.SetActive(false); return; }
        SetLocked(open && Typing());
        if (!open) return;

        if (Typing())
        {
            if (kb.upArrowKey.wasPressedThisFrame) Recall(+1);
            else if (kb.downArrowKey.wasPressedThisFrame) Recall(-1);
        }

        // Scroll back: mouse wheel, Page Up / Page Down.
        int step = 0;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null) { float dy = mouse.scroll.ReadValue().y; if (dy > 0.01f) step = 3; else if (dy < -0.01f) step = -3; }
        if (kb.pageUpKey.wasPressedThisFrame) step = 10;
        if (kb.pageDownKey.wasPressedThisFrame) step = -10;
        if (step != 0) { scroll = Mathf.Clamp(scroll + step, 0, Math.Max(0, lines.Count - 1)); Render(); }
    }

    static void SetOpen(bool value)
    {
        if (value == open) return;
        if (value && !Build()) return;
        open = value;
        if (open)
        {
            root.SetActive(true);
            Focus();
        }
        else
        {
            if (input != null) { input.DeactivateInputField(); input.text = ""; }
            SetLocked(false);
        }
    }

    // The box is taking keys (focused, or about to be after Focus()).
    static bool Typing() => input != null && (input.isFocused || focusPending);
    static bool focusPending;

    // Menu mode follows typing, so clicking outside the box gives play back with the console still showing.
    static void SetLocked(bool value)
    {
        if (value == (menuMode != null)) return;
        if (value) menuMode = Ui.RequestMenuMode(ModKit.Guid + ".console");
        else { menuMode.Dispose(); menuMode = null; }
    }

    static void Focus()
    {
        // Next frame, so the key that opened the console isn't typed into the box.
        focusPending = true;
        Scheduler.NextFrame(() =>
        {
            focusPending = false;
            try { if (open && input != null) { input.ActivateInputField(); input.MoveTextEnd(false); } } catch { }
        });
    }

    // ---------- commands ----------

    static void Submit(string text)
    {
        text = (text ?? "").Trim();
        input.text = "";
        Focus();   // the box loses focus on Enter; keep typing
        if (text == "") return;
        if (history.Count == 0 || history[0] != text) history.Insert(0, text);
        historyIndex = -1;
        scroll = 0;

        Add($"<color={EchoColor}>{Prompt}{Escape(text)}</color>");
        if (text is "clear" or "cls") { lines.Clear(); Render(); return; }
        try
        {
            var (name, args) = Parse(text);
            object reply = DevCommands.Run(name, args);
            if (name.Equals("help", StringComparison.OrdinalIgnoreCase)) AddHelp();
            else Add($"<color={ReplyColor}>{Escape(Format(reply))}</color>");
        }
        catch (Exception e) { Add($"<color={ErrorColor}>{Escape((e.InnerException ?? e).Message)}</color>"); }
        Render();
    }

    static void AddHelp()
    {
        int width = DevCommands.All.Max(c => c.Name.Length) + 2;
        foreach (var c in DevCommands.All)
            Add($"<color={ReplyColor}>{Escape(c.Name.PadRight(width))}</color><color={NoteColor}>{Escape(c.Help)}</color>");
        Add($"<color={NoteColor}>clear: empty this console.  Up/Down: earlier commands.  Tab: complete.  Esc or `: close.</color>");
    }

    static string Format(object reply)
    {
        if (reply is string s) return s;
        return JsonSerializer.Serialize(reply, Json);
    }

    // Readable: apostrophes and accents as themselves, not ' escapes (the text is shown, never put in HTML).
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static void Recall(int direction)
    {
        if (history.Count == 0) return;
        historyIndex = Mathf.Clamp(historyIndex + direction, -1, history.Count - 1);
        input.text = historyIndex < 0 ? "" : history[historyIndex];
        input.MoveTextEnd(false);
    }

    // Tab completes the command name; the console key's character never stays in the box.
    static void TextChanged(string text)
    {
        if (text == null) return;
        bool tab = text.Contains('\t');
        string clean = text.Replace("\t", "").Replace("`", "");
        if (tab && !clean.Contains(' '))
        {
            var matches = DevCommands.All.Where(c => c.Name.StartsWith(clean, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) clean = matches[0].Name + " ";
            else if (matches.Count > 1)
            {
                Add($"<color={NoteColor}>{string.Join("   ", matches.Select(m => m.Name))}</color>");
                Render();
            }
        }
        if (clean != text) { input.SetTextWithoutNotify(clean); input.MoveTextEnd(false); }
    }

    // "name a=1 b=\"two words\" flag" -> ("name", {a:1, b:two words, flag:""})
    internal static (string name, Dictionary<string, string> args) Parse(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (char c in text.Trim())
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        if (tokens.Count == 0) throw new ArgumentException("empty command");

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tokens.Skip(1))
        {
            int eq = t.IndexOf('=');
            if (eq < 0) args[t] = "";
            else args[t.Substring(0, eq)] = t.Substring(eq + 1);
        }
        return (tokens[0], args);
    }

    // ---------- output ----------

    static void Add(string line)
    {
        foreach (var l in line.Split('\n')) lines.Add(l);
        if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
    }

    // The newest lines, ending `scroll` lines from the bottom; the text is bottom-aligned and clipped at the top.
    static void Render()
    {
        if (output == null) return;
        int end = lines.Count - scroll;
        int start = Math.Max(0, end - 200);
        var sb = new StringBuilder();
        for (int i = start; i < end; i++) sb.Append(lines[i]).Append('\n');
        if (scroll > 0) sb.Append($"<color={NoteColor}>-- {scroll} more below (Page Down) --</color>");
        output.text = sb.ToString().TrimEnd('\n');
    }

    // Data shown as text: a zero-width space after '<' keeps TMP from reading it as a tag.
    static string Escape(string s) => (s ?? "").Replace("<", "<​");

    // ---------- building ----------

    static bool Build()
    {
        if (root != null) return true;
        try
        {
            var font = PickFont();
            root = new GameObject("Kit_DevConsole");
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;   // above the game's UI and the kit's windows
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();

            // The panel: top of the screen, slides down.
            panel = NewRect("Panel", root.transform);
            panel.anchorMin = new Vector2(0f, 1f - HeightFraction);
            panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = Background;

            // Output: everything above the input line, clipped, newest at the bottom.
            var outArea = NewRect("Output", panel);
            outArea.anchorMin = Vector2.zero; outArea.anchorMax = Vector2.one;
            outArea.offsetMin = new Vector2(16f, 52f); outArea.offsetMax = new Vector2(-16f, -10f);
            outArea.gameObject.AddComponent<RectMask2D>();
            var outText = NewRect("Text", outArea);
            outText.anchorMin = Vector2.zero; outText.anchorMax = new Vector2(1f, 0f);
            outText.pivot = new Vector2(0.5f, 0f);
            outText.sizeDelta = new Vector2(0f, 4000f);
            output = outText.gameObject.AddComponent<TextMeshProUGUI>();
            Style(output, font, 20f);
            output.alignment = TextAlignmentOptions.BottomLeft;
            output.enableWordWrapping = true;
            output.overflowMode = TextOverflowModes.Overflow;

            // Input line: prompt and box along the bottom.
            var line = NewRect("InputLine", panel);
            line.anchorMin = Vector2.zero; line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.offsetMin = new Vector2(8f, 8f); line.offsetMax = new Vector2(-8f, 44f);
            line.gameObject.AddComponent<Image>().color = InputBack;

            var promptRt = NewRect("Prompt", line);
            promptRt.anchorMin = Vector2.zero; promptRt.anchorMax = new Vector2(0f, 1f);
            promptRt.pivot = new Vector2(0f, 0.5f);
            promptRt.sizeDelta = new Vector2(28f, 0f); promptRt.anchoredPosition = new Vector2(10f, 0f);
            var promptText = promptRt.gameObject.AddComponent<TextMeshProUGUI>();
            Style(promptText, font, 22f);
            promptText.text = $"<color={EchoColor}>></color>";
            promptText.alignment = TextAlignmentOptions.MidlineLeft;

            // TMP_InputField needs its viewport and text before it first enables: build inactive.
            var fieldRt = NewRect("Field", line);
            fieldRt.gameObject.SetActive(false);
            fieldRt.anchorMin = Vector2.zero; fieldRt.anchorMax = Vector2.one;
            fieldRt.offsetMin = new Vector2(38f, 0f); fieldRt.offsetMax = new Vector2(-10f, 0f);
            var viewport = NewRect("Viewport", fieldRt);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            var fieldTextRt = NewRect("Text", viewport);
            fieldTextRt.anchorMin = Vector2.zero; fieldTextRt.anchorMax = Vector2.one;
            fieldTextRt.offsetMin = fieldTextRt.offsetMax = Vector2.zero;
            var fieldText = fieldTextRt.gameObject.AddComponent<TextMeshProUGUI>();
            Style(fieldText, font, 22f);
            fieldText.richText = false;
            fieldText.alignment = TextAlignmentOptions.MidlineLeft;
            fieldText.enableWordWrapping = false;

            input = fieldRt.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = fieldText;
            input.fontAsset = font;
            input.pointSize = 22f;
            input.richText = false;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.customCaretColor = true;
            input.caretColor = Color.white;
            input.caretWidth = 2;
            input.selectionColor = new Color(0.5f, 0.8f, 1f, 0.35f);
            input.onFocusSelectAll = false;
            input.resetOnDeActivation = false;
            input.onSubmit.AddListener(onSubmit);
            input.onValueChanged.AddListener(onChanged);
            // The game's own typing lock: stops movement and hotkeys while the box has focus.
            var lockComp = fieldRt.gameObject.AddComponent<Nivalis.InputFieldPreventExternalInput>();
            lockComp._field = input;
            fieldRt.gameObject.SetActive(true);

            panel.anchoredPosition = new Vector2(0f, 2000f);   // start above the screen; Tick slides it in
            root.SetActive(false);
            if (lines.Count == 0)
                Add($"<color={NoteColor}>Nivalis ModKit {ModKit.Version} dev console. Type help for commands. Font: {font?.name ?? "default"}</color>");
            Render();
            return true;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogError($"DevConsole: could not build the console: {e}");
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            return false;
        }
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static void Style(TMP_Text t, TMP_FontAsset font, float size)
    {
        if (font != null) t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyles.Normal;   // the game's fonts are often set to capitals
        t.color = Color.white;
        t.raycastTarget = false;
    }

    // A plain font from the game: [DevConsole] Font by name, else a monospace or Liberation font, else the font of
    // the title screen's copyright line (normal case, readable at small sizes). Logs what's available once.
    static TMP_FontAsset PickFont()
    {
        var fonts = new List<TMP_FontAsset>();
        try
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>()))
            {
                var f = o.TryCast<TMP_FontAsset>();
                if (f != null && !fonts.Any(x => x.name == f.name)) fonts.Add(f);
            }
        }
        catch { }
        KitPlugin.L.LogInfo($"DevConsole: fonts available: {string.Join(", ", fonts.Select(f => f.name))}");

        string wanted = KitPlugin.ConsoleFont?.Value?.Trim();
        TMP_FontAsset pick = null;
        if (!string.IsNullOrEmpty(wanted)) pick = fonts.FirstOrDefault(f => f.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0);
        pick ??= fonts.FirstOrDefault(f => f.name.IndexOf("mono", StringComparison.OrdinalIgnoreCase) >= 0);
        pick ??= fonts.FirstOrDefault(f => f.name.IndexOf("Liberation", StringComparison.OrdinalIgnoreCase) >= 0);
        pick ??= CopyrightFont();
        pick ??= TMP_Settings.defaultFontAsset;
        pick ??= fonts.FirstOrDefault();
        return pick;
    }

    static TMP_FontAsset CopyrightFont()
    {
        try
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_Text>()))
            {
                var t = o.TryCast<TMP_Text>();
                if (t != null && (t.text ?? "").Contains("©")) return t.font;
            }
        }
        catch { }
        return null;
    }
}
