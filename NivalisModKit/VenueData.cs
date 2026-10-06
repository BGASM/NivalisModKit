using System;
using System.Collections.Generic;
using System.Linq;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.Player;

namespace NivalisModKit;

/// <summary>A dish on a venue's menu: see <see cref="Venues.MenuOf"/>.</summary>
[Experimental("New in 0.5.")]
public sealed class MenuEntry
{
    /// <summary>The recipe as the venue makes it (with the ingredients chosen for it).</summary>
    public IRecipe Recipe { get; internal set; }
    /// <summary>The dish it makes.</summary>
    public ItemType Dish { get; internal set; }
    /// <summary>The menu price, in hundredths.</summary>
    public int Price { get; internal set; }
    /// <summary>The ingredients per plate, as chosen on the menu (one entry per slot; the same item may appear twice).</summary>
    public IReadOnlyList<(ItemType Item, int Amount)> Ingredients { get; internal set; }
}

/// <summary>A customer review: see <see cref="Venues.ReviewsOf"/>.</summary>
[Experimental("New in 0.5.")]
public sealed class ReviewInfo
{
    /// <summary>The score the customer gave.</summary>
    public int Score { get; internal set; }
    /// <summary>The main dish they had, or null.</summary>
    public ItemType Dish { get; internal set; }
    /// <summary>Who wrote it.</summary>
    public string Reviewer { get; internal set; }
    /// <summary>When: total game seconds (see <see cref="Day"/>, <see cref="Hour"/>).</summary>
    public int GameSeconds { get; internal set; }
    /// <summary>The game day it was written.</summary>
    public int Day => GameSeconds / 86400;
    /// <summary>The hour it was written.</summary>
    public int Hour => GameSeconds % 86400 / 3600;
    /// <summary>Service quality the customer felt.</summary>
    public float ServiceQuality { get; internal set; }
    /// <summary>How clean the venue felt.</summary>
    public float Cleanliness { get; internal set; }
    /// <summary>How comfortable the furniture felt.</summary>
    public float Comfort { get; internal set; }
    /// <summary>Discomfort from rain.</summary>
    public float RainDiscomfort { get; internal set; }
    /// <summary>Discomfort from snow.</summary>
    public float SnowDiscomfort { get; internal set; }
    /// <summary>Their order was taken.</summary>
    public bool OrderTaken { get; internal set; }
    /// <summary>Everything they ordered arrived.</summary>
    public bool AllFoodDelivered { get; internal set; }
    /// <summary>How they reacted to their interaction (the game's CustomerReaction, as text).</summary>
    public string Reaction { get; internal set; }
}

/// <summary>A customer order waiting to be filled: see <see cref="Venues.OrdersOf"/>.</summary>
[Experimental("New in 0.5.")]
public sealed class OrderInfo
{
    /// <summary>The order's id (stable while it's active).</summary>
    public string Id { get; internal set; }
    /// <summary>The dishes ordered.</summary>
    public IReadOnlyList<ItemType> Dishes { get; internal set; }
    /// <summary>Total price, in hundredths.</summary>
    public int Price { get; internal set; }
    /// <summary>Dishes prepared so far.</summary>
    public int Prepared { get; internal set; }
    /// <summary>Dishes delivered to the table so far.</summary>
    public int Delivered { get; internal set; }
    /// <summary>Service quality so far.</summary>
    public float ServiceQuality { get; internal set; }
}

/// <summary>A member of staff: see <see cref="Venues.StaffOf"/>.</summary>
[Experimental("New in 0.5.")]
public sealed class StaffMember
{
    /// <summary>The person.</summary>
    public Person Person { get; internal set; }
    /// <summary>Their name.</summary>
    public string Name { get; internal set; }
    /// <summary>Hourly wage, in hundredths, paid each hour of their shift.</summary>
    public int Wage { get; internal set; }
    /// <summary>Shift start, hours (0-24).</summary>
    public float ShiftStart { get; internal set; }
    /// <summary>Shift end, hours; past 24 when the shift runs past midnight.</summary>
    public float ShiftEnd { get; internal set; }
    /// <summary>Their roles: Serving, Cooking, Cleaning, Managing (the game's VenueTasks, as text).</summary>
    public string Roles { get; internal set; }
    /// <summary>Hours still to work (and be paid for) today, from now.</summary>
    public int HoursLeftToday { get; internal set; }
}

/// <summary>A venue receipt: see <see cref="Venues.ReceiptsOf"/>.</summary>
[Experimental("New in 0.5.")]
public sealed class ReceiptInfo
{
    /// <summary>What it's for: Restaurant (a sale), Shop (buying or selling at vendors), Staff (wages), Rent, BoatFuel.</summary>
    public string Type { get; internal set; }
    /// <summary>The game day (as the game counts days in play).</summary>
    public int Day { get; internal set; }
    /// <summary>When, in seconds into that day.</summary>
    public int DaySeconds { get; internal set; }
    /// <summary>The amount, in hundredths: positive coming in, negative going out.</summary>
    public int Amount { get; internal set; }
    /// <summary>How many transactions it covers (the game merges similar ones).</summary>
    public int Count { get; internal set; }
    /// <summary>Sales: the dish sold.</summary>
    public ItemType Dish { get; internal set; }
    /// <summary>Rent: the property it's for (a venue, an apartment).</summary>
    public Nivalis.GhostSystem.CustomerLoop.BaseProperty Property { get; internal set; }
}

public static partial class Venues
{
    /// <summary>
    /// The venue's receipts, what its end-of-day screen reads: sales (with the dish), ingredient purchases (totals only),
    /// wages, rent. <paramref name="day"/> limits it to one day (the game's day in play, as <see cref="GameTime.Day"/>).
    /// </summary>
    [Experimental("New in 0.5.")]
    public static List<ReceiptInfo> ReceiptsOf(VenueAreaGhost area, int? day = null)
    {
        var list = new List<ReceiptInfo>();
        try
        {
            ReadReceipts(area?.m_receipts, day, list);
        }
        catch (Exception e) { KitPlugin.L.LogError($"Venues.ReceiptsOf: {e.Message}"); }
        return list;
    }

    internal static void ReadReceipts(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase source, int? day, List<ReceiptInfo> list)
    {
        if (source == null) return;
        var items = source.TryCast<ReceiptList>()?._backingList ?? source.TryCast<BaseReceiptList>()?._backingList;
        if (items == null) return;
        foreach (var r in items)
        {
            if (r == null) continue;
            int d = r.Time.GameplayGameDay;
            if (day is { } want && d != want) continue;
            list.Add(new ReceiptInfo
            {
                Type = r.Type.ToString(), Day = d, DaySeconds = r.Time.TotalDaySeconds, Amount = r.Amount,
                Count = r.Count, Dish = r.TryCast<RestaurantReceipt>()?.Meal, Property = r.TryCast<RentReceipt>()?.Rentable,
            });
        }
    }

    /// <summary>The venue's menu: each dish, its price, and the ingredients chosen for it. Empty if none.</summary>
    [Experimental("New in 0.5.")]
    public static List<MenuEntry> MenuOf(VenueAreaGhost area)
    {
        var list = new List<MenuEntry>();
        try
        {
            var items = area?._menu?._backingList;
            if (items == null) return list;
            foreach (var m in items)
            {
                if (m == null) continue;
                // The venue's recipe (with the ingredients chosen on the menu), read through the IRecipe interface; the
                // definition's defaults if the venue has none.
                var recipe = m._recipe;
                var ingredients = new List<(ItemType, int)>();
                MealRecipeDefinition.InputDefinition[] inputs = null;
                try { inputs = recipe?.Inputs; } catch { }
                if (inputs == null || inputs.Length == 0) { try { inputs = m.recipe?.inputsNew; } catch { } }
                if (inputs != null)
                    foreach (var input in inputs)
                        if (input.DefaultItem != null) ingredients.Add((input.DefaultItem, 1));
                ItemType dish = null;
                try { dish = recipe?.Output?.type ?? m.recipe?.output?.type; } catch { }
                list.Add(new MenuEntry { Recipe = recipe, Dish = dish, Price = m.price, Ingredients = ingredients });
            }
        }
        catch (Exception e) { KitPlugin.L.LogError($"Venues.MenuOf: {e.Message}"); }
        return list;
    }

    /// <summary>The venue's reviews, oldest first.</summary>
    [Experimental("New in 0.5.")]
    public static List<ReviewInfo> ReviewsOf(VenueAreaGhost area)
    {
        var list = new List<ReviewInfo>();
        try
        {
            var reviews = area?._reviews?._backingList;
            if (reviews == null) return list;
            foreach (var r in reviews)
            {
                if (r == null) continue;
                string by = null, reaction = null;
                int when = 0;
                try { by = r.ReviewBy?.DisplayedName ?? r.ReviewBy?.Name; } catch { }
                try { reaction = r.InteractionOutcome.ToString(); } catch { }
                try { when = r.ReviewTime.TotalGameSeconds; } catch { }
                list.Add(new ReviewInfo
                {
                    Score = r.Score, Dish = r.MainItem, Reviewer = by, GameSeconds = when,
                    ServiceQuality = r.ServiceQuality, Cleanliness = r.RestaurantCleanliness, Comfort = r.FurnitureComfort,
                    RainDiscomfort = r.RainDiscomfort, SnowDiscomfort = r.SnowDiscomfort,
                    OrderTaken = r.WasOrderTaken, AllFoodDelivered = r.AllFoodDelivered, Reaction = reaction,
                });
            }
        }
        catch (Exception e) { KitPlugin.L.LogError($"Venues.ReviewsOf: {e.Message}"); }
        return list;
    }

    /// <summary>The venue's popularity with each customer group (the game's DemographicGroup names).</summary>
    [Experimental("New in 0.5.")]
    public static Dictionary<string, float> PopularityOf(VenueAreaGhost area)
    {
        var map = new Dictionary<string, float>();
        try
        {
            var pop = area?.Popularity;
            if (pop == null) return map;
            foreach (var kv in pop)
                if (kv.Key != null) map[((UnityEngine.Object)kv.Key).name] = kv.Value;
        }
        catch (Exception e) { KitPlugin.L.LogError($"Venues.PopularityOf: {e.Message}"); }
        return map;
    }

    /// <summary>Customer orders taken and not yet completed. When an order completes it leaves this list.</summary>
    [Experimental("New in 0.5.")]
    public static List<OrderInfo> OrdersOf(VenueAreaGhost area)
    {
        var list = new List<OrderInfo>();
        try
        {
            var active = area?.Orders?._active;
            if (active == null) return list;
            foreach (var o in active)
            {
                if (o == null || !o.IsValid) continue;
                var dishes = new List<ItemType>();
                if (o.OrderedMeals != null) foreach (var d in o.OrderedMeals) if (d != null) dishes.Add(d);
                int price = 0;
                if (o.OrderedForPrice != null) foreach (var kv in o.OrderedForPrice) price += kv.Value;
                list.Add(new OrderInfo
                {
                    Id = o.Id, Dishes = dishes, Price = price,
                    Prepared = o.PreparedMealTypes?.Count ?? 0, Delivered = o.DeliveredMeals?.Count ?? 0,
                    ServiceQuality = o.ServiceQuality,
                });
            }
        }
        catch (Exception e) { KitPlugin.L.LogError($"Venues.OrdersOf: {e.Message}"); }
        return list;
    }

    /// <summary>The venue's staff: wage, shift, roles, and hours still to be paid today.</summary>
    [Experimental("New in 0.5.")]
    public static List<StaffMember> StaffOf(VenueAreaGhost area)
    {
        var list = new List<StaffMember>();
        try
        {
            var staff = area?.staff?.backingList;
            if (staff == null) return list;
            // The game's working day runs from 8:00; hours before that belong to the previous day.
            float now = GameTime.Hour + GameTime.Minute / 60f;
            if (now < 8f) now += 24f;
            foreach (var p in staff)
            {
                var data = p?.RuntimeData;
                if (data == null) continue;
                float start = data.WorkingHours.x, end = data.WorkingHours.y;
                int left = 0;
                for (int h = (int)Math.Ceiling(now); h < end; h++) if (h >= start) left++;
                string name = null;
                try { name = p.DisplayedName ?? p.Name; } catch { }
                list.Add(new StaffMember
                {
                    Person = p, Name = name, Wage = data.Wage, ShiftStart = start % 24f, ShiftEnd = end,
                    Roles = data.Tasks.ToString(), HoursLeftToday = left,
                });
            }
        }
        catch (Exception e) { KitPlugin.L.LogError($"Venues.StaffOf: {e.Message}"); }
        return list;
    }

    /// <summary>Everything in the venue's storage (fridges and cupboards), by item, with counts.</summary>
    [Experimental("New in 0.5.")]
    public static Dictionary<ItemType, int> StockOf(VenueAreaGhost area)
    {
        var map = new Dictionary<ItemType, int>();
        try
        {
            var inv = area?.JointInventory;
            if (inv == null) return map;
            CountInto(inv.NormalInventory, map);
            CountInto(inv.RefridgeratedInventory, map);
        }
        catch (Exception e) { KitPlugin.L.LogError($"Venues.StockOf: {e.Message}"); }
        return map;
    }

    // Adds a container's items to the counts (by item).
    internal static void CountInto(ItemContainer container, Dictionary<ItemType, int> map)
    {
        var stacks = container?._items;
        if (stacks == null) return;
        foreach (var stack in stacks)
        {
            var type = stack?._type;
            if (type == null) continue;
            int n = stack._instanceData?.Count ?? 0;
            var key = map.Keys.FirstOrDefault(k => k.Pointer == type.Pointer) ?? type;
            map[key] = (map.TryGetValue(key, out var have) ? have : 0) + n;
        }
    }

    /// <summary>Meals the venue has served (the game's running count).</summary>
    [Experimental("New in 0.5.")]
    public static int MealsServedOf(VenueAreaGhost area)
    {
        try { return area?.mealsServed ?? 0; } catch { return 0; }
    }
}

public static partial class Economy
{
    /// <summary>What the player is carrying, by item, with counts.</summary>
    [Experimental("New in 0.5.")]
    public static Dictionary<ItemType, int> PlayerStock()
    {
        var map = new Dictionary<ItemType, int>();
        try
        {
            if (Singleton<PlayerManager>.InstanceExist(out var pm)) Venues.CountInto(pm.LocalPlayer?.Inventory?.Items, map);
        }
        catch (Exception e) { KitPlugin.L.LogError($"Economy.PlayerStock: {e.Message}"); }
        return map;
    }

    /// <summary>
    /// The player's own receipts (not a venue's): what they bought and sold at vendors by hand, rent they paid, and so
    /// on. <paramref name="day"/> limits it to one day (as <see cref="GameTime.Day"/>).
    /// </summary>
    [Experimental("New in 0.5.")]
    public static List<ReceiptInfo> PlayerReceipts(int? day = null)
    {
        var list = new List<ReceiptInfo>();
        try
        {
            if (Singleton<PlayerManager>.InstanceExist(out var pm))
                Venues.ReadReceipts(pm.LocalPlayer?.personalReceipts, day, list);
        }
        catch (Exception e) { KitPlugin.L.LogError($"Economy.PlayerReceipts: {e.Message}"); }
        return list;
    }
}
