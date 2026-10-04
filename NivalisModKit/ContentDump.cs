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
            name = Try(() => i.Name),
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
            type = Try(() => v.type.ToString()),
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
    static string TagName(ObjectTag t) => t == null ? null : ((UnityEngine.Object)t).name;

    static T Try<T>(Func<T> f)
    {
        try { return f(); } catch { return default; }
    }
}
