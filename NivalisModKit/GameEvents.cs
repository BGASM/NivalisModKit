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

    /// <summary>A quest marker (world point) was switched on.</summary>
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

    internal static void Raise(string name, Action handler)
    {
        if (handler == null) return;
        foreach (Delegate d in handler.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception e) { LogFailure(name, d, e); }
        }
    }

    internal static void Raise<T>(string name, Action<T> handler, T arg)
    {
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
