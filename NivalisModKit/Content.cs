using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.InventorySystem;
using Nivalis.Localization;
using UnityEngine;

namespace NivalisModKit;

/// <summary>
/// Adds new things to the game: items and recipes, each made by copying an existing one and changing what you list.
/// A copied item keeps its template's model, tags, decay and cooking use, so vendors that sell by tag stock it like
/// its template. A copied recipe keeps its template's ingredient slots, processing and place in the unlock panel.
/// Register in your plugin's Load, or ship a <c>*.content.json</c> file (see <see cref="ContentPacks"/>).
/// </summary>
/// <example><code>
/// Content.AddItem(MyGuid, "cherry-cola", "Cola", item =>
/// {
///     item.Name = "Cherry Cola";
///     item.Description = "Cola with a cherry kick.";
///     item.BasePrice = 4.5f;
/// });
/// Content.AddRecipe(MyGuid, "cherry-cola", "Cola", recipe =>
/// {
///     recipe.Output = "Cherry Cola";
///     recipe.ReplaceIngredient["Sugar"] = "Cherries";
///     recipe.KnownFromStart = true;
/// });
/// </code></example>
/// <remarks>
/// Experimental. Every item and recipe gets a fixed GUID from your mod's GUID and the id you give, so saves find it
/// again on the next load. If a content mod is removed, the game skips its items, placed objects and recipes when a save
/// loads, and they return when the mod is reinstalled; saving without the mod drops them from that save for good.
/// </remarks>
[Experimental("Items, recipes, models and placeable furniture, tested in game. The API may still grow.")]
public static class Content
{
    // ---------- items ----------

    /// <summary>What to change on a copied item. Anything left null keeps the template's value.</summary>
    public sealed class ItemSpec
    {
        /// <summary>Name shown in the game (also the name <see cref="Items.ByName"/> finds). Required.</summary>
        public string Name { get; set; }
        /// <summary>Short name (inventory slots); defaults to <see cref="Name"/>.</summary>
        public string ShortName { get; set; }
        /// <summary>Description shown in tooltips.</summary>
        public string Description { get; set; }
        /// <summary>Base price in the game's units (the template's <c>basePrice</c>; vendors vary it daily).</summary>
        public float? BasePrice { get; set; }
        /// <summary>Fewest a vendor stocks. 0 for both this and <see cref="MaxStock"/>: vendors don't sell it.</summary>
        public int? MinStock { get; set; }
        /// <summary>Most a vendor stocks.</summary>
        public int? MaxStock { get; set; }
        /// <summary>Days until it spoils (ingredients and meals).</summary>
        public int? DecayDays { get; set; }
        /// <summary>A PNG file for the icon (square works best). Null keeps the template's icon.</summary>
        public string IconPath { get; set; }
        /// <summary>
        /// A Unity AssetBundle file (built with Unity 2020.3, built-in render pipeline) holding the 3D model to use
        /// instead of the template's. Null keeps the template's model.
        /// </summary>
        public string ModelBundlePath { get; set; }
        /// <summary>The model's name inside the bundle (the imported model's name, e.g. "Thomas").</summary>
        public string ModelAsset { get; set; }
    }

    /// <summary>An item a mod added.</summary>
    public sealed class CustomItem
    {
        /// <summary>The mod that added it.</summary>
        public string Owner { get; internal set; }
        /// <summary>The id it was registered with (unique within the mod).</summary>
        public string Id { get; internal set; }
        /// <summary>The item's GUID, fixed for this owner and id.</summary>
        public string Guid { get; internal set; }
        /// <summary>The name of the item it was copied from.</summary>
        public string Template { get; internal set; }
        /// <summary>What was changed.</summary>
        public ItemSpec Spec { get; internal set; }
        /// <summary>The game's item once built (the game builds items at startup), else null.</summary>
        public ItemType Item { get; internal set; }
        /// <summary>For furniture and other placeable items: the prefab GUID placed copies are saved with.</summary>
        public string PrefabGuid { get; internal set; }
        internal string LocGuid;
        internal LocItemPlain Texts;
    }

    static readonly List<CustomItem> items = new();

    /// <summary>Items added by mods.</summary>
    public static IReadOnlyList<CustomItem> AllItems => items;

    /// <summary>
    /// Adds an item copied from <paramref name="template"/> (an item name, as <see cref="Items.ByName"/> takes it).
    /// <paramref name="id"/> must be unique within your mod and must never change once players have it in saves.
    /// </summary>
    public static CustomItem AddItem(string owner, string id, string template, Action<ItemSpec> configure)
    {
        Require(owner, id, template);
        if (items.Any(i => i.Owner == owner && i.Id == id)) throw new ArgumentException($"{owner} already added an item '{id}'");
        var spec = new ItemSpec();
        configure?.Invoke(spec);
        if (string.IsNullOrEmpty(spec.Name)) throw new ArgumentException($"item '{id}': Name is required");
        var c = new CustomItem
        {
            Owner = owner, Id = id, Template = template, Spec = spec,
            Guid = StableGuid($"{owner}/{id}"), LocGuid = StableGuid($"{owner}/{id}/loc"),
        };
        items.Add(c);
        if (itemsBuilt) KitPlugin.L.LogWarning($"Content: item '{id}' from {owner} was added after the game built its items; it appears from the next launch");
        return c;
    }

    // ---------- recipes ----------

    /// <summary>What to change on a copied recipe. Anything left null keeps the template's value.</summary>
    public sealed class RecipeSpec
    {
        /// <summary>The dish it makes: an item name (your own items included). Null keeps the template's dish.</summary>
        public string Output { get; set; }
        /// <summary>How many it makes; null keeps the template's amount.</summary>
        public int? OutputAmount { get; set; }
        /// <summary>
        /// Ingredient swaps by item name: <c>["Sugar"] = "Cherries"</c> makes the slot whose default is Sugar use
        /// Cherries instead. The slot keeps its amount, processing (chopped, fried...) and allowed substitutes.
        /// </summary>
        public Dictionary<string, string> ReplaceIngredient { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Inspiration points to unlock it in the cooking screen; null keeps the template's cost.</summary>
        public int? UnlockCost { get; set; }
        /// <summary>True: the player knows it as soon as a game loads, without unlocking it.</summary>
        public bool KnownFromStart { get; set; }
    }

    /// <summary>A recipe a mod added.</summary>
    public sealed class CustomRecipe
    {
        /// <summary>The mod that added it.</summary>
        public string Owner { get; internal set; }
        /// <summary>The id it was registered with (unique within the mod).</summary>
        public string Id { get; internal set; }
        /// <summary>The recipe's GUID, fixed for this owner and id.</summary>
        public string Guid { get; internal set; }
        /// <summary>The template: a dish name (its recipe is copied) or a recipe asset name.</summary>
        public string Template { get; internal set; }
        /// <summary>What was changed.</summary>
        public RecipeSpec Spec { get; internal set; }
        /// <summary>The game's recipe once built, else null.</summary>
        public MealRecipeDefinition Recipe { get; internal set; }
    }

    static readonly List<CustomRecipe> recipes = new();

    /// <summary>Recipes added by mods.</summary>
    public static IReadOnlyList<CustomRecipe> AllRecipes => recipes;

    /// <summary>
    /// Adds a recipe copied from the recipe of <paramref name="template"/> (a dish name like "Cola", or a recipe asset
    /// name). <paramref name="id"/> must be unique within your mod and must never change once players know it.
    /// </summary>
    public static CustomRecipe AddRecipe(string owner, string id, string template, Action<RecipeSpec> configure)
    {
        Require(owner, id, template);
        if (recipes.Any(r => r.Owner == owner && r.Id == id)) throw new ArgumentException($"{owner} already added a recipe '{id}'");
        var spec = new RecipeSpec();
        configure?.Invoke(spec);
        var c = new CustomRecipe { Owner = owner, Id = id, Template = template, Spec = spec, Guid = StableGuid($"{owner}/{id}/recipe") };
        recipes.Add(c);
        if (recipesBuilt) KitPlugin.L.LogWarning($"Content: recipe '{id}' from {owner} was added after the game built its recipes; it appears from the next launch");
        return c;
    }

    // ---------- building into the game ----------

    static bool itemsBuilt, recipesBuilt;

    internal static void Install(Harmony harmony)
    {
        // InitializeProviderData reloads _allItems from Resources and builds the GUID map from it, so the copies go in
        // afterwards: appended to the list and added to the map.
        harmony.Patch(AccessTools.Method(typeof(ItemDatabase), nameof(ItemDatabase.InitializeProviderData)),
            postfix: new HarmonyMethod(typeof(Content), nameof(AfterItemDatabase)));
        harmony.Patch(AccessTools.Method(typeof(MealDatabase), nameof(MealDatabase.LoadResources)),
            postfix: new HarmonyMethod(typeof(Content), nameof(AfterRecipesLoaded)));
        // The language's text table is rebuilt on a language change; put the custom texts back.
        harmony.Patch(AccessTools.Method(typeof(LocObjectsDB), nameof(LocObjectsDB.ForceUpdateCurrentLanguagePairs)),
            postfix: new HarmonyMethod(typeof(Content), nameof(RegisterTexts)));
        GameEvents.GameReady += LearnStartingRecipes;
        GameEvents.GameReady += ApplyReplacementsToSavedRecipes;
        ContentModels.Install(harmony);
        ContentGuard.Install(harmony);
    }

    // An item by squashed name: the copies first, then every item asset in memory.
    internal static ItemType FindItem(string name)
    {
        string want = Items.Squash(name);
        var custom = items.FirstOrDefault(c => c.Item != null && Items.Squash(c.Spec.Name) == want);
        if (custom != null) return custom.Item;
        // By asset name first; then by the name the game shows, then by model prefab name (e.g. "Furniture_Radio_Cyber"),
        // so templates can be named however a modder found them (content dump, the game's menus, AssetStudio).
        ItemType byShown = null, byPrefab = null;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<ItemType>()))
        {
            var it = o.TryCast<ItemType>();
            if (it == null) continue;
            if (Items.Squash(it.name) == want) return it;
            if (byShown == null) { try { if (Items.Squash(it.Name) == want) byShown = it; } catch { } }
            if (byPrefab == null) { try { if (it.entityPrefab != null && Items.Squash(it.entityPrefab.name) == want) byPrefab = it; } catch { } }
        }
        return byShown ?? byPrefab;
    }

    // Builds the copies on first need (the item and recipe databases may initialize in either order).
    static void EnsureItemsBuilt()
    {
        foreach (var c in items)
            if (c.Item == null) Build(c);
    }

    static void AfterItemDatabase(ItemDatabase __instance)
    {
        itemsBuilt = true;
        if (items.Count == 0) return;
        try
        {
            EnsureItemsBuilt();
            var all = __instance._allItems?.ToList() ?? new List<ItemType>();
            var add = items.Where(c => c.Item != null && !all.Any(i => i != null && i.Pointer == c.Item.Pointer)).ToList();
            if (add.Count > 0)
            {
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ItemType>(all.Count + add.Count);
                for (int i = 0; i < all.Count; i++) arr[i] = all[i];
                for (int i = 0; i < add.Count; i++) arr[all.Count + i] = add[i].Item;
                __instance._allItems = arr;
                KitPlugin.L.LogInfo($"Content: added {add.Count} item(s): {string.Join(", ", add.Select(c => c.Spec.Name))}");
            }
            var map = __instance._guidItemTypeMap;
            if (map != null)
                foreach (var c in items.Where(c => c.Item != null))
                    map[c.Guid] = c.Item;
            RegisterTexts();
        }
        catch (Exception e) { KitPlugin.L.LogError($"Content: building items failed: {e}"); }
    }

    static void Build(CustomItem c)
    {
        var template = FindItem(c.Template);
        if (template == null)
        {
            KitPlugin.L.LogWarning($"Content: item '{c.Id}' from {c.Owner}: no item named '{c.Template}' to copy");
            return;
        }
        var item = UnityEngine.Object.Instantiate(template);
        item.name = c.Spec.Name;
        UnityEngine.Object.DontDestroyOnLoad(item);
        item.guid = new SerializableGuid { guid = c.Guid };
        item.storeAchievementId = "";
        var s = c.Spec;
        if (s.BasePrice.HasValue) item.basePrice = s.BasePrice.Value;
        if (s.MinStock.HasValue) item.minStockPerVendor = s.MinStock.Value;
        if (s.MaxStock.HasValue) item.maxStockPerVendor = s.MaxStock.Value;
        if (s.DecayDays.HasValue) item.decayTimeInDays = s.DecayDays.Value;

        // Its own text entry, so every screen that shows the name or description finds the mod's text.
        // The game reads these fields directly (ItemType.Name, tooltips); the struct is what language files load into.
        c.Texts = new LocItemPlain
        {
            guid = c.LocGuid,
            displayName = s.Name,
            shortDisplayName = s.ShortName ?? s.Name,
            description = s.Description ?? "",
        };
        c.Texts.myStruct = new TextsStruct
        {
            name = s.Name, shortName = s.ShortName ?? s.Name, description = s.Description ?? "", GUID = c.LocGuid,
        };
        item.locObjRef = new LocObjRef<LocItemPlain>(false) { locObjGuid = c.LocGuid };

        // The model prefab points back at its item (ItemEntity.data), and picking an object up gives that item. The copy
        // gets its own prefab pointing at it, kept under an inactive holder so it never shows or runs on its own.
        if (template.entityPrefab != null)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(template.entityPrefab.gameObject, PrefabHolder().transform);
                go.name = s.Name;
                var entity = go.GetComponent<ItemEntity>();
                if (entity != null)
                {
                    entity.data = item;
                    item.entityPrefab = entity;
                    if (!string.IsNullOrEmpty(s.ModelBundlePath) && !string.IsNullOrEmpty(s.ModelAsset))
                        ContentModels.ApplyModel(go, s.ModelBundlePath, s.ModelAsset, $"item '{c.Id}'");
                    // Placed copies are saved by prefab GUID: give this one its own, or it reloads as the template.
                    c.PrefabGuid = StableGuid($"{c.Owner}/{c.Id}/prefab");
                    ContentModels.Identify(go, c.PrefabGuid, $"item '{c.Id}'");
                }
                else UnityEngine.Object.Destroy(go);
            }
            catch (Exception e) { KitPlugin.L.LogWarning($"Content: item '{c.Id}': model copy failed, sharing {template.name}'s ({e.Message})"); }
        }

        if (!string.IsNullOrEmpty(s.IconPath))
        {
            var icon = LoadIcon(s.IconPath);
            if (icon != null) item.icon = icon;
            else KitPlugin.L.LogWarning($"Content: item '{c.Id}': could not load icon {s.IconPath}; keeping {template.name}'s");
        }
        c.Item = item;
    }

    // Recipes load from Resources here; copies are added to the same three maps the game fills.
    static void AfterRecipesLoaded(MealDatabase __instance)
    {
        recipesBuilt = true;
        if (recipes.Count == 0) return;
        try
        {
            EnsureItemsBuilt();
            int added = 0;
            foreach (var c in recipes)
            {
                if (c.Recipe != null && __instance._guidMealRecipeMap.ContainsKey(c.Guid)) continue;
                var def = BuildRecipe(c, __instance);
                if (def == null) continue;
                var runtime = new MealRecipe(def);
                __instance._allRecipes[def] = new IRecipe(runtime.Pointer);
                __instance._guidMealRecipeMap[c.Guid] = def;
                if (def.output?.type != null) __instance._mealRecipeMap[def.output.type] = def;
                // A dish item points at its recipe (FoodItemType.recipe), and the kitchen cooks from that link. A copied
                // dish still points at its template's recipe, so orders for it were cooked (and served) as the template.
                var dish = def.output?.type?.TryCast<FoodItemType>();
                // Only for the kit's own items: a recipe for an existing dish leaves that dish's recipe alone.
                if (dish != null && items.Any(i => i.Item != null && i.Item.Pointer == dish.Pointer)) dish.recipe = def;
                added++;
            }
            if (added > 0) KitPlugin.L.LogInfo($"Content: added {added} recipe(s): {string.Join(", ", recipes.Where(r => r.Recipe != null).Select(r => r.Id))}");
        }
        catch (Exception e) { KitPlugin.L.LogError($"Content: building recipes failed: {e}"); }
    }

    static MealRecipeDefinition BuildRecipe(CustomRecipe c, MealDatabase db)
    {
        // The template: the recipe that makes the named dish, else a recipe asset of that name.
        MealRecipeDefinition template = null;
        var dish = FindItem(c.Template);
        if (dish != null && db._mealRecipeMap.ContainsKey(dish)) template = db._mealRecipeMap[dish];
        if (template == null)
        {
            string want = Items.Squash(c.Template);
            foreach (var kv in db._guidMealRecipeMap)
                if (kv.Value != null && Items.Squash(kv.Value.name) == want) { template = kv.Value; break; }
        }
        if (template == null)
        {
            KitPlugin.L.LogWarning($"Content: recipe '{c.Id}' from {c.Owner}: no recipe for '{c.Template}' to copy");
            return null;
        }

        var def = UnityEngine.Object.Instantiate(template);
        def.name = $"{c.Owner}/{c.Id}";
        UnityEngine.Object.DontDestroyOnLoad(def);
        def.guid = new SerializableGuid { guid = c.Guid };
        var s = c.Spec;
        if (s.UnlockCost.HasValue) def.unlockCost = s.UnlockCost.Value;

        if (s.Output != null || s.OutputAmount.HasValue)
        {
            var output = s.Output != null ? FindItem(s.Output) : def.output?.type;
            if (output == null)
            {
                KitPlugin.L.LogWarning($"Content: recipe '{c.Id}': no item named '{s.Output}' to make");
                return null;
            }
            def.output = new ItemTypeAmount(output, s.OutputAmount ?? def.output?.amount ?? 1);
        }

        // InputDefinition is a struct: edit a copy and write it back into the array, or the recipe never sees it.
        var inputs = def.inputsNew;
        foreach (var swap in s.ReplaceIngredient)
        {
            var with = FindItem(swap.Value);
            int index = -1;
            for (int i = 0; inputs != null && i < inputs.Length; i++)
            {
                var item = inputs[i]?.DefaultItem;
                if (item != null && Items.Squash(item.name) == Items.Squash(swap.Key)) { index = i; break; }
            }
            if (index < 0) { KitPlugin.L.LogWarning($"Content: recipe '{c.Id}': the template has no ingredient '{swap.Key}'"); continue; }
            if (with == null) { KitPlugin.L.LogWarning($"Content: recipe '{c.Id}': no item named '{swap.Value}'"); continue; }
            var input = inputs[index];
            input.DefaultItem = with;
            inputs[index] = input;
        }
        if (inputs != null) def.inputsNew = inputs;
        c.Recipe = def;
        return def;
    }

    // The game saves each known recipe's ingredients (MealRecipeSave) and puts them back on load, over the recipe the kit
    // built. A save from before a pack's "replace" (or before 0.6.1, when replace didn't work) so keeps the old
    // ingredient: swap it in the loaded recipe too. Only slots still holding a replaced ingredient change, so the
    // player's other ingredient choices stay.
    static void ApplyReplacementsToSavedRecipes()
    {
        try
        {
            var db = MealDatabase.Instance;
            if (db == null) return;
            foreach (var c in recipes)
            {
                if (c.Recipe == null || c.Spec.ReplaceIngredient.Count == 0) continue;
                if (!db._allRecipes.ContainsKey(c.Recipe)) continue;
                var runtime = db._allRecipes[c.Recipe]?.TryCast<MealRecipe>();
                var have = runtime?.inputIngredients;
                var want = c.Recipe.inputsNew;
                if (have == null || want == null) continue;
                int changed = 0;
                for (int i = 0; i < have.Length && i < want.Length; i++)
                {
                    var slot = have[i];
                    var old = slot?.DefaultItem;
                    var now = want[i]?.DefaultItem;
                    if (old == null || now == null || old.Pointer == now.Pointer) continue;
                    if (!c.Spec.ReplaceIngredient.Keys.Any(k => Items.Squash(k) == Items.Squash(old.name))) continue;
                    slot.DefaultItem = now;
                    have[i] = slot;
                    changed++;
                }
                if (changed == 0) continue;
                runtime.inputIngredients = have;
                KitPlugin.L.LogInfo($"Content: recipe '{c.Id}': {changed} replaced ingredient(s) applied to the saved recipe");
            }
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Content: saved recipes: {e.Message}"); }
    }

    // Recipes marked KnownFromStart become known in each game (the game saves known recipes, so once is enough).
    static void LearnStartingRecipes()
    {
        try
        {
            var db = MealDatabase.Instance;
            if (db == null) return;
            foreach (var c in recipes.Where(r => r.Spec.KnownFromStart && r.Recipe != null))
                if (!db.knownRecipes.Contains(c.Recipe))
                {
                    db.DiscoverRecipe(c.Recipe);
                    KitPlugin.L.LogInfo($"Content: recipe '{c.Id}' known");
                }
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Content: starting recipes: {e.Message}"); }
    }

    // ---------- helpers ----------

    // Adds (or re-adds) each custom item's text to the current language's table.
    static void RegisterTexts()
    {
        try
        {
            var pairs = Nivalis.Utility.ScriptableObjectSingleton<LocObjectsDB>.Instance?.locObjGuidPairs;
            if (pairs == null) return;
            foreach (var c in items)
                if (c.Texts != null) pairs[c.LocGuid] = new Wrapper<LocObjPlain>(c.Texts);
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Content: item names: {e.Message}"); }
    }

    static Sprite LoadIcon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) return null;
            tex.name = Path.GetFileNameWithoutExtension(path);
            UnityEngine.Object.DontDestroyOnLoad(tex);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            UnityEngine.Object.DontDestroyOnLoad(sprite);
            return sprite;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Content: icon {path}: {e.Message}"); return null; }
    }

    static GameObject prefabHolder;
    static GameObject PrefabHolder()
    {
        if (prefabHolder != null) return prefabHolder;
        prefabHolder = new GameObject("Kit_ContentPrefabs");
        prefabHolder.SetActive(false);   // children keep activeSelf, so copies the game spawns from them are active
        UnityEngine.Object.DontDestroyOnLoad(prefabHolder);
        return prefabHolder;
    }

    static void Require(string owner, string id, string template)
    {
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(template))
            throw new ArgumentException("owner, id and template are required");
    }

    // The same key always gives the same GUID, so saves find the item or recipe again.
    static string StableGuid(string key)
    {
        using var md5 = MD5.Create();
        return new System.Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("NivalisModKit.Content/" + key))).ToString();
    }
}
