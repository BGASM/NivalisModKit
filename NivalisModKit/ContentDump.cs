using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using Nivalis.CraftingSystem;
using Nivalis.InventorySystem;

namespace NivalisModKit;

// "content dump": every item, recipe and vendor in the game as JSON, for content makers: template names, tags,
// prices, ingredient slots and who sells what. Written to BepInEx\cache\NivalisModKit\content\.
internal static class ContentDump
{
    static readonly JsonSerializerOptions json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static object Write()
    {
        if (!GameEvents.IsInGame) throw new InvalidOperationException("load a save first (vendors and some recipes exist only in gameplay)");
        string dir = Path.Combine(Paths.CachePath, "NivalisModKit", "content");
        Directory.CreateDirectory(dir);

        var items = Items.All.Select(i => new
        {
            asset = Try(() => i.name),
            name = Try(() => string.IsNullOrEmpty(i.Name) ? null : i.Name),   // null: the game has no shown name (decorations, story items)
            guid = Try(() => i.guid?.guid),
            custom = Content.AllItems.Any(c => c.Item != null && c.Item.Pointer == i.Pointer),
            basePrice = Try(() => (float?)i.basePrice),
            stock = Try(() => $"{i.minStockPerVendor}-{i.maxStockPerVendor}"),
            buy = Try(() => (bool?)i.allowBuying),
            sell = Try(() => (bool?)i.allowSelling),
            ingredient = Try(() => (bool?)i.isIngredient),
            furniture = Try(() => (bool?)i.isFurniture),
            plant = Try(() => (bool?)i._isPlant),
            fish = Try(() => (bool?)i.isFish),
            decayDays = Try(() => (int?)i.decayTimeInDays),
            fridge = Try(() => (bool?)i.requiresRefrigeration),
            tags = Try(() => { var l = new System.Collections.Generic.List<string>(); if (i.tags != null) foreach (var t in i.tags) if (t != null) l.Add(TagName(t)); return l.ToArray(); }),
            model = Try(() => i.entityPrefab?.name),
            size = Try(() => Size(i.entityPrefab?.gameObject)),
            icon = Try(() => i.icon?.name),
        }).OrderBy(i => i.asset).ToArray();

        var db = MealDatabase.Instance;
        var known = db?.knownRecipes;
        var defs = new System.Collections.Generic.List<MealRecipeDefinition>();
        if (db?._guidMealRecipeMap != null)
            foreach (var kv in db._guidMealRecipeMap)
                if (kv.Value != null) defs.Add(kv.Value);
        var recipes = defs
            .Select(r => new
            {
                asset = Try(() => r.name),
                guid = Try(() => r.guid?.guid),
                custom = Content.AllRecipes.Any(c => c.Recipe != null && c.Recipe.Pointer == r.Pointer),
                output = Try(() => r.output?.type?.name),
                amount = Try(() => (int?)r.output?.amount),
                type = Try(() => r.recipeType.ToString()),
                unlockCost = Try(() => (int?)r.unlockCost),
                known = Try(() => (bool?)(known != null && known.Contains(r))),
                disabled = Try(() => (bool?)r.disabled),
                playerOnly = Try(() => (bool?)r.playerOnly),
                prerequisites = Try(() => { var l = new System.Collections.Generic.List<string>(); if (r.unlockPrerequisites != null) foreach (var p in r.unlockPrerequisites) l.Add(p?.name); return l.ToArray(); }),
                inputs = Try(() => r.inputsNew?.Select(n => new
                {
                    item = Try(() => n.DefaultItem?.name),
                    amount = Try(() => (int?)n.Amount),
                    slot = Try(() => n.Slot.ToString()),
                    processing = Try(() => n.ProcessingType.ToString()),
                    substitutes = Try(() => n.AllowedTags?.Select(TagName).Where(x => x != null).ToArray()),
                }).ToArray()),
            }).OrderBy(r => r.asset).ToArray();

        var vendors = Economy.Vendors.Select(v => new
        {
            name = Economy.NameOf(v),
            district = Try(() => World.NameOf(Economy.DistrictOf(v))),
            type = Try(() => ((UnityEngine.Object)v.type)?.name),
            tier = Try(() => v.tier.ToString()),
            offers = Try(() => v.offerredItems?.Select(o => new
            {
                by = Try(() => o.type.ToString()),
                tag = Try(() => TagName(o.tag)),
                item = Try(() => o.itemType?.name),
                priceBonus = Try(() => (int?)o.priceBonus),
                stockMultiplier = Try(() => (float?)o.stockMultiplier),
            }).ToArray()),
            excluded = Try(() => v.excludedItems?.Select(e => e?.name).ToArray()),
        }).OrderBy(v => v.name).ToArray();

        File.WriteAllText(Path.Combine(dir, "items.json"), JsonSerializer.Serialize(items, json));
        File.WriteAllText(Path.Combine(dir, "recipes.json"), JsonSerializer.Serialize(recipes, json));
        File.WriteAllText(Path.Combine(dir, "vendors.json"), JsonSerializer.Serialize(vendors, json));
        KitPlugin.L.LogInfo($"Content dump: {items.Length} items, {recipes.Length} recipes, {vendors.Length} vendors -> {dir}");
        return new { folder = dir, items = items.Length, recipes = recipes.Length, vendors = vendors.Length };
    }

    // ObjectTag has its own "name" field (a localized label) hiding the asset name.
    // The model's visible size in metres (width x, height y, depth z), from its meshes as the prefab holds them.
    // Same meshes the model swap looks at: enabled renderers, shadow meshes left out.
    static object Size(UnityEngine.GameObject prefab)
    {
        if (prefab == null) return null;
        // In the root's axes but at real scale: many furniture roots are scaled down (0.08 is common), and the size is
        // what the player sees.
        var root = UnityEngine.Matrix4x4.Scale(prefab.transform.lossyScale) * prefab.transform.worldToLocalMatrix;
        bool any = false;
        UnityEngine.Vector3 min = default, max = default;
        foreach (var r in prefab.GetComponentsInChildren<UnityEngine.MeshRenderer>(true))
        {
            if (!r.enabled || r.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            var mesh = r.GetComponent<UnityEngine.MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            var b = mesh.bounds;
            var m = root * r.transform.localToWorldMatrix;
            for (int c = 0; c < 8; c++)
            {
                var p = m.MultiplyPoint3x4(new UnityEngine.Vector3(
                    (c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z));
                if (!any) { min = max = p; any = true; }
                else { min = UnityEngine.Vector3.Min(min, p); max = UnityEngine.Vector3.Max(max, p); }
            }
        }
        if (!any) return null;
        var s = max - min;
        return new { x = MathF.Round(s.x, 2), y = MathF.Round(s.y, 2), z = MathF.Round(s.z, 2) };
    }

    static string TagName(ObjectTag t) => t == null ? null : ((UnityEngine.Object)t).name;

    static T Try<T>(Func<T> f)
    {
        try { return f(); } catch { return default; }
    }
}
