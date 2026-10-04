using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;

namespace NivalisModKit;

/// <summary>
/// Content without code: every <c>*.content.json</c> file under <c>BepInEx\plugins</c> (any subfolder) is read at
/// startup and its items and recipes added through <see cref="Content"/>. Icon paths are relative to the file.
/// </summary>
/// <example><code>
/// {
///   "owner": "you.cherrypack",
///   "items": [
///     { "id": "cherry-cola", "template": "Cola", "name": "Cherry Cola",
///       "description": "Cola with a cherry kick.", "basePrice": 4.5, "icon": "cherry-cola.png",
///       "model": { "bundle": "cherry-cola", "asset": "CherryCola" } }
///   ],
///   "recipes": [
///     { "id": "cherry-cola", "template": "Cola", "output": "Cherry Cola",
///       "replace": { "Sugar": "Cherries" }, "unlockCost": 2, "knownFromStart": false }
///   ]
/// }
/// </code></example>
[Experimental("Follows Content: the file format may grow, but existing fields keep their meaning.")]
public static class ContentPacks
{
    /// <summary>A content file that was read.</summary>
    public sealed class Pack
    {
        /// <summary>Full path of the file.</summary>
        public string Path { get; internal set; }
        /// <summary>Its owner (the "owner" field).</summary>
        public string Owner { get; internal set; }
        /// <summary>Items and recipes it added.</summary>
        public int Items { get; internal set; }
        /// <inheritdoc cref="Items"/>
        public int Recipes { get; internal set; }
        /// <summary>Problems found while reading it (also in the log).</summary>
        public List<string> Problems { get; } = new();
    }

    static readonly List<Pack> packs = new();

    /// <summary>The content files read at startup.</summary>
    public static IReadOnlyList<Pack> Loaded => packs;

    // ---------- file format ----------

    sealed class FileModel
    {
        public string Owner { get; set; }
        public List<ItemModel> Items { get; set; }
        public List<RecipeModel> Recipes { get; set; }
    }

    sealed class ItemModel
    {
        public string Id { get; set; }
        public string Template { get; set; }
        public string Name { get; set; }
        public string ShortName { get; set; }
        public string Description { get; set; }
        public float? BasePrice { get; set; }
        public int? MinStock { get; set; }
        public int? MaxStock { get; set; }
        public int? DecayDays { get; set; }
        public string Icon { get; set; }
        public ModelRef Model { get; set; }
    }

    sealed class ModelRef
    {
        public string Bundle { get; set; }
        public string Asset { get; set; }
    }

    sealed class RecipeModel
    {
        public string Id { get; set; }
        public string Template { get; set; }
        public string Output { get; set; }
        public int? OutputAmount { get; set; }
        public Dictionary<string, string> Replace { get; set; }
        public int? UnlockCost { get; set; }
        public bool KnownFromStart { get; set; }
    }

    static readonly JsonSerializerOptions json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // ---------- loading ----------

    internal static void LoadAll()
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(Paths.PluginPath, "*.content.json", SearchOption.AllDirectories).ToList(); }
        catch (Exception e) { KitPlugin.L.LogWarning($"Content packs: could not search {Paths.PluginPath}: {e.Message}"); return; }
        foreach (var file in files) Load(file);
        if (packs.Count > 0)
            KitPlugin.L.LogInfo($"Content packs: {packs.Count} file(s), {packs.Sum(p => p.Items)} item(s), {packs.Sum(p => p.Recipes)} recipe(s)");
    }

    static void Load(string file)
    {
        var pack = new Pack { Path = file };
        packs.Add(pack);
        void Problem(string text)
        {
            pack.Problems.Add(text);
            KitPlugin.L.LogWarning($"Content pack {System.IO.Path.GetFileName(file)}: {text}");
        }

        FileModel model;
        try { model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(file), json); }
        catch (Exception e) { Problem($"not valid JSON ({e.Message})"); return; }
        if (model == null || string.IsNullOrWhiteSpace(model.Owner)) { Problem("needs an \"owner\" (a unique name for your pack, e.g. \"you.cherrypack\")"); return; }
        pack.Owner = model.Owner;
        string dir = System.IO.Path.GetDirectoryName(file) ?? "";

        foreach (var i in model.Items ?? new List<ItemModel>())
        {
            try
            {
                Content.AddItem(model.Owner, i.Id, i.Template, s =>
                {
                    s.Name = i.Name; s.ShortName = i.ShortName; s.Description = i.Description;
                    s.BasePrice = i.BasePrice; s.MinStock = i.MinStock; s.MaxStock = i.MaxStock; s.DecayDays = i.DecayDays;
                    if (!string.IsNullOrEmpty(i.Icon)) s.IconPath = System.IO.Path.Combine(dir, i.Icon);
                    if (i.Model != null && !string.IsNullOrEmpty(i.Model.Bundle))
                    {
                        s.ModelBundlePath = System.IO.Path.Combine(dir, i.Model.Bundle);
                        s.ModelAsset = i.Model.Asset;
                    }
                });
                pack.Items++;
            }
            catch (Exception e) { Problem($"item '{i?.Id}': {e.Message}"); }
        }

        foreach (var r in model.Recipes ?? new List<RecipeModel>())
        {
            try
            {
                Content.AddRecipe(model.Owner, r.Id, r.Template, s =>
                {
                    s.Output = r.Output; s.OutputAmount = r.OutputAmount; s.UnlockCost = r.UnlockCost; s.KnownFromStart = r.KnownFromStart;
                    foreach (var kv in r.Replace ?? new Dictionary<string, string>()) s.ReplaceIngredient[kv.Key] = kv.Value;
                });
                pack.Recipes++;
            }
            catch (Exception e) { Problem($"recipe '{r?.Id}': {e.Message}"); }
        }
    }
}
