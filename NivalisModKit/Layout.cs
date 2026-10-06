using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisModKit;

/// <summary>
/// A screen panel built from a layout file: boxes (colour, rounded corners, border), text and repeating rows, placed
/// where a browser put them. Layouts are made with the kit's UI Studio (tools/ui-studio): write the panel in HTML and
/// CSS, preview it, export a <c>.layout.json</c>. Elements with an <c>id</c> are named, so the mod fills them in by name.
/// With a watch path the panel rebuilds whenever that file changes, so a layout can be edited while the game runs.
/// Display only for now: the panel doesn't take clicks (they pass through to the game).
/// </summary>
/// <example><code>
/// var view = LayoutView.Load(MyGuid, defaultJson, Path.Combine(Paths.ConfigPath, "MyMod", "panel.layout.json"));
/// view.Visible = true;
/// view.SetText("profit", "123.40");
/// view.Repeat("lowRow", items.Count, row => row.SetText("name", items[row.Index].Name));
/// </code></example>
[Experimental("New in 0.6: the layout format and this API may change.")]
public sealed class LayoutView
{
    /// <summary>The mod that loaded it (its GUID).</summary>
    public string Owner { get; }

    /// <summary>The layout's name, from the file.</summary>
    public string Name => model?.Name ?? "";

    /// <summary>The file watched for changes, or null.</summary>
    public string WatchPath { get; }

    /// <summary>Raised on the main thread after the panel was rebuilt from a changed file (values set before are kept).</summary>
    public event Action Reloaded;

    /// <summary>Shows or hides the panel. It's built the first time it's shown.</summary>
    public bool Visible
    {
        get => visible;
        set
        {
            visible = value;
            if (value && root == null) Build();
            if (root != null) root.SetActive(value);
        }
    }

    /// <summary>
    /// Loads a layout. <paramref name="json"/> is the layout the mod ships (an exported <c>.layout.json</c>, e.g. an
    /// embedded resource); if <paramref name="watchPath"/> is given and that file exists, it's used instead and watched,
    /// so players and modders can restyle the panel by dropping in their own export. Returns null if neither parses
    /// (the reason is logged).
    /// </summary>
    public static LayoutView Load(string owner, string json, string watchPath = null)
    {
        var view = new LayoutView(owner, watchPath);
        string text = json;
        if (watchPath != null && File.Exists(watchPath))
        {
            try { text = File.ReadAllText(watchPath); }
            catch (Exception e) { KitPlugin.L.LogWarning($"Layout {owner}: couldn't read {watchPath}: {e.Message}; using the built-in layout"); }
        }
        view.model = Parse(text, out var error) ?? (text != json ? Parse(json, out error) : null);
        if (view.model == null) { KitPlugin.L.LogWarning($"Layout {owner}: {error}"); return null; }
        view.Watch();
        return view;
    }

    /// <summary>Sets a named text element's text.</summary>
    public void SetText(string name, string text) => Set(name, Kind.Text, text ?? "");

    /// <summary>Sets a named element's colour: a text's colour, or a box's fill.</summary>
    public void SetColor(string name, Color color) => Set(name, Kind.Color, color);

    /// <summary>Sets a named element's width as a fraction (0 to 1) of its designed width, from its left edge: bars.</summary>
    public void SetFill(string name, float fraction) => Set(name, Kind.Fill, Mathf.Clamp01(fraction));

    /// <summary>Shows or hides a named element (and what's inside it).</summary>
    public void SetShown(string name, bool shown) => Set(name, Kind.Shown, shown);

    /// <summary>
    /// Fills a repeating element (one marked <c>data-repeat</c> in the studio): <paramref name="count"/> rows, each
    /// below the last, and <paramref name="fill"/> sets each row's named elements. Rows past the count are hidden.
    /// </summary>
    public void Repeat(string name, int count, Action<LayoutRow> fill)
    {
        if (string.IsNullOrEmpty(name)) return;
        count = Math.Max(0, count);
        repeats[name] = count;
        if (root != null) ShowRows(name, count);
        if (fill == null) return;
        for (int i = 0; i < count; i++)
        {
            try { fill(new LayoutRow(this, $"{name}#{i}/", i)); }
            catch (Exception e) { KitPlugin.L.LogError($"Layout {Owner}/{Name}: filling {name} row {i}: {e.Message}"); }
        }
    }

    /// <summary>Removes the panel and stops watching its file.</summary>
    public void Destroy()
    {
        try { watcher?.Dispose(); } catch { }
        watcher = null;
        KitLoop.Tick -= Poll;
        if (root != null) UnityEngine.Object.Destroy(root);
        root = null;
    }

    // ---------- internals ----------

    internal enum Kind { Text, Color, Fill, Shown }

    sealed class Bound
    {
        public GameObject Go;
        public RectTransform Rect;
        public TMP_Text Text;
        public Image Fill;
        public float DesignWidth;
    }

    sealed class Template
    {
        public Node Node;
        public RectTransform Parent;
        public string Prefix;
        public readonly List<GameObject> Rows = new();
    }

    LayoutModel model;
    GameObject root;
    bool visible;
    readonly Dictionary<string, Bound> bound = new();
    readonly Dictionary<string, Template> templates = new();
    readonly Dictionary<string, int> repeats = new();
    readonly Dictionary<(string path, Kind kind), object> values = new();
    FileSystemWatcher watcher;
    volatile bool changed;
    float changedAt;

    LayoutView(string owner, string watchPath)
    {
        Owner = owner;
        WatchPath = watchPath == null ? null : Path.GetFullPath(watchPath);
    }

    internal void Set(string path, Kind kind, object value)
    {
        if (string.IsNullOrEmpty(path)) return;
        values[(path, kind)] = value;
        if (root != null && bound.TryGetValue(path, out var b)) Apply(b, kind, value);
    }

    static void Apply(Bound b, Kind kind, object value)
    {
        try
        {
            switch (kind)
            {
                case Kind.Text:
                    if (b.Text != null) b.Text.text = (string)value;
                    break;
                case Kind.Color:
                    if (b.Text != null) b.Text.color = (Color)value;
                    else if (b.Fill != null) b.Fill.color = (Color)value;
                    break;
                case Kind.Fill:
                    b.Rect.sizeDelta = new Vector2(b.DesignWidth * (float)value, b.Rect.sizeDelta.y);
                    break;
                case Kind.Shown:
                    b.Go.SetActive((bool)value);
                    break;
            }
        }
        catch { }
    }

    // ---------- building ----------

    void Build()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        bound.Clear();
        templates.Clear();
        try
        {
            root = new GameObject($"KitLayout_{Owner}_{model.Name}");
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = model.Order;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var panel = NewRect("Panel", root.transform);
            Place(panel, model.Anchor, model.Offset, model.Width, model.Height);
            foreach (var node in model.Children) BuildNode(node, panel, "");

            // Rows and values set before (or before a reload).
            foreach (var kv in repeats) ShowRows(kv.Key, kv.Value);
            foreach (var kv in values)
                if (bound.TryGetValue(kv.Key.path, out var b)) Apply(b, kv.Key.kind, kv.Value);
            root.SetActive(visible);
        }
        catch (Exception e)
        {
            KitPlugin.L.LogError($"Layout {Owner}/{model?.Name}: building failed: {e}");
        }
    }

    void BuildNode(Node node, RectTransform parent, string prefix)
    {
        if (node.Repeat > 0 && !string.IsNullOrEmpty(node.Name))
        {
            // A template: its rows are made by Repeat.
            templates[prefix + node.Name] = new Template { Node = node, Parent = parent, Prefix = prefix };
            return;
        }
        var rect = BuildElement(node, parent, node.X, node.Y, prefix + (node.Name ?? ""), !string.IsNullOrEmpty(node.Name));
        foreach (var child in node.Children) BuildNode(child, rect, prefix);
    }

    RectTransform BuildElement(Node node, RectTransform parent, float x, float y, string path, bool named)
    {
        var rect = NewRect(string.IsNullOrEmpty(node.Name) ? node.Type : node.Name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(node.W, node.H);
        var b = new Bound { Go = rect.gameObject, Rect = rect, DesignWidth = node.W };

        // Box: border (outer, rounded) and fill (inset by the border width).
        if (node.Border > 0f && node.BorderColor.a > 0f)
        {
            var edge = rect.gameObject.AddComponent<Image>();
            Shape(edge, node.Radius, node.BorderColor);
            var inner = NewRect("Fill", rect);
            inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(node.Border, node.Border);
            inner.offsetMax = new Vector2(-node.Border, -node.Border);
            b.Fill = inner.gameObject.AddComponent<Image>();
            Shape(b.Fill, Math.Max(0f, node.Radius - node.Border), node.Bg);
        }
        else if (node.Bg.a > 0f || node.Type == "box" && named)
        {
            b.Fill = rect.gameObject.AddComponent<Image>();
            Shape(b.Fill, node.Radius, node.Bg);
        }

        if (node.Type == "text")
        {
            var host = b.Fill != null ? NewRect("Text", rect) : rect;   // a text with a background keeps both
            if (host != rect) { host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = host.offsetMax = Vector2.zero; }
            var t = host.gameObject.AddComponent<TextMeshProUGUI>();
            var font = Fonts.Find(node.Font);
            if (font != null) t.font = font;
            t.text = node.Text ?? "";
            t.fontSize = node.Size;
            t.color = node.Color;
            // Bold on a font that is already bold (Barlow-ExtraBold) would thicken it again.
            bool fakeBold = node.Bold && (font == null || font.name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) < 0);
            t.fontStyle = (fakeBold ? FontStyles.Bold : FontStyles.Normal) | (node.Italic ? FontStyles.Italic : 0)
                        | (node.Upper ? FontStyles.UpperCase : 0);
            if (node.Spacing != 0f && node.Size > 0f) t.characterSpacing = node.Spacing / node.Size * 100f;   // px to em/100
            // Game fonts measure wider than the browser's: text that doesn't wrap may run past its box (left-aligned
            // text to the right, right-aligned to the left) instead of being cut, unless the layout asks for clipping.
            t.enableWordWrapping = node.Wrap;
            t.overflowMode = node.Clip ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            t.alignment = Alignment(node.Align, node.VAlign);
            t.margin = new Vector4(node.PadLeft, node.PadTop, node.PadRight, node.PadBottom);
            t.raycastTarget = false;
            b.Text = t;
        }
        if (node.Opacity < 1f) rect.gameObject.AddComponent<CanvasGroup>().alpha = node.Opacity;
        if (node.Hidden) rect.gameObject.SetActive(false);   // until the mod shows it (SetShown)
        if (named) bound[path] = b;
        return rect;
    }

    void ShowRows(string name, int count)
    {
        if (!templates.TryGetValue(name, out var t)) return;
        var node = t.Node;
        while (t.Rows.Count < Math.Min(count, node.Repeat))
        {
            int i = t.Rows.Count;
            string prefix = $"{t.Prefix}{name}#{i}/";
            var row = BuildElement(node, t.Parent, node.X, node.Y + i * node.Stride, $"{t.Prefix}{name}#{i}", true);
            foreach (var child in node.Children) BuildNode(child, row, prefix);
            t.Rows.Add(row.gameObject);
            foreach (var kv in values)
                if (kv.Key.path.StartsWith(prefix, StringComparison.Ordinal) && bound.TryGetValue(kv.Key.path, out var b)) Apply(b, kv.Key.kind, kv.Value);
        }
        for (int i = 0; i < t.Rows.Count; i++) t.Rows[i].SetActive(i < count);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    // The panel on screen: a corner, edge or the centre, moved inward by the offset.
    static void Place(RectTransform rect, string anchor, Vector2 offset, float w, float h)
    {
        anchor = (anchor ?? "top-left").ToLowerInvariant();
        float ax = anchor.Contains("left") ? 0f : anchor.Contains("right") ? 1f : 0.5f;
        float ay = anchor.Contains("top") ? 1f : anchor.Contains("bottom") ? 0f : 0.5f;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(ax, ay);
        rect.sizeDelta = new Vector2(w, h);
        rect.anchoredPosition = new Vector2(ax == 1f ? -offset.x : offset.x, ay == 1f ? -offset.y : offset.y);
    }

    static TextAlignmentOptions Alignment(string h, string v) => (v, h) switch
    {
        ("top", "center") => TextAlignmentOptions.Top,
        ("top", "right") => TextAlignmentOptions.TopRight,
        ("top", _) => TextAlignmentOptions.TopLeft,
        ("bottom", "center") => TextAlignmentOptions.Bottom,
        ("bottom", "right") => TextAlignmentOptions.BottomRight,
        ("bottom", _) => TextAlignmentOptions.BottomLeft,
        (_, "center") => TextAlignmentOptions.Center,
        (_, "right") => TextAlignmentOptions.Right,
        _ => TextAlignmentOptions.Left,
    };

    // A rounded rectangle: a white 9-sliced sprite per radius, tinted. Radius 0 is a plain image.
    static readonly Dictionary<int, Sprite> shapes = new();

    static void Shape(Image image, float radius, Color color)
    {
        image.color = color;
        image.raycastTarget = false;
        int r = Mathf.RoundToInt(radius);
        if (r <= 0) return;
        if (!shapes.TryGetValue(r, out var sprite) || sprite == null)
        {
            int size = r * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = x + 0.5f, cy = y + 0.5f;
                    float dx = Math.Max(0f, Math.Max(r - cx, cx - (size - r)));
                    float dy = Math.Max(0f, Math.Max(r - cy, cy - (size - r)));
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            shapes[r] = sprite;
        }
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
    }

    // ---------- watching ----------

    void Watch()
    {
        if (WatchPath == null) return;
        try
        {
            string dir = Path.GetDirectoryName(WatchPath);
            Directory.CreateDirectory(dir);
            watcher = new FileSystemWatcher(dir, Path.GetFileName(WatchPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += (_, _) => changed = true;
            watcher.Created += (_, _) => changed = true;
            watcher.Renamed += (_, _) => changed = true;
            watcher.EnableRaisingEvents = true;
            KitLoop.Tick += Poll;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Layout {Owner}: can't watch {WatchPath}: {e.Message}"); }
    }

    void Poll()
    {
        if (!changed) return;
        if (changedAt == 0f) { changedAt = Time.unscaledTime; return; }
        if (Time.unscaledTime - changedAt < 0.3f) return;   // editors and browsers write in steps
        changed = false;
        changedAt = 0f;
        string text;
        try { text = File.ReadAllText(WatchPath); }
        catch { changed = true; return; }   // still being written: next frame
        var next = Parse(text, out var error);
        if (next == null) { KitPlugin.L.LogWarning($"Layout {Owner}: {Path.GetFileName(WatchPath)}: {error}; keeping the previous layout"); return; }
        model = next;
        if (root != null) Build();
        KitPlugin.L.LogInfo($"Layout reloaded: {Path.GetFileName(WatchPath)} ({Count(model.Children)} elements)");
        try { Reloaded?.Invoke(); } catch (Exception e) { KitPlugin.L.LogError($"Layout {Owner}: Reloaded handler: {e.Message}"); }
    }

    static int Count(List<Node> nodes) => nodes.Sum(n => 1 + Count(n.Children));

    // ---------- the file ----------

    sealed class LayoutModel
    {
        public string Name = "layout";
        public string Anchor = "top-left";
        public Vector2 Offset;
        public float Width, Height;
        public int Order = 32700;   // above the game's HUD (its quest tracker draws over 30000), below the kit's console
        public List<Node> Children = new();
    }

    sealed class Node
    {
        public string Type = "box", Name, Text, Font, Align = "left", VAlign = "middle";
        public float X, Y, W, H, Radius, Border, Size = 14f, Opacity = 1f, Stride, Spacing;
        public float PadLeft, PadTop, PadRight, PadBottom;
        public Color Bg = Color.clear, BorderColor = Color.clear, Color = Color.white;
        public bool Bold, Italic, Upper, Wrap, Clip, Hidden;
        public int Repeat;
        public List<Node> Children = new();
    }

    static LayoutModel Parse(string json, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "empty layout"; return null; }
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var r = doc.RootElement;
            if (Str(r, "format") is { } f && !f.StartsWith("nivalis-layout/", StringComparison.Ordinal)) { error = $"not a layout file (format {f})"; return null; }
            var m = new LayoutModel
            {
                Name = Str(r, "name") ?? "layout",
                Anchor = Str(r, "anchor") ?? "top-left",
                Offset = new Vector2(Num(r, "offsetX"), Num(r, "offsetY")),
                Width = Num(r, "w"), Height = Num(r, "h"),
                Order = (int)Num(r, "order", 32700),
            };
            if (r.TryGetProperty("children", out var kids)) foreach (var k in kids.EnumerateArray()) m.Children.Add(ParseNode(k));
            return m;
        }
        catch (Exception e) { error = $"bad layout: {e.Message}"; return null; }
    }

    static Node ParseNode(JsonElement e)
    {
        var n = new Node
        {
            Type = Str(e, "type") ?? "box", Name = Str(e, "name"), Text = Str(e, "text"), Font = Str(e, "font"),
            Align = Str(e, "align") ?? "left", VAlign = Str(e, "valign") ?? "middle",
            X = Num(e, "x"), Y = Num(e, "y"), W = Num(e, "w"), H = Num(e, "h"),
            Radius = Num(e, "radius"), Border = Num(e, "border"), Size = Num(e, "size", 14f), Opacity = Num(e, "opacity", 1f),
            PadLeft = Num(e, "padL"), PadTop = Num(e, "padT"), PadRight = Num(e, "padR"), PadBottom = Num(e, "padB"),
            Bg = Col(e, "bg"), BorderColor = Col(e, "borderColor"), Color = Col(e, "color", Color.white),
            Bold = Bool(e, "bold"), Italic = Bool(e, "italic"), Upper = Bool(e, "upper"), Wrap = Bool(e, "wrap"),
            Clip = Bool(e, "clip"), Hidden = Bool(e, "hidden"),
            Repeat = (int)Num(e, "repeat"), Stride = Num(e, "stride"), Spacing = Num(e, "spacing"),
        };
        if (e.TryGetProperty("children", out var kids)) foreach (var k in kids.EnumerateArray()) n.Children.Add(ParseNode(k));
        return n;
    }

    static string Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static float Num(JsonElement e, string p, float fallback = 0f) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : fallback;
    static bool Bool(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;

    // "#rgb", "#rrggbb" or "#rrggbbaa".
    static Color Col(JsonElement e, string p, Color fallback = default)
    {
        string s = Str(e, p);
        if (string.IsNullOrEmpty(s) || s[0] != '#') return fallback;
        try
        {
            s = s.Substring(1);
            if (s.Length == 3) s = string.Concat(s.Select(c => $"{c}{c}"));
            if (s.Length == 6) s += "ff";
            if (s.Length != 8) return fallback;
            byte B(int i) => Convert.ToByte(s.Substring(i, 2), 16);
            return new Color32(B(0), B(2), B(4), B(6));
        }
        catch { return fallback; }
    }

    // Game fonts by name: a layout names the CSS font family; the closest game font is used, else the default.
    static class Fonts
    {
        static List<TMP_FontAsset> all;
        static readonly Dictionary<string, TMP_FontAsset> picked = new(StringComparer.OrdinalIgnoreCase);

        internal static TMP_FontAsset Find(string family)
        {
            family ??= "";
            if (picked.TryGetValue(family, out var hit) && hit != null) return hit;
            if (all == null || all.Count == 0)
            {
                all = new List<TMP_FontAsset>();
                try
                {
                    foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>()))
                    {
                        var f = o.TryCast<TMP_FontAsset>();
                        if (f != null && !all.Any(x => x.name == f.name)) all.Add(f);
                    }
                }
                catch { }
                if (all.Count > 0) KitPlugin.L.LogInfo($"Layout: game fonts: {string.Join(", ", all.Select(f => f.name))}");
            }
            TMP_FontAsset font = null;
            foreach (var name in family.Split(',').Select(s => s.Trim().Trim('"', '\'')).Where(s => s.Length > 0))
                if ((font = all.FirstOrDefault(f => f.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)) != null) break;
            font ??= all.FirstOrDefault(f => f.name.IndexOf("Liberation", StringComparison.OrdinalIgnoreCase) >= 0);
            font ??= TMP_Settings.defaultFontAsset;
            font ??= all.FirstOrDefault();
            if (font != null) picked[family] = font;
            return font;
        }
    }
}

/// <summary>One row of a repeating layout element (see <see cref="LayoutView.Repeat"/>): sets that row's named elements.</summary>
[Experimental("New in 0.6.")]
public sealed class LayoutRow
{
    readonly LayoutView view;
    readonly string prefix;

    /// <summary>The row's position, from 0.</summary>
    public int Index { get; }

    internal LayoutRow(LayoutView view, string prefix, int index) { this.view = view; this.prefix = prefix; Index = index; }

    /// <summary>Sets a text in this row.</summary>
    public void SetText(string name, string text) => view.Set(prefix + name, LayoutView.Kind.Text, text ?? "");

    /// <summary>Sets a colour in this row (a text's colour, or a box's fill).</summary>
    public void SetColor(string name, Color color) => view.Set(prefix + name, LayoutView.Kind.Color, color);

    /// <summary>Sets a bar's width in this row, as a fraction (0 to 1) of its designed width.</summary>
    public void SetFill(string name, float fraction) => view.Set(prefix + name, LayoutView.Kind.Fill, Mathf.Clamp01(fraction));

    /// <summary>Shows or hides an element in this row.</summary>
    public void SetShown(string name, bool shown) => view.Set(prefix + name, LayoutView.Kind.Shown, shown);
}
