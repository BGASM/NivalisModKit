using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NivalisModKit;

/// <summary>How <see cref="Photo.TopDown"/> takes its picture.</summary>
[Experimental("New in 0.4.")]
public sealed class PhotoOptions
{
    /// <summary>Resolution: image pixels per world metre. Lowered if the image would exceed <see cref="MaxSize"/>.</summary>
    public float PixelsPerMetre { get; set; } = 10f;
    /// <summary>Largest image side, pixels.</summary>
    public int MaxSize { get; set; } = 4096;
    /// <summary>A world height (y) above which everything is left out (roofs, awnings, wires), or null to keep all.</summary>
    public float? CutAt { get; set; }
    /// <summary>Layers left out. Defaults to <see cref="Photo.DefaultExcludedLayers"/>.</summary>
    public string[] ExcludeLayers { get; set; } = Photo.DefaultExcludedLayers;
    /// <summary>
    /// Renderers switched off for the picture when their shader, material or object name contains one of these.
    /// Defaults to <see cref="Photo.DefaultHidden"/>.
    /// </summary>
    public string[] Hide { get; set; } = Photo.DefaultHidden;
    /// <summary>Local lights (not the sun) brighter than this are turned down for the picture; 0 leaves them.</summary>
    public float LightCap { get; set; } = 3f;
    /// <summary>Brightness multiplier.</summary>
    public float Exposure { get; set; } = 1f;
    /// <summary>Brightness (0..1) above which highlights roll off towards white instead of clipping.</summary>
    public float Knee { get; set; } = 0.75f;
}

/// <summary>A picture from <see cref="Photo.TopDown"/>, and how it maps onto the world.</summary>
[Experimental("New in 0.4.")]
public sealed class PhotoResult
{
    /// <summary>The image, row by row from the south edge (lowest z), as Texture2D stores pixels.</summary>
    public Color32[] Pixels { get; internal set; }
    /// <summary>Image width, pixels.</summary>
    public int Width { get; internal set; }
    /// <summary>Image height, pixels.</summary>
    public int Height { get; internal set; }
    /// <summary>Image pixels per world metre.</summary>
    public float PixelsPerMetre { get; internal set; }
    /// <summary>The world x and z of the image's lower-left corner (pixel 0, 0).</summary>
    public Vector2 Origin { get; internal set; }
    /// <summary>Lights turned down for the picture.</summary>
    public int CappedLights { get; internal set; }
    /// <summary>Renderers switched off for the picture.</summary>
    public int HiddenRenderers { get; internal set; }
    /// <summary>True if a washed-out first render was replaced (see <see cref="Photo.TopDown"/>).</summary>
    public bool Retaken { get; internal set; }
    /// <summary>Time spent rendering, milliseconds.</summary>
    public double RenderMs { get; internal set; }

    /// <summary>A world position's pixel (x across, y up from the south edge). May fall outside the image.</summary>
    public Vector2 ToPixel(Vector3 world) =>
        new((world.x - Origin.x) * PixelsPerMetre, (world.z - Origin.y) * PixelsPerMetre);

    /// <summary>The image as a new texture (clamped at the edges). Destroy it when done.</summary>
    public Texture2D ToTexture()
    {
        var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixels32(Pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>The image as PNG bytes.</summary>
    public byte[] ToPng()
    {
        var tex = ToTexture();
        try { return ImageConversion.EncodeToPNG(tex); }
        finally { UnityEngine.Object.Destroy(tex); }
    }
}

/// <summary>Pictures of the world taken from code.</summary>
[Experimental("New in 0.4.")]
public static class Photo
{
    /// <summary>
    /// Layers left out by default: people (NPCs would be frozen mid-step; the player), traffic, and UI floating in the
    /// world.
    /// </summary>
    public static readonly string[] DefaultExcludedLayers = { Layers.Character, Layers.Player, Layers.Vehicle, Layers.UI, Layers.WorldUI };

    /// <summary>
    /// Shaders hidden by default: the game's grass, vine and bush shaders draw as black silhouettes for any camera but
    /// its own.
    /// </summary>
    public static readonly string[] DefaultHidden = { "Vegetation/Procedural", "Advanced Bush" };

    /// <summary>
    /// The world straight down from above, orthographic (no perspective: every metre is the same number of pixels), over
    /// the x/z extent of <paramref name="area"/>, north (+z) up. Everything is put back after: hidden renderers, capped
    /// lights. Rendered in high dynamic range and tone mapped, since the game's own camera tames bright lights with
    /// post-processing this camera doesn't have. Rendered twice, keeping the darker if one comes out washed out (it
    /// happens now and then, a game-wide flash of some kind). Takes a fraction of a second: take it once and keep it.
    /// </summary>
    /// <example><code>
    /// var mesh = Navigation.Triangulate();
    /// var shot = Photo.TopDown(mesh.Bounds);                        // the walkable district from above
    /// File.WriteAllBytes(path, shot.ToPng());
    /// Vector2 me = shot.ToPixel(Player.Position);                   // where the player is on it
    /// </code></example>
    public static PhotoResult TopDown(Bounds area, PhotoOptions options = null)
    {
        var o = options ?? new PhotoOptions();
        float sizeX = Mathf.Max(area.size.x, 1f), sizeZ = Mathf.Max(area.size.z, 1f);
        float ppm = Mathf.Min(o.PixelsPerMetre, o.MaxSize / Mathf.Max(sizeX, sizeZ));
        var result = new PhotoResult
        {
            PixelsPerMetre = ppm,
            Width = Mathf.Max(1, Mathf.CeilToInt(sizeX * ppm)),
            Height = Mathf.Max(1, Mathf.CeilToInt(sizeZ * ppm)),
            Origin = new Vector2(area.min.x, area.min.z),
        };

        int mask = Layers.AllExcept(o.ExcludeLayers ?? Array.Empty<string>());
        var hidden = HideRenderers(o.Hide ?? Array.Empty<string>());
        var capped = CapLights(o.LightCap);
        result.HiddenRenderers = hidden.Count;
        result.CappedLights = capped.Count;
        Color[] raw;
        var started = DateTime.UtcNow;
        try
        {
            raw = Render(result, area, o.CutAt, mask);
            var second = Render(result, area, o.CutAt, mask);
            float a = MeanBrightness(raw), b = MeanBrightness(second);
            if (a > b * 1.5f) { raw = second; result.Retaken = true; }
            else if (b > a * 1.5f) result.Retaken = true;
        }
        finally
        {
            foreach (var r in hidden) if (r != null) r.enabled = true;
            foreach (var (light, intensity) in capped) if (light != null) light.intensity = intensity;
        }
        result.RenderMs = (DateTime.UtcNow - started).TotalMilliseconds;

        bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        result.Pixels = new Color32[raw.Length];
        for (int i = 0; i < raw.Length; i++) result.Pixels[i] = ToneMap(raw[i], o.Exposure, o.Knee, linear);
        return result;
    }

    static Color[] Render(PhotoResult r, Bounds area, float? cutAt, int cullingMask)
    {
        float worldW = r.Width / r.PixelsPerMetre, worldH = r.Height / r.PixelsPerMetre;
        float top = area.max.y + 300f;
        var go = new GameObject("NivalisModKit_PhotoCamera");
        RenderTexture rt = null;
        Texture2D tex = null;
        try
        {
            go.transform.position = new Vector3(r.Origin.x + worldW / 2f, top, r.Origin.y + worldH / 2f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // looking down, image up = north (+z)
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;                                     // renders only when asked
            cam.orthographic = true;
            cam.orthographicSize = worldH / 2f;
            cam.aspect = (float)r.Width / r.Height;
            cam.nearClipPlane = cutAt is { } cut ? Mathf.Max(0.3f, top - cut) : 0.3f;
            cam.farClipPlane = top - area.min.y + 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = cullingMask;
            cam.allowHDR = true;

            rt = new RenderTexture(r.Width, r.Height, 24, RenderTextureFormat.ARGBHalf);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            tex = new Texture2D(r.Width, r.Height, TextureFormat.RGBAHalf, false);
            tex.ReadPixels(new Rect(0, 0, r.Width, r.Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            var src = tex.GetPixels();
            var pixels = new Color[src.Length];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = src[i];
            return pixels;
        }
        finally
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            UnityEngine.Object.Destroy(go);
        }
    }

    // Local lights (not the sun) brighter than cap are turned down to it; returns them with their intensity to restore.
    static List<(Light, float)> CapLights(float cap)
    {
        var capped = new List<(Light, float)>();
        if (cap <= 0f) return capped;
        foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Light>()))
        {
            var l = o.TryCast<Light>();
            if (l == null || !l.enabled || l.type == LightType.Directional || l.intensity <= cap) continue;
            capped.Add((l, l.intensity));
            l.intensity = cap;
        }
        return capped;
    }

    static List<Renderer> HideRenderers(string[] words)
    {
        var hidden = new List<Renderer>();
        if (words.Length == 0) return hidden;
        foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Renderer>()))
        {
            var r = o.TryCast<Renderer>();
            if (r == null || !r.enabled) continue;
            string names = r.gameObject.name;
            try
            {
                foreach (var m in r.sharedMaterials)
                    if (m != null) names += "|" + m.name + "|" + (m.shader != null ? m.shader.name : "");
            }
            catch { }
            if (!words.Any(w => names.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            r.enabled = false;
            hidden.Add(r);
        }
        return hidden;
    }

    static float MeanBrightness(Color[] pixels)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < pixels.Length; i += 16) { var c = pixels[i]; sum += c.r + c.g + c.b; n++; }
        return n == 0 ? 0f : (float)(sum / n);
    }

    // Exposure, then a soft shoulder: brightness below the knee is unchanged; above it, it approaches white instead of
    // clipping. Applied to the brightest channel so colours keep their hue. Linear values are encoded to sRGB.
    static Color32 ToneMap(Color c, float exposure, float knee, bool linear)
    {
        float r = c.r * exposure, g = c.g * exposure, b = c.b * exposure;
        if (linear) { r = ToSrgb(r); g = ToSrgb(g); b = ToSrgb(b); }
        float m = Mathf.Max(r, Mathf.Max(g, b));
        if (m > knee && m > 0f)
        {
            float over = m - knee, room = 1f - knee;
            float shoulder = knee + room * (1f - Mathf.Exp(-over / Mathf.Max(room, 1e-4f)));
            float s = shoulder / m;
            r *= s; g *= s; b *= s;
        }
        return new Color32(B(r), B(g), B(b), 255);
    }

    static float ToSrgb(float v) => v <= 0f ? 0f : v < 0.0031308f ? v * 12.92f : 1.055f * Mathf.Pow(v, 1f / 2.4f) - 0.055f;

    static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
}
