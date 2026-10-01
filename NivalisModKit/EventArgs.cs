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

/// <summary>Arguments for <see cref="GameEvents.AwarenessIncreased"/>.</summary>
public sealed class AwarenessArgs
{
    /// <summary>How much awareness rose.</summary>
    public float Delta { get; }

    /// <summary>Awareness after the rise.</summary>
    public float Awareness { get; }

    internal AwarenessArgs(float delta, float awareness)
    {
        Delta = delta;
        Awareness = awareness;
    }
}

/// <summary>Arguments for <see cref="GameEvents.SecurityLevelChanged"/>.</summary>
public sealed class SecurityLevelArgs
{
    /// <summary>The district.</summary>
    public WorldLocation District { get; }

    /// <summary>Its new security level.</summary>
    public int Level { get; }

    internal SecurityLevelArgs(WorldLocation district, int level)
    {
        District = district;
        Level = level;
    }
}

/// <summary>Arguments for events about one item type (fish caught, crop planted).</summary>
public sealed class ItemArgs
{
    /// <summary>The item.</summary>
    public ItemType Item { get; }

    internal ItemArgs(ItemType item) => Item = item;
}

/// <summary>Arguments for <see cref="GameEvents.CropHarvested"/>.</summary>
public sealed class CropHarvestedArgs
{
    /// <summary>The plant harvested.</summary>
    public ItemType Plant { get; }

    /// <summary>True the first time the player harvests this plant.</summary>
    public bool FirstTime { get; }

    internal CropHarvestedArgs(ItemType plant, bool firstTime)
    {
        Plant = plant;
        FirstTime = firstTime;
    }
}

/// <summary>Arguments for the property events.</summary>
public sealed class PropertyArgs
{
    /// <summary>The property: a Venue, Apartment or greenhouse, all BaseProperty.</summary>
    public BaseProperty Property { get; }

    /// <summary>True if the player owns or rents it now.</summary>
    public bool PlayerOwned { get; }

    internal PropertyArgs(BaseProperty property)
    {
        Property = property;
        try { PlayerOwned = property != null && property.PlayerOwned; } catch { }
    }
}

/// <summary>Arguments for <see cref="GameEvents.FurniturePlaced"/> and <see cref="GameEvents.FurnitureStored"/>.</summary>
public sealed class FurnitureArgs
{
    /// <summary>The placed or stored object.</summary>
    public HoldableEntity Entity { get; }

    internal FurnitureArgs(HoldableEntity entity) => Entity = entity;
}

/// <summary>Arguments for <see cref="GameEvents.ApartmentEntered"/> and <see cref="GameEvents.ApartmentLeft"/>.</summary>
public sealed class ApartmentArgs
{
    /// <summary>The apartment's scene controller.</summary>
    public Nivalis.Apartment.ApartmentController Apartment { get; }

    internal ApartmentArgs(Nivalis.Apartment.ApartmentController apartment) => Apartment = apartment;
}

/// <summary>Arguments for <see cref="GameEvents.VenueOwnerChanged"/>.</summary>
public sealed class VenueOwnerArgs
{
    /// <summary>The venue.</summary>
    public Venue Venue { get; }

    /// <summary>Its live data (staff, storage, menu), or null.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>True if the player owns or rents it now.</summary>
    public bool PlayerOwned { get; }

    internal VenueOwnerArgs(Venue venue)
    {
        Venue = venue;
        try { Area = venue?.RuntimeData; } catch { }
        try { PlayerOwned = venue != null && venue.PlayerOwned; } catch { }
    }
}

/// <summary>Arguments for events about one venue.</summary>
public sealed class VenueArgs
{
    /// <summary>The venue's live data.</summary>
    public VenueAreaGhost Area { get; }

    internal VenueArgs(VenueAreaGhost area) => Area = area;
}

/// <summary>Arguments for <see cref="GameEvents.StaffHired"/> and <see cref="GameEvents.StaffFired"/>.</summary>
public sealed class StaffArgs
{
    /// <summary>The venue.</summary>
    public Venue Venue { get; }

    /// <summary>The staff member.</summary>
    public Person Person { get; }

    internal StaffArgs(Venue venue, Person person)
    {
        Venue = venue;
        Person = person;
    }
}

/// <summary>Arguments for <see cref="GameEvents.StaffPaid"/>.</summary>
public sealed class StaffPaidArgs
{
    /// <summary>The venue paying.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The staff member.</summary>
    public Person Person { get; }

    /// <summary>The wage due (RuntimePersonData.Wage), in hundredths.</summary>
    public int Wage { get; }

    /// <summary>
    /// True if the owner was charged. False when the owner couldn't afford it; the game then lowers
    /// the employee's happiness instead of paying.
    /// </summary>
    public bool Paid { get; }

    internal StaffPaidArgs(VenueAreaGhost area, Person person, int wage, bool paid)
    {
        Area = area;
        Person = person;
        Wage = wage;
        Paid = paid;
    }
}

/// <summary>Arguments for <see cref="GameEvents.StaffSkillGained"/>.</summary>
public sealed class StaffSkillArgs
{
    /// <summary>The person.</summary>
    public Person Person { get; }

    /// <summary>The skill.</summary>
    public Nivalis.SkillSystem.SkillDefinition Skill { get; }

    /// <summary>Experience gained.</summary>
    public float Amount { get; }

    internal StaffSkillArgs(Person person, Nivalis.SkillSystem.SkillDefinition skill, float amount)
    {
        Person = person;
        Skill = skill;
        Amount = amount;
    }
}

/// <summary>Arguments for <see cref="GameEvents.StaffRolesChanged"/>.</summary>
public sealed class StaffRolesArgs
{
    /// <summary>The venue.</summary>
    public Venue Venue { get; }

    /// <summary>The staff member's live data.</summary>
    public RuntimePersonData Person { get; }

    /// <summary>The new roles (flags: Serving, Cooking, Cleaning, Managing).</summary>
    public VenueTasks Roles { get; }

    internal StaffRolesArgs(Venue venue, RuntimePersonData person, VenueTasks roles)
    {
        Venue = venue;
        Person = person;
        Roles = roles;
    }
}

/// <summary>Arguments for <see cref="GameEvents.TheftCommitted"/>.</summary>
public sealed class TheftArgs
{
    /// <summary>The venue stolen from.</summary>
    public VenueAreaGhost Area { get; }

    /// <summary>The stolen furniture.</summary>
    public Nivalis.GhostSystem.Ghost Furniture { get; }

    internal TheftArgs(VenueAreaGhost area, Nivalis.GhostSystem.Ghost furniture)
    {
        Area = area;
        Furniture = furniture;
    }
}

/// <summary>Arguments for <see cref="GameEvents.CameraDisabled"/>.</summary>
public sealed class CameraArgs
{
    /// <summary>The camera.</summary>
    public SecurityCamera Camera { get; }

    internal CameraArgs(SecurityCamera camera) => Camera = camera;
}

/// <summary>Arguments for <see cref="GameEvents.BoatDocked"/> and <see cref="GameEvents.BoatUndocked"/>.</summary>
public sealed class BoatDockArgs
{
    /// <summary>The dock.</summary>
    public Nivalis.Boat.BoatDock Dock { get; }

    internal BoatDockArgs(Nivalis.Boat.BoatDock dock) => Dock = dock;
}

/// <summary>Arguments for <see cref="GameEvents.BoatTravel"/>.</summary>
public sealed class BoatTravelArgs
{
    /// <summary>The destination portal.</summary>
    public PortalKey Destination { get; }

    internal BoatTravelArgs(PortalKey destination) => Destination = destination;
}

/// <summary>Arguments for <see cref="GameEvents.BoatRefueled"/>.</summary>
public sealed class BoatRefueledArgs
{
    /// <summary>Fuel added.</summary>
    public float FuelAdded { get; }

    /// <summary>Fuel now.</summary>
    public float Fuel { get; }

    internal BoatRefueledArgs(float added, float fuel)
    {
        FuelAdded = added;
        Fuel = fuel;
    }
}

/// <summary>Arguments for <see cref="GameEvents.DayEnded"/>.</summary>
public sealed class DayEndedArgs
{
    /// <summary>The game day that ended.</summary>
    public int Day { get; }

    internal DayEndedArgs(int day) => Day = day;
}

/// <summary>Arguments for <see cref="GameEvents.PlayerCaught"/>.</summary>
public sealed class PlayerCaughtArgs
{
    /// <summary>True if a drone caught the player, false for a camera (or theft).</summary>
    public bool ByDrone { get; }

    /// <summary>The district the player was caught in.</summary>
    public WorldLocation District { get; }

    /// <summary>The district's security level after the catch (the game raises it).</summary>
    public int SecurityLevel { get; }

    internal PlayerCaughtArgs(bool byDrone, WorldLocation district, int level)
    {
        ByDrone = byDrone;
        District = district;
        SecurityLevel = level;
    }
}

/// <summary>Arguments for <see cref="GameEvents.StaffHoursChanged"/>.</summary>
public sealed class StaffHoursArgs
{
    /// <summary>The venue the staff member works at.</summary>
    public Venue Venue { get; }

    /// <summary>The staff member's live data.</summary>
    public RuntimePersonData Person { get; }

    /// <summary>Shift start and end hours before the change (x = start, y = end; past 24 is after midnight).</summary>
    public UnityEngine.Vector2 Before { get; }

    /// <summary>Shift start and end hours after the change.</summary>
    public UnityEngine.Vector2 After { get; }

    internal StaffHoursArgs(Venue venue, RuntimePersonData person, UnityEngine.Vector2 before, UnityEngine.Vector2 after)
    {
        Venue = venue;
        Person = person;
        Before = before;
        After = after;
    }
}
