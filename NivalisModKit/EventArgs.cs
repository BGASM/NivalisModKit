using System.Collections.Generic;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.Economy;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.VenueSupplyQuest;

namespace NivalisModKit;

// Event arguments are classes, so fields can be added later without breaking mods.

/// <summary>Arguments for <see cref="GameEvents.BuyIngredientsStarting"/> and <see cref="GameEvents.BuyIngredientsFinished"/>.</summary>
public sealed class BuyIngredientsArgs
{
    /// <summary>The venue area doing the buying.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The recipe being bought for.</summary>
    public IRecipe Recipe { get; }

    /// <summary>
    /// Whether anything was bought, as the game (and any other mod) reported it.
    /// Always false for BuyIngredientsStarting.
    /// </summary>
    public bool Bought { get; }

    internal BuyIngredientsArgs(VenueAreaGhost area, IRecipe recipe, bool bought)
    {
        Area = area;
        Recipe = recipe;
        Bought = bought;
    }
}

/// <summary>Arguments for <see cref="GameEvents.VenueHour"/>.</summary>
public sealed class VenueHourArgs
{
    /// <summary>The venue area that updated.</summary>
    public VenueAreaGhost Area { get; }

    internal VenueHourArgs(VenueAreaGhost area) => Area = area;
}

/// <summary>Arguments for <see cref="GameEvents.IngredientsPurchased"/>.</summary>
public sealed class IngredientsPurchasedArgs
{
    /// <summary>The venue area that bought.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The ingredient bought.</summary>
    public ItemType Item { get; }

    /// <summary>Number of items bought.</summary>
    public int Count { get; }

    /// <summary>Total price paid.</summary>
    public int TotalPrice { get; }

    internal IngredientsPurchasedArgs(VenueAreaGhost area, ItemType item, int count, int totalPrice)
    {
        Area = area;
        Item = item;
        Count = count;
        TotalPrice = totalPrice;
    }
}

/// <summary>Arguments for <see cref="GameEvents.EquipmentPurchased"/>.</summary>
public sealed class EquipmentPurchasedArgs
{
    /// <summary>The venue area that bought.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>What was bought, e.g. Drinks_Machine.</summary>
    public ItemType Item { get; }

    /// <summary>Number of items bought.</summary>
    public int Count { get; }

    /// <summary>Total price paid.</summary>
    public int TotalPrice { get; }

    internal EquipmentPurchasedArgs(VenueAreaGhost area, ItemType item, int count, int totalPrice)
    {
        Area = area;
        Item = item;
        Count = count;
        TotalPrice = totalPrice;
    }
}

/// <summary>Arguments for <see cref="GameEvents.DayStarted"/>.</summary>
public sealed class DayStartedArgs
{
    /// <summary>The game day that started (TimeOfDayManager.GameplayGameDay).</summary>
    public int Day { get; }

    /// <summary>Its day of the week.</summary>
    public Nivalis.DayOfWeek DayOfWeek { get; }

    internal DayStartedArgs(int day, Nivalis.DayOfWeek dayOfWeek)
    {
        Day = day;
        DayOfWeek = dayOfWeek;
    }
}

/// <summary>Arguments for <see cref="GameEvents.HourStarted"/>.</summary>
public sealed class HourStartedArgs
{
    /// <summary>The game day (TimeOfDayManager.GameplayGameDay).</summary>
    public int Day { get; }

    /// <summary>The clock hour, 0 to 23.</summary>
    public int Hour { get; }

    internal HourStartedArgs(int day, int hour)
    {
        Day = day;
        Hour = hour;
    }
}

/// <summary>Arguments for <see cref="GameEvents.GameLoaded"/>.</summary>
public sealed class GameLoadedArgs
{
    /// <summary>The save's name, e.g. save_2026_09_30_12_15_47. Null if unknown.</summary>
    public string SaveName { get; }

    /// <summary>The district the player starts in, or null if unknown.</summary>
    public WorldLocation District { get; }

    internal GameLoadedArgs(string saveName, WorldLocation district)
    {
        SaveName = saveName;
        District = district;
    }
}

/// <summary>Arguments for <see cref="GameEvents.GameSaved"/>.</summary>
public sealed class GameSavedArgs
{
    /// <summary>The save's name.</summary>
    public string SaveName { get; }

    /// <summary>True for an autosave.</summary>
    public bool IsAutoSave { get; }

    internal GameSavedArgs(string saveName, bool isAutoSave)
    {
        SaveName = saveName;
        IsAutoSave = isAutoSave;
    }
}

/// <summary>Arguments for <see cref="GameEvents.DistrictEntered"/>.</summary>
public sealed class DistrictEnteredArgs
{
    /// <summary>The district arrived in. <see cref="World.NameOf"/> gives its name.</summary>
    public WorldLocation District { get; }

    internal DistrictEnteredArgs(WorldLocation district) => District = district;
}

/// <summary>Arguments for <see cref="GameEvents.DishCooked"/>.</summary>
public sealed class DishCookedArgs
{
    /// <summary>The venue the dish was ordered at, or null when cooked without an order.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The recipe cooked.</summary>
    public IRecipe Recipe { get; }

    /// <summary>The finished meal. <c>Meal.Type</c> is the dish, <c>Meal.IsFailed</c> a failed cook.</summary>
    public MealGhost Meal { get; }

    internal DishCookedArgs(VenueAreaGhost area, IRecipe recipe, MealGhost meal)
    {
        Area = area;
        Recipe = recipe;
        Meal = meal;
    }
}

/// <summary>Arguments for <see cref="GameEvents.SaleMade"/>.</summary>
public sealed class SaleMadeArgs
{
    /// <summary>The venue that made the sale, or null for a vending machine.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>What was sold.</summary>
    public ItemType Meal { get; }

    /// <summary>The price paid.</summary>
    public int Price { get; }

    /// <summary>The customer.</summary>
    public Person Customer { get; }

    internal SaleMadeArgs(VenueAreaGhost area, ItemType meal, int price, Person customer)
    {
        Area = area;
        Meal = meal;
        Price = price;
        Customer = customer;
    }
}

/// <summary>Arguments for <see cref="GameEvents.DeliveryCompleted"/>.</summary>
public sealed class DeliveryCompletedArgs
{
    /// <summary>The venue area receiving the delivery.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The staff member who delivered it.</summary>
    public AgentGhost Staff { get; }

    /// <summary>Items that went into storage, by type.</summary>
    public IReadOnlyDictionary<ItemType, int> Items { get; }

    internal DeliveryCompletedArgs(VenueAreaGhost area, AgentGhost staff, IReadOnlyDictionary<ItemType, int> items)
    {
        Area = area;
        Staff = staff;
        Items = items;
    }
}

/// <summary>Arguments for <see cref="GameEvents.ShopOpened"/> and <see cref="GameEvents.ShopClosed"/>.</summary>
public sealed class ShopArgs
{
    /// <summary>The vendor. Null for ShopClosed if the kit didn't see the shop open.</summary>
    public Vendor Vendor { get; }

    internal ShopArgs(Vendor vendor) => Vendor = vendor;
}

/// <summary>Arguments for <see cref="GameEvents.PlayerBought"/> and <see cref="GameEvents.PlayerSold"/>.</summary>
public sealed class PlayerTradeArgs
{
    /// <summary>The vendor whose shop is open, or null.</summary>
    public Vendor Vendor { get; }

    /// <summary>The item.</summary>
    public ItemType Item { get; }

    /// <summary>Number of items.</summary>
    public int Count { get; }

    /// <summary>Total price paid or received.</summary>
    public int TotalPrice { get; }

    /// <summary>The player container the items went into (bought) or came from (sold).</summary>
    public IItemContainer Container { get; }

    internal PlayerTradeArgs(Vendor vendor, ItemType item, int count, int totalPrice, IItemContainer container)
    {
        Vendor = vendor;
        Item = item;
        Count = count;
        TotalPrice = totalPrice;
        Container = container;
    }
}

/// <summary>Arguments for <see cref="GameEvents.MoneyChanged"/>.</summary>
public sealed class MoneyChangedArgs
{
    /// <summary>Money before the change.</summary>
    public int Old { get; }

    /// <summary>Money after the change.</summary>
    public int New { get; }

    /// <summary>New minus Old: positive for income, negative for spending.</summary>
    public int Delta => New - Old;

    internal MoneyChangedArgs(int oldValue, int newValue)
    {
        Old = oldValue;
        New = newValue;
    }
}

/// <summary>Arguments for the quest events.</summary>
public sealed class QuestArgs
{
    /// <summary>The quest definition.</summary>
    public Quest Quest { get; }

    /// <summary>The quest's id (Quest.Guid), or null.</summary>
    public string Id { get; }

    /// <summary>The quest's title, localized, or null.</summary>
    public string Title { get; }

    internal QuestArgs(Quest quest)
    {
        Quest = quest;
        try { Id = quest?.Guid; } catch { }
        try { Title = quest?.Title; } catch { }
    }
}

/// <summary>Arguments for <see cref="GameEvents.QuestObjectiveStarted"/> and <see cref="GameEvents.QuestObjectiveCompleted"/>.</summary>
public sealed class QuestObjectiveArgs
{
    /// <summary>The quest the objective belongs to.</summary>
    public QuestArgs Quest { get; }

    /// <summary>The objective. The game calls these sub-quests.</summary>
    public SubQuest Objective { get; }

    /// <summary>The objective's id (SubQuest.ArticyId), or null.</summary>
    public string Id { get; }

    /// <summary>The objective's progress text as the journal shows it, or null.</summary>
    public string Text { get; }

    internal QuestObjectiveArgs(Quest quest, SubQuest objective)
    {
        Quest = new QuestArgs(quest);
        Objective = objective;
        try { Id = objective?.ArticyId; } catch { }
        try { Text = objective?.ProgressText; } catch { }
    }
}

/// <summary>Arguments for <see cref="GameEvents.QuestPinnedChanged"/>.</summary>
public sealed class QuestPinnedArgs
{
    /// <summary>The quest.</summary>
    public QuestArgs Quest { get; }

    /// <summary>True if now pinned.</summary>
    public bool Pinned { get; }

    internal QuestPinnedArgs(Quest quest, bool pinned)
    {
        Quest = new QuestArgs(quest);
        Pinned = pinned;
    }
}

/// <summary>Arguments for <see cref="GameEvents.QuestMarkerAdded"/> and <see cref="GameEvents.QuestMarkerRemoved"/>.</summary>
public sealed class QuestMarkerArgs
{
    /// <summary>The world point's id.</summary>
    public string PointId { get; }

    /// <summary>The scene the point is in.</summary>
    public int SceneIndex { get; }

    /// <summary>The quest the marker is for.</summary>
    public QuestArgs Quest { get; }

    internal QuestMarkerArgs(string pointId, int sceneIndex, Quest quest)
    {
        PointId = pointId;
        SceneIndex = sceneIndex;
        Quest = new QuestArgs(quest);
    }
}

/// <summary>Arguments for <see cref="GameEvents.VenueSetupQuestUpdated"/>.</summary>
public sealed class VenueSetupQuestArgs
{
    /// <summary>The live venue setup quest: <c>Venue</c>, <c>ActiveObjectives</c>, <c>CompletedObjectives</c>.</summary>
    public RuntimeVenueSetupQuest SetupQuest { get; }

    /// <summary>Its title, or null.</summary>
    public string Title { get; }

    /// <summary>Its state.</summary>
    public QuestState State { get; }

    internal VenueSetupQuestArgs(RuntimeVenueSetupQuest setupQuest)
    {
        SetupQuest = setupQuest;
        try { Title = setupQuest?.Title; } catch { }
        try { State = setupQuest?.State ?? QuestState.None; } catch { }
    }
}
