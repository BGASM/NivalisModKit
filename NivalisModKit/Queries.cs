using System;
using System.Collections.Generic;
using System.Linq;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.Economy;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;

namespace NivalisModKit;

// Read-only lookups over the game's managers. Each returns a fresh managed list (safe to keep and
// iterate), or empty/null when the game isn't in gameplay. Call from the main thread: plugin Load,
// kit events, or Unity callbacks.

/// <summary>The game clock. Values are 0 before gameplay starts.</summary>
public static class GameTime
{
    /// <summary>The game day (TimeOfDayManager.GameplayGameDay). The day turns over at 08:00.</summary>
    public static int Day => Safe(() => TimeOfDayManager.GameplayGameDay);

    /// <summary>The clock hour, 0 to 23.</summary>
    public static int Hour => Safe(() => TimeOfDayManager.ClockHour);

    /// <summary>The clock minute, 0 to 59.</summary>
    public static int Minute => Safe(() => TimeOfDayManager.ClockMinute);

    /// <summary>Total game hours since the start of the save, with fractions.</summary>
    public static float TotalHours => Safe(() => TimeOfDayManager.TotalHours);

    /// <summary>The day of the week.</summary>
    public static Nivalis.DayOfWeek DayOfWeek => Safe(() => TimeOfDayManager.CurrentDayOfWeek);

    static T Safe<T>(Func<T> f)
    {
        try { return f(); } catch { return default; }
    }
}

/// <summary>Venues: restaurants, bars and shops the city's managers run, the player's included.</summary>
public static class Venues
{
    /// <summary>Every venue in the city.</summary>
    public static List<VenueAreaGhost> All
    {
        get
        {
            var list = new List<VenueAreaGhost>();
            try
            {
                if (!Singleton<VenueManager>.InstanceExist(out var vm) || vm._venueGhosts == null) return list;
                foreach (var area in vm._venueGhosts)
                    if (area != null) list.Add(area);
            }
            catch (Exception e) { KitPlugin.L.LogError($"Venues.All: {e.Message}"); }
            return list;
        }
    }

    /// <summary>The player's venues.</summary>
    public static List<VenueAreaGhost> PlayerOwned => All.Where(IsPlayerOwned).ToList();

    /// <summary>Venues in a district.</summary>
    public static List<VenueAreaGhost> InDistrict(WorldLocation district) =>
        district == null ? new List<VenueAreaGhost>() : All.Where(a => DistrictOf(a)?.Pointer == district.Pointer).ToList();

    /// <summary>The venue's district, or null.</summary>
    public static WorldLocation DistrictOf(VenueAreaGhost area)
    {
        try { return area?.Venue?.Location; } catch { return null; }
    }

    /// <summary>The venue's asset name, e.g. "Venue_NoodleBar".</summary>
    public static string NameOf(VenueAreaGhost area)
    {
        try
        {
            string s = area?.Venue?.ToString();
            if (s == null) return null;
            int i = s.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : s;
        }
        catch { return null; }
    }

    /// <summary>How many of an item the venue has in storage (fridge plus cupboard).</summary>
    public static int Stock(VenueAreaGhost area, ItemType item)
    {
        try { return item == null ? 0 : area?.JointInventory?.GetItemCount(item) ?? 0; } catch { return 0; }
    }

    /// <summary>
    /// The venue's storage: items stored and capacity, for normal and refrigerated storage. Capacity comes
    /// from its storage furniture (and, since game patch 2, decorations with storage); null if unlimited.
    /// </summary>
    public static VenueStorage StorageOf(VenueAreaGhost area)
    {
        try
        {
            var inv = area?.JointInventory;
            if (inv == null) return null;
            return new VenueStorage(Count(inv.NormalInventory), Capacity(inv.NormalInventory),
                                    Count(inv.RefridgeratedInventory), Capacity(inv.RefridgeratedInventory));
        }
        catch { return null; }
    }

    static int Count(ItemContainer c)
    {
        try { return c?.ItemCount ?? 0; } catch { return 0; }
    }

    static int? Capacity(ItemContainer c)
    {
        try
        {
            var max = c?._restriction?.MaxItems;
            return max != null && max.HasValue ? max.Value : null;
        }
        catch { return null; }
    }

    static bool IsPlayerOwned(VenueAreaGhost area)
    {
        try { return area.PlayerOwned; } catch { return false; }
    }
}

/// <summary>Vendors, prices, stock, and the player's money.</summary>
public static class Economy
{
    /// <summary>A vendor's name as the game shows it, e.g. "Greengrocer Calypso Island", or null.</summary>
    public static string NameOf(Vendor vendor)
    {
        try
        {
            if (vendor == null) return null;
            string s = vendor.ToString();
            int i = s.IndexOf(" (", StringComparison.Ordinal);   // Unity appends " (Nivalis.Economy.Vendor)"
            return i > 0 ? s.Substring(0, i) : s;
        }
        catch { return null; }
    }

    /// <summary>The player's money, or null outside gameplay.</summary>
    public static int? PlayerMoney
    {
        get
        {
            try
            {
                if (!Singleton<PlayerManager>.InstanceExist(out var pm)) return null;
                return pm.LocalPlayer?.Inventory?.Money;
            }
            catch { return null; }
        }
    }

    /// <summary>Every vendor, including ones in tiers the player hasn't unlocked.</summary>
    public static List<Vendor> Vendors
    {
        get
        {
            var list = new List<Vendor>();
            try
            {
                if (!Singleton<EconomyManager>.InstanceExist(out var em) || em.Vendors == null) return list;
                foreach (var v in em.Vendors)
                    if (v != null) list.Add(v);
            }
            catch (Exception e) { KitPlugin.L.LogError($"Economy.Vendors: {e.Message}"); }
            return list;
        }
    }

    /// <summary>Vendors that sell an item.</summary>
    public static List<Vendor> VendorsFor(ItemType item) =>
        item == null ? new List<Vendor>() : Vendors.Where(v => Offers(v, item)).ToList();

    /// <summary>True if the vendor sells the item.</summary>
    public static bool Offers(Vendor vendor, ItemType item)
    {
        try { return vendor != null && item != null && vendor.IsOffered(item); } catch { return false; }
    }

    /// <summary>
    /// What the vendor charges per item, before any venue barter discount, or null if unknown.
    /// Prices change every game day and when a save loads.
    /// </summary>
    public static int? Price(Vendor vendor, ItemType item, FoodFreshness freshness = FoodFreshness.Fresh)
    {
        try { return vendor?.GetItemBuyCost(item, freshness); } catch { return null; }
    }

    /// <summary>What the vendor pays per item when the player sells, or null if unknown.</summary>
    public static int? SellPrice(Vendor vendor, ItemType item, FoodFreshness freshness = FoodFreshness.Fresh)
    {
        try { return vendor?.GetItemSellCost(item, freshness); } catch { return null; }
    }

    /// <summary>The vendor's stock of the item, or 0.</summary>
    public static int Stock(Vendor vendor, ItemType item)
    {
        try { return vendor?.Container?.GetItemCount(item) ?? 0; } catch { return 0; }
    }

    /// <summary>The vendor's district, or null.</summary>
    public static WorldLocation DistrictOf(Vendor vendor)
    {
        try { return vendor?.Location?.Location; } catch { return null; }
    }

    /// <summary>True if the vendor's tier is unlocked for the player.</summary>
    public static bool IsUnlocked(Vendor vendor)
    {
        try
        {
            return vendor != null && Singleton<EconomyManager>.InstanceExist(out var em) &&
                   em.IsVendorTierUnlocked(vendor.Tier);
        }
        catch { return false; }
    }
}

/// <summary>Item types: ingredients, dishes, equipment, furniture.</summary>
public static class Items
{
    /// <summary>Every item type in the game.</summary>
    public static List<ItemType> All
    {
        get
        {
            var list = new List<ItemType>();
            try
            {
                if (!Singleton<ItemDatabase>.InstanceExist(out var db) || db._allItems == null) return list;
                foreach (var item in db._allItems)
                    if (item != null) list.Add(item);
            }
            catch (Exception e) { KitPlugin.L.LogError($"Items.All: {e.Message}"); }
            return list;
        }
    }

    /// <summary>
    /// Finds an item by its asset name, ignoring case, spaces and underscores ("chicken",
    /// "Drinks Machine"). Null if none match.
    /// </summary>
    public static ItemType ByName(string name)
    {
        string want = Squash(name);
        return want == "" ? null : All.FirstOrDefault(i => Squash(NameOf(i)) == want);
    }

    /// <summary>Finds an item by its id (ItemType.Guid). Null if unknown.</summary>
    public static ItemType ById(string guid)
    {
        try
        {
            if (string.IsNullOrEmpty(guid) || !Singleton<ItemDatabase>.InstanceExist(out var db)) return null;
            var map = db._guidItemTypeMap;
            return map != null && map.ContainsKey(guid) ? map[guid] : null;
        }
        catch { return null; }
    }

    /// <summary>The item's asset name, e.g. "Chicken". The game shows the same name.</summary>
    public static string NameOf(ItemType item)
    {
        try { return item?.name; } catch { return null; }
    }

    internal static string Squash(string s) =>
        s == null ? "" : s.Replace(" ", "").Replace("_", "").ToLowerInvariant();
}

/// <summary>Meal recipes.</summary>
public static class Recipes
{
    /// <summary>Every recipe in the game.</summary>
    public static List<IRecipe> All
    {
        get
        {
            var list = new List<IRecipe>();
            try
            {
                var all = MealDatabase.Instance?._allRecipes;
                if (all == null) return list;
                foreach (var kv in all)
                    if (kv.Value != null) list.Add(kv.Value);
            }
            catch (Exception e) { KitPlugin.L.LogError($"Recipes.All: {e.Message}"); }
            return list;
        }
    }

    /// <summary>Recipes the player has discovered.</summary>
    public static List<IRecipe> Known
    {
        get
        {
            var list = new List<IRecipe>();
            try
            {
                var db = MealDatabase.Instance;
                if (db?.knownRecipes == null || db._allRecipes == null) return list;
                foreach (var def in db.knownRecipes)
                    if (def != null && db._allRecipes.ContainsKey(def)) list.Add(db._allRecipes[def]);
            }
            catch (Exception e) { KitPlugin.L.LogError($"Recipes.Known: {e.Message}"); }
            return list;
        }
    }

    /// <summary>The recipe that makes a dish, or null.</summary>
    public static IRecipe ForDish(ItemType dish)
    {
        try
        {
            var db = MealDatabase.Instance;
            if (dish == null || db?._mealRecipeMap == null || db._allRecipes == null) return null;
            if (!db._mealRecipeMap.ContainsKey(dish)) return null;
            var def = db._mealRecipeMap[dish];
            return def != null && db._allRecipes.ContainsKey(def) ? db._allRecipes[def] : null;
        }
        catch { return null; }
    }

    /// <summary>The ingredients one serving needs: the default item for each input, and how many.</summary>
    public static List<(ItemType Item, int Amount)> InputsOf(IRecipe recipe)
    {
        var list = new List<(ItemType, int)>();
        try
        {
            var inputs = recipe?.Inputs;
            if (inputs == null) return list;
            foreach (var input in inputs)
                if (input?.DefaultItem != null) list.Add((input.DefaultItem, input.Amount));
        }
        catch (Exception e) { KitPlugin.L.LogError($"Recipes.InputsOf: {e.Message}"); }
        return list;
    }

    /// <summary>The dish a recipe makes, or null.</summary>
    public static ItemType OutputOf(IRecipe recipe)
    {
        try { return recipe?.Output.type; } catch { return null; }
    }
}

/// <summary>Quests the player has.</summary>
public static class Quests
{
    /// <summary>Active quests.</summary>
    public static List<RuntimeQuest> Active => Collect(qm => qm._activeQuests);

    /// <summary>Completed quests.</summary>
    public static List<RuntimeQuest> Completed => Collect(qm => qm._completedQuests);

    /// <summary>The pinned quest, or null.</summary>
    public static RuntimeQuest Pinned => Active.FirstOrDefault(q =>
    {
        try { return q.Pinned; } catch { return false; }
    });

    static List<RuntimeQuest> Collect(Func<QuestManager, Il2CppSystem.Collections.Generic.Dictionary<string, RuntimeQuest>> source)
    {
        var list = new List<RuntimeQuest>();
        try
        {
            if (!Singleton<QuestManager>.InstanceExist(out var qm)) return list;
            var d = source(qm);
            if (d == null) return list;
            foreach (var kv in d)
                if (kv.Value != null) list.Add(kv.Value);
        }
        catch (Exception e) { KitPlugin.L.LogError($"Quests: {e.Message}"); }
        return list;
    }
}

/// <summary>Curfew and CorpSec security state.</summary>
public static class Security
{
    static CurfewManager Manager => Singleton<CurfewManager>.InstanceExist(out var cm) ? cm : null;

    /// <summary>True during curfew (02:00 to 08:00).</summary>
    public static bool IsCurfew
    {
        get { try { return CurfewManager.IsCurfewInEffect; } catch { return false; } }
    }

    /// <summary>True when cameras and drones are active (curfew security on).</summary>
    public static bool IsSecurityActive
    {
        get { try { return Manager?.IsCurfewSecurityEnabled ?? false; } catch { return false; } }
    }

    /// <summary>Awareness of the player, 0 to 1. At 1 the player is caught.</summary>
    public static float Awareness
    {
        get { try { return Manager?.Awarness ?? 0f; } catch { return 0f; } }
    }

    /// <summary>
    /// True while the player is caught (awareness at 1). Furniture stays locked against theft
    /// until awareness resets.
    /// </summary>
    public static bool IsCaught
    {
        get { try { return Manager?.IsPlayerCaught ?? false; } catch { return false; } }
    }

    /// <summary>Security level of the player's current district.</summary>
    public static int Level
    {
        get { try { return Manager?.GetSecurityLevel() ?? 0; } catch { return 0; } }
    }

    /// <summary>Security level of a district.</summary>
    public static int LevelOf(WorldLocation district)
    {
        try { return district == null ? 0 : Manager?.GetSecurityLevel(district) ?? 0; } catch { return 0; }
    }
}

/// <summary>A venue's storage use (see <see cref="Venues.StorageOf"/>).</summary>
public sealed class VenueStorage
{
    /// <summary>Items in normal storage.</summary>
    public int Normal { get; }

    /// <summary>Normal storage capacity, or null if unlimited.</summary>
    public int? NormalCapacity { get; }

    /// <summary>Items in refrigerated storage.</summary>
    public int Refrigerated { get; }

    /// <summary>Refrigerated storage capacity, or null if unlimited.</summary>
    public int? RefrigeratedCapacity { get; }

    internal VenueStorage(int normal, int? normalCapacity, int refrigerated, int? refrigeratedCapacity)
    {
        Normal = normal;
        NormalCapacity = normalCapacity;
        Refrigerated = refrigerated;
        RefrigeratedCapacity = refrigeratedCapacity;
    }
}
