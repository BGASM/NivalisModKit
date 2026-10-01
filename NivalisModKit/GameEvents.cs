using System;
using System.Collections.Generic;

namespace NivalisModKit;

/// <summary>
/// Game events raised by the kit. Subscribe in your plugin's Load. An event whose patch
/// failed to install never fires; check <see cref="IsAvailable"/> to degrade gracefully.
/// </summary>
public static partial class GameEvents
{
    internal static readonly HashSet<string> Live = new();

    /// <summary>
    /// True if the named event's patch installed and the event will fire.
    /// Pass <c>nameof(GameEvents.SomeEvent)</c>.
    /// </summary>
    public static bool IsAvailable(string name) => name != null && Live.Contains(name);

    /// <summary>A venue area is about to buy ingredients for one recipe.</summary>
    public static event Action<BuyIngredientsArgs> BuyIngredientsStarting;

    /// <summary>
    /// A venue area finished buying ingredients for one recipe. Raised after every other
    /// mod's patches on the purchase, so the purchases are final.
    /// </summary>
    public static event Action<BuyIngredientsArgs> BuyIngredientsFinished;

    /// <summary>
    /// A venue area ran its hourly update. Fires for every venue in the world, not only the
    /// player's; check <c>Area.PlayerOwned</c>.
    /// </summary>
    public static event Action<VenueHourArgs> VenueHour;

    /// <summary>
    /// A venue area successfully bought one stack of an ingredient. Fires once per vendor
    /// purchase, inside a restock round (between BuyIngredientsStarting and Finished); failed
    /// purchases and equipment purchases don't fire. Needs both BuyIngredients events.
    /// </summary>
    public static event Action<IngredientsPurchasedArgs> IngredientsPurchased;

    /// <summary>
    /// A venue bought something outside a restock round: equipment, such as the drinks machine a
    /// venue needs before it can serve drinks. NPC venues do this too. Needs both BuyIngredients events.
    /// </summary>
    public static event Action<EquipmentPurchasedArgs> EquipmentPurchased;

    /// <summary>
    /// A new game day started. The game's day turns over at 08:00, not midnight. Doesn't fire
    /// while a save loads.
    /// </summary>
    public static event Action<DayStartedArgs> DayStarted;

    /// <summary>
    /// The world clock reached a new hour. Fires once per clock change: when sleeping skips
    /// several hours it fires once, with the new hour. Doesn't fire while a save loads.
    /// </summary>
    public static event Action<HourStartedArgs> HourStarted;

    /// <summary>
    /// The player started a new game. Fires as the world begins loading; the starting district
    /// follows as <see cref="DistrictEntered"/>. Loading a save fires <see cref="GameLoaded"/> instead.
    /// </summary>
    public static event Action NewGameStarted;

    internal static void RaiseNewGameStarted() => Raise(nameof(NewGameStarted), NewGameStarted);

    /// <summary>A save finished loading into gameplay.</summary>
    public static event Action<GameLoadedArgs> GameLoaded;

    /// <summary>The game was saved successfully, manually or by autosave.</summary>
    public static event Action<GameSavedArgs> GameSaved;

    /// <summary>
    /// The player arrived in a different district. Going in and out of buildings doesn't count.
    /// Not raised for the starting district when a save loads; <see cref="GameLoadedArgs.District"/> has that.
    /// </summary>
    public static event Action<DistrictEnteredArgs> DistrictEntered;

    /// <summary>A dish finished cooking at a food processor, by staff or the player.</summary>
    public static event Action<DishCookedArgs> DishCooked;

    /// <summary>
    /// A customer paid: a restaurant receipt was created. Customers pay as they leave a venue;
    /// vending machine purchases also count, with no venue.
    /// </summary>
    public static event Action<SaleMadeArgs> SaleMade;

    /// <summary>
    /// Staff put a delivery of bought ingredients into a venue's storage. Reports what actually
    /// went in; items that didn't fit are delivered later and reported then.
    /// </summary>
    public static event Action<DeliveryCompletedArgs> DeliveryCompleted;

    /// <summary>The player opened a vendor's shop window.</summary>
    public static event Action<ShopArgs> ShopOpened;

    /// <summary>The player closed the shop window.</summary>
    public static event Action<ShopArgs> ShopClosed;

    /// <summary>
    /// The player bought something and paid for it. <c>Vendor</c> is the open shop, or null if
    /// the purchase happened elsewhere.
    /// </summary>
    public static event Action<PlayerTradeArgs> PlayerBought;

    /// <summary>The player sold something and was paid. <c>Vendor</c> is the open shop, or null.</summary>
    public static event Action<PlayerTradeArgs> PlayerSold;

    /// <summary>
    /// The player's money changed, as the money display shows it. While the game holds money
    /// events back (it can lock them during some operations), several changes arrive as one.
    /// </summary>
    public static event Action<MoneyChangedArgs> MoneyChanged;

    /// <summary>A quest started (the game's QuestManager.OnQuestStarted).</summary>
    public static event Action<QuestArgs> QuestStarted;

    /// <summary>A quest was completed (the game's QuestManager.OnQuestCompleted).</summary>
    public static event Action<QuestArgs> QuestCompleted;

    /// <summary>A quest failed.</summary>
    public static event Action<QuestArgs> QuestFailed;

    /// <summary>A quest objective (the game calls them sub-quests) started.</summary>
    public static event Action<QuestObjectiveArgs> QuestObjectiveStarted;

    /// <summary>A quest objective was completed.</summary>
    public static event Action<QuestObjectiveArgs> QuestObjectiveCompleted;

    /// <summary>The player pinned or unpinned a quest as their target.</summary>
    public static event Action<QuestPinnedArgs> QuestPinnedChanged;

    /// <summary>
    /// A quest marker (world point) was switched on. When the game refreshes a marker it switches
    /// it on, off and on again within a moment, so expect brief Added/Removed/Added sequences.
    /// </summary>
    public static event Action<QuestMarkerArgs> QuestMarkerAdded;

    /// <summary>A quest marker was switched off.</summary>
    public static event Action<QuestMarkerArgs> QuestMarkerRemoved;

    /// <summary>A venue setup quest changed state or progress.</summary>
    public static event Action<VenueSetupQuestArgs> VenueSetupQuestUpdated;

    internal static void RaiseQuestStarted(QuestArgs a) => Raise(nameof(QuestStarted), QuestStarted, a);
    internal static void RaiseQuestCompleted(QuestArgs a) => Raise(nameof(QuestCompleted), QuestCompleted, a);
    internal static void RaiseQuestFailed(QuestArgs a) => Raise(nameof(QuestFailed), QuestFailed, a);
    internal static void RaiseQuestObjectiveStarted(QuestObjectiveArgs a) => Raise(nameof(QuestObjectiveStarted), QuestObjectiveStarted, a);
    internal static void RaiseQuestObjectiveCompleted(QuestObjectiveArgs a) => Raise(nameof(QuestObjectiveCompleted), QuestObjectiveCompleted, a);
    internal static void RaiseQuestPinnedChanged(QuestPinnedArgs a) => Raise(nameof(QuestPinnedChanged), QuestPinnedChanged, a);
    internal static void RaiseQuestMarkerAdded(QuestMarkerArgs a) => Raise(nameof(QuestMarkerAdded), QuestMarkerAdded, a);
    internal static void RaiseQuestMarkerRemoved(QuestMarkerArgs a) => Raise(nameof(QuestMarkerRemoved), QuestMarkerRemoved, a);
    internal static void RaiseVenueSetupQuestUpdated(VenueSetupQuestArgs a) => Raise(nameof(VenueSetupQuestUpdated), VenueSetupQuestUpdated, a);

    /// <summary>The player was caught breaking curfew (by a camera or drone).</summary>
    public static event Action PlayerCaught;
    /// <summary>Security awareness of the player rose (being seen during curfew, theft).</summary>
    public static event Action<AwarenessArgs> AwarenessIncreased;
    /// <summary>A district's security level changed.</summary>
    public static event Action<SecurityLevelArgs> SecurityLevelChanged;
    /// <summary>Curfew began (02:00). Cameras and drones switch on.</summary>
    public static event Action CurfewStarted;
    /// <summary>Curfew ended (08:00).</summary>
    public static event Action CurfewEnded;
    /// <summary>The curfew warning period began (an hour before curfew).</summary>
    public static event Action CurfewWarning;
    /// <summary>A fish (or other catch) went into the player's inventory.</summary>
    public static event Action<ItemArgs> FishCaught;
    /// <summary>The player caught a fish species for the first time.</summary>
    public static event Action<ItemArgs> FishDiscovered;
    /// <summary>A crop was planted in a greenhouse module. Item is the plant.</summary>
    public static event Action<ItemArgs> CropPlanted;
    /// <summary>A crop was harvested.</summary>
    public static event Action<CropHarvestedArgs> CropHarvested;
    /// <summary>Any property (venue, apartment, greenhouse) was bought, sold, rented or given up.</summary>
    public static event Action<PropertyArgs> PropertyOwnerChanged;
    /// <summary>A property started being rented.</summary>
    public static event Action<PropertyArgs> RentStarted;
    /// <summary>A property stopped being rented.</summary>
    public static event Action<PropertyArgs> RentStopped;
    /// <summary>The player placed a piece of furniture or another holdable object.</summary>
    public static event Action<FurnitureArgs> FurniturePlaced;
    /// <summary>The player picked up and stored a piece of furniture or another holdable object.</summary>
    public static event Action<FurnitureArgs> FurnitureStored;
    /// <summary>The player entered an apartment.</summary>
    public static event Action<ApartmentArgs> ApartmentEntered;
    /// <summary>The player left an apartment.</summary>
    public static event Action<ApartmentArgs> ApartmentLeft;
    /// <summary>A venue was bought, sold, given or rented, by the player or an NPC owner.</summary>
    public static event Action<VenueOwnerArgs> VenueOwnerChanged;
    internal static void RaisePlayerCaught() => Raise(nameof(PlayerCaught), PlayerCaught);
    internal static void RaiseAwarenessIncreased(AwarenessArgs a) => Raise(nameof(AwarenessIncreased), AwarenessIncreased, a);
    internal static void RaiseSecurityLevelChanged(SecurityLevelArgs a) => Raise(nameof(SecurityLevelChanged), SecurityLevelChanged, a);
    internal static void RaiseCurfewStarted() => Raise(nameof(CurfewStarted), CurfewStarted);
    internal static void RaiseCurfewEnded() => Raise(nameof(CurfewEnded), CurfewEnded);
    internal static void RaiseCurfewWarning() => Raise(nameof(CurfewWarning), CurfewWarning);
    internal static void RaiseFishCaught(ItemArgs a) => Raise(nameof(FishCaught), FishCaught, a);
    internal static void RaiseFishDiscovered(ItemArgs a) => Raise(nameof(FishDiscovered), FishDiscovered, a);
    internal static void RaiseCropPlanted(ItemArgs a) => Raise(nameof(CropPlanted), CropPlanted, a);
    internal static void RaiseCropHarvested(CropHarvestedArgs a) => Raise(nameof(CropHarvested), CropHarvested, a);
    internal static void RaisePropertyOwnerChanged(PropertyArgs a) => Raise(nameof(PropertyOwnerChanged), PropertyOwnerChanged, a);
    internal static void RaiseRentStarted(PropertyArgs a) => Raise(nameof(RentStarted), RentStarted, a);
    internal static void RaiseRentStopped(PropertyArgs a) => Raise(nameof(RentStopped), RentStopped, a);
    internal static void RaiseFurniturePlaced(FurnitureArgs a) => Raise(nameof(FurniturePlaced), FurniturePlaced, a);
    internal static void RaiseFurnitureStored(FurnitureArgs a) => Raise(nameof(FurnitureStored), FurnitureStored, a);
    internal static void RaiseApartmentEntered(ApartmentArgs a) => Raise(nameof(ApartmentEntered), ApartmentEntered, a);
    internal static void RaiseApartmentLeft(ApartmentArgs a) => Raise(nameof(ApartmentLeft), ApartmentLeft, a);
    internal static void RaiseVenueOwnerChanged(VenueOwnerArgs a) => Raise(nameof(VenueOwnerChanged), VenueOwnerChanged, a);

    /// <summary>Staff were hired at a venue.</summary>
    public static event Action<StaffArgs> StaffHired;
    /// <summary>Staff were fired from a venue.</summary>
    public static event Action<StaffArgs> StaffFired;
    /// <summary>A venue paid a staff member's wage.</summary>
    public static event Action<StaffPaidArgs> StaffPaid;
    /// <summary>A person gained experience in a skill.</summary>
    public static event Action<StaffSkillArgs> StaffSkillGained;
    /// <summary>A staff member's roles (serving, cooking, cleaning, managing) were changed.</summary>
    public static event Action<StaffRolesArgs> StaffRolesChanged;
    /// <summary>A venue's staff working hours changed.</summary>
    public static event Action<VenueArgs> StaffHoursChanged;
    /// <summary>The player stole furniture from a venue they don't own (during curfew).</summary>
    public static event Action<TheftArgs> TheftCommitted;
    /// <summary>The player disabled a security camera.</summary>
    public static event Action<CameraArgs> CameraDisabled;
    /// <summary>The player took the boat's helm.</summary>
    public static event Action BoatBoarded;
    /// <summary>The player left the boat's helm.</summary>
    public static event Action BoatLeft;
    /// <summary>The boat docked.</summary>
    public static event Action<BoatDockArgs> BoatDocked;
    /// <summary>The boat left a dock.</summary>
    public static event Action<BoatDockArgs> BoatUndocked;
    /// <summary>The player fast-travelled by boat.</summary>
    public static event Action<BoatTravelArgs> BoatTravel;
    /// <summary>The boat finished refuelling at a pump.</summary>
    public static event Action<BoatRefueledArgs> BoatRefueled;
    /// <summary>A venue opened. Detected on its hourly update, so up to an hour late.</summary>
    public static event Action<VenueArgs> VenueOpened;
    /// <summary>A venue closed. Detected on its hourly update, so up to an hour late.</summary>
    public static event Action<VenueArgs> VenueClosed;
    internal static void RaiseStaffHired(StaffArgs x) => Raise(nameof(StaffHired), StaffHired, x);
    internal static void RaiseStaffFired(StaffArgs x) => Raise(nameof(StaffFired), StaffFired, x);
    internal static void RaiseStaffPaid(StaffPaidArgs x) => Raise(nameof(StaffPaid), StaffPaid, x);
    internal static void RaiseStaffSkillGained(StaffSkillArgs x) => Raise(nameof(StaffSkillGained), StaffSkillGained, x);
    internal static void RaiseStaffRolesChanged(StaffRolesArgs x) => Raise(nameof(StaffRolesChanged), StaffRolesChanged, x);
    internal static void RaiseStaffHoursChanged(VenueArgs x) => Raise(nameof(StaffHoursChanged), StaffHoursChanged, x);
    internal static void RaiseTheftCommitted(TheftArgs x) => Raise(nameof(TheftCommitted), TheftCommitted, x);
    internal static void RaiseCameraDisabled(CameraArgs x) => Raise(nameof(CameraDisabled), CameraDisabled, x);
    internal static void RaiseBoatBoarded() => Raise(nameof(BoatBoarded), BoatBoarded);
    internal static void RaiseBoatLeft() => Raise(nameof(BoatLeft), BoatLeft);
    internal static void RaiseBoatDocked(BoatDockArgs x) => Raise(nameof(BoatDocked), BoatDocked, x);
    internal static void RaiseBoatUndocked(BoatDockArgs x) => Raise(nameof(BoatUndocked), BoatUndocked, x);
    internal static void RaiseBoatTravel(BoatTravelArgs x) => Raise(nameof(BoatTravel), BoatTravel, x);
    internal static void RaiseBoatRefueled(BoatRefueledArgs x) => Raise(nameof(BoatRefueled), BoatRefueled, x);
    internal static void RaiseVenueOpened(VenueArgs x) => Raise(nameof(VenueOpened), VenueOpened, x);
    internal static void RaiseVenueClosed(VenueArgs x) => Raise(nameof(VenueClosed), VenueClosed, x);

    internal static void RaiseMoneyChanged(MoneyChangedArgs a) => Raise(nameof(MoneyChanged), MoneyChanged, a);

    internal static void RaiseShopOpened(ShopArgs a) => Raise(nameof(ShopOpened), ShopOpened, a);
    internal static void RaiseShopClosed(ShopArgs a) => Raise(nameof(ShopClosed), ShopClosed, a);
    internal static void RaisePlayerBought(PlayerTradeArgs a) => Raise(nameof(PlayerBought), PlayerBought, a);
    internal static void RaisePlayerSold(PlayerTradeArgs a) => Raise(nameof(PlayerSold), PlayerSold, a);

    internal static void RaiseGameLoaded(GameLoadedArgs a) => Raise(nameof(GameLoaded), GameLoaded, a);
    internal static void RaiseGameSaved(GameSavedArgs a) => Raise(nameof(GameSaved), GameSaved, a);
    internal static void RaiseDistrictEntered(DistrictEnteredArgs a) => Raise(nameof(DistrictEntered), DistrictEntered, a);
    internal static void RaiseDishCooked(DishCookedArgs a) => Raise(nameof(DishCooked), DishCooked, a);
    internal static void RaiseSaleMade(SaleMadeArgs a) => Raise(nameof(SaleMade), SaleMade, a);
    internal static void RaiseDeliveryCompleted(DeliveryCompletedArgs a) => Raise(nameof(DeliveryCompleted), DeliveryCompleted, a);

    internal static void RaiseEquipmentPurchased(EquipmentPurchasedArgs a) =>
        Raise(nameof(EquipmentPurchased), EquipmentPurchased, a);

    internal static void RaiseDayStarted(DayStartedArgs a) =>
        Raise(nameof(DayStarted), DayStarted, a);

    internal static void RaiseHourStarted(HourStartedArgs a) =>
        Raise(nameof(HourStarted), HourStarted, a);

    internal static void RaiseBuyIngredientsStarting(BuyIngredientsArgs a) =>
        Raise(nameof(BuyIngredientsStarting), BuyIngredientsStarting, a);

    internal static void RaiseBuyIngredientsFinished(BuyIngredientsArgs a) =>
        Raise(nameof(BuyIngredientsFinished), BuyIngredientsFinished, a);

    internal static void RaiseVenueHour(VenueHourArgs a) =>
        Raise(nameof(VenueHour), VenueHour, a);

    internal static void RaiseIngredientsPurchased(IngredientsPurchasedArgs a) =>
        Raise(nameof(IngredientsPurchased), IngredientsPurchased, a);

    // Each subscriber runs in its own try, so one failing mod can't stop the others
    // or throw into game code.

    // How often each event fired and when last, for the dev bridge. Main thread only.
    internal static readonly Dictionary<string, (int count, DateTime last)> Fired = new();

    static void Count(string name) =>
        Fired[name] = (Fired.TryGetValue(name, out var f) ? f.count + 1 : 1, DateTime.Now);

    internal static void Raise(string name, Action handler)
    {
        Count(name);
        if (handler == null) return;
        foreach (Delegate d in handler.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception e) { LogFailure(name, d, e); }
        }
    }

    internal static void Raise<T>(string name, Action<T> handler, T arg)
    {
        Count(name);
        if (handler == null) return;
        foreach (Delegate d in handler.GetInvocationList())
        {
            try { ((Action<T>)d)(arg); }
            catch (Exception e) { LogFailure(name, d, e); }
        }
    }

    internal static void LogFailure(string name, Delegate d, Exception e)
    {
        string who = d.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
        KitPlugin.L?.LogError($"{name} handler in {who} ({d.Method.DeclaringType?.Name}.{d.Method.Name}) threw: {e}");
    }
}
