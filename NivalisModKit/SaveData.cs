using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace NivalisModKit;

/// <summary>
/// Data that belongs to one save, stored next to it as <c>&lt;save&gt;.modkit.json</c>. Values are
/// kept per mod and per key, as JSON. Read and set them any time during gameplay; the kit writes
/// the file when the game saves and reads it back when that save loads. A new game starts empty.
/// </summary>
public static class SaveData
{
    /// <summary>
    /// The current save's data was loaded, or reset for a new game. Raised just before
    /// <see cref="GameEvents.GameLoaded"/> / <see cref="GameEvents.NewGameStarted"/>.
    /// </summary>
    public static event Action Loaded;

    /// <summary>The game is saving: store any values you keep elsewhere now, before the file is written.</summary>
    public static event Action Saving;

    // mod id -> key -> value
    static Dictionary<string, Dictionary<string, JsonElement>> data = new();
    static readonly JsonSerializerOptions json = new() { WriteIndented = true };

    /// <summary>The store for one mod. Use your plugin GUID as the id.</summary>
    public static ModSaveData For(string modId)
    {
        if (string.IsNullOrEmpty(modId)) throw new ArgumentException("modId is required", nameof(modId));
        return new ModSaveData(modId);
    }

    internal static bool TryGet(string mod, string key, out JsonElement value)
    {
        value = default;
        return data.TryGetValue(mod, out var keys) && keys.TryGetValue(key, out value);
    }

    internal static void Set(string mod, string key, JsonElement value)
    {
        if (!data.TryGetValue(mod, out var keys)) data[mod] = keys = new Dictionary<string, JsonElement>();
        keys[key] = value;
    }

    internal static bool Remove(string mod, string key) =>
        data.TryGetValue(mod, out var keys) && keys.Remove(key);

    internal static IReadOnlyCollection<string> Keys(string mod) =>
        data.TryGetValue(mod, out var keys) ? keys.Keys : Array.Empty<string>();

    static string PathFor(string saveName)
    {
        if (string.IsNullOrEmpty(saveName)) return null;
        foreach (char c in Path.GetInvalidFileNameChars())
            if (saveName.IndexOf(c) >= 0) return null;
        return Path.Combine(Application.persistentDataPath, saveName + ".modkit.json");
    }

    // ---------- called by the kit's game hooks (main thread) ----------

    internal static void OnNewGame()
    {
        data = new();
        RaiseLoaded();
    }

    internal static void OnLoaded(string saveName)
    {
        data = new();
        string path = PathFor(saveName);
        try
        {
            if (path != null && File.Exists(path))
            {
                data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(
                    File.ReadAllText(path)) ?? new();
                KitPlugin.L.LogInfo($"Save data: read {Path.GetFileName(path)} ({data.Count} mods)");
            }
        }
        catch (Exception e)
        {
            data = new();
            KitPlugin.L.LogError($"Save data: could not read {Path.GetFileName(path)}, starting empty: {e.Message}");
        }
        RaiseLoaded();
    }

    internal static void OnSaved(string saveName)
    {
        GameEvents.Raise($"SaveData.{nameof(Saving)}", Saving);
        string path = PathFor(saveName);
        if (path == null) return;
        try
        {
            if (data.Count == 0)
            {
                // Nothing to store; drop a file left by an earlier save under this name.
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            // Write a temp file and swap it in, so a crash mid-write can't leave half a file.
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, json));
            File.Move(temp, path, overwrite: true);
            KitPlugin.L.LogInfo($"Save data: wrote {Path.GetFileName(path)} ({data.Count} mods)");
        }
        catch (Exception e) { KitPlugin.L.LogError($"Save data: could not write {Path.GetFileName(path)}: {e.Message}"); }
    }

    static void RaiseLoaded() => GameEvents.Raise($"SaveData.{nameof(Loaded)}", Loaded);
}

/// <summary>One mod's values in the current save. Get one from <see cref="SaveData.For"/>.</summary>
public sealed class ModSaveData
{
    readonly string mod;

    internal ModSaveData(string mod) => this.mod = mod;

    /// <summary>
    /// The value stored under <paramref name="key"/>, or <paramref name="fallback"/> if there is none
    /// or it can't be read as <typeparamref name="T"/>.
    /// </summary>
    public T Get<T>(string key, T fallback = default)
    {
        if (!SaveData.TryGet(mod, key, out var element)) return fallback;
        try { return element.Deserialize<T>(); }
        catch { return fallback; }
    }

    /// <summary>Stores a value. It must be JSON-serializable: numbers, strings, lists, plain classes.</summary>
    public void Set<T>(string key, T value) => SaveData.Set(mod, key, JsonSerializer.SerializeToElement(value));

    /// <summary>True if a value is stored under the key.</summary>
    public bool Has(string key) => SaveData.TryGet(mod, key, out _);

    /// <summary>Removes a value. True if there was one.</summary>
    public bool Remove(string key) => SaveData.Remove(mod, key);

    /// <summary>The keys this mod has stored.</summary>
    public IReadOnlyCollection<string> Keys => SaveData.Keys(mod);
}
