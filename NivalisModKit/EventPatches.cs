using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.Economy;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem;
using Nivalis.UI;
using Nivalis.VenueSupplyQuest;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.Player;
using IL2List = Il2CppSystem.Collections.Generic.List<Nivalis.InventorySystem.ItemInstanceData>;

namespace NivalisModKit;

// Installs one patch per event, each in its own try, so a game update that breaks one
// target leaves the rest working. Every event goes through Install.
static class EventPatches
{
    static Harmony harmony;
    static HashSet<string> simulated;
    static int attempted;

    internal static void InstallAll()
    {
        harmony = new Harmony(ModKit.Guid);
        simulated = new HashSet<string>(
            KitPlugin.SimulateMissing.Value.Split(',').Select(s => s.Trim()).Where(s => s != ""));

        // The target type is passed as a function so a type missing from the interop
        // assemblies fails inside that event's try, not the whole installer.
        Install(nameof(GameEvents.BuyIngredientsStarting),
            () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryPurchaseIngredients),
            prefix: nameof(BuyStartPrefix));

        // Last, so it runs after Order Fix flushes its deferred purchases.
        Install(nameof(GameEvents.BuyIngredientsFinished),
            () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryPurchaseIngredients),
            postfix: nameof(BuyFinishPostfix), priority: Priority.Last);

        Install(nameof(GameEvents.VenueHour),
            () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.OnHourUpdate),
            postfix: nameof(VenueHourPostfix));

        // TryMakePurchase also buys equipment (e.g. Drinks_Machine), so this only fires inside a
        // restock round, which the BuyIngredients patches mark.
        Install(nameof(GameEvents.IngredientsPurchased),
            () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryMakePurchase),
            postfix: nameof(PurchasePostfix),
            requires: new[] { nameof(GameEvents.BuyIngredientsStarting), nameof(GameEvents.BuyIngredientsFinished) });

        // The same purchases outside a restock round: equipment such as drinks machines.
        Install(nameof(GameEvents.EquipmentPurchased),
            () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.TryMakePurchase),
            postfix: nameof(EquipmentPostfix),
            requires: new[] { nameof(GameEvents.BuyIngredientsStarting), nameof(GameEvents.BuyIngredientsFinished) });

        // The game's own clock events (TimeOfDayManager.OnTimeUpdate calls them), so no patch:
        // same 08:00 day boundary as the game, and silent while a save loads.
        Subscribe(nameof(GameEvents.DayStarted), () => Via(() => TimeOfDayManager.OnDayChange, OnDayChange));
        Subscribe(nameof(GameEvents.HourStarted), () => Via(() => TimeOfDayManager.OnHourTick, OnHourTick));

        // SerializationManager owns saving and loading. Load only starts a coroutine, so the
        // prefix just records the save name; OnPostLoad fires when loading has finished.
        Helper("save name for GameLoaded", () => typeof(SerializationManager),
            nameof(SerializationManager.Load), prefix: nameof(LoadPrefix));
        Subscribe(nameof(GameEvents.GameLoaded), () => Via(() => SerializationManager.OnPostLoad, OnPostLoad));

        // A new game doesn't load a save, so OnPostLoad never fires for it.
        Install(nameof(GameEvents.NewGameStarted),
            () => typeof(GameSceneManager), nameof(GameSceneManager.StartGame),
            postfix: nameof(StartGamePostfix), args: () => Type.EmptyTypes);

        // The one top-level save, manual and autosave alike.
        Install(nameof(GameEvents.GameSaved),
            () => typeof(SerializationManager), nameof(SerializationManager.Save),
            postfix: nameof(SavePostfix));

        Subscribe(nameof(GameEvents.DistrictEntered), ViaAreaArrival);

        // Returns the finished meal; the game raises its OnMealMade here. Staff and player cooking.
        Install(nameof(GameEvents.DishCooked),
            () => typeof(FoodProcessorGhost), nameof(FoodProcessorGhost.CraftRecipe),
            postfix: nameof(CraftPostfix));

        // Receipts are created in two places: a customer leaving a venue (LeaveReview, which has
        // the order and so the venue) and vending machines. The helper records the venue.
        Helper("venue for SaleMade", () => typeof(VisitVenueAgentActionType.LeaveVenueSubAction),
            "LeaveReview", prefix: nameof(LeaveReviewPrefix), postfix: nameof(LeaveReviewPostfix));
        Install(nameof(GameEvents.SaleMade),
            () => typeof(RestaurantReceipt), nameof(RestaurantReceipt.Init),
            postfix: nameof(ReceiptPostfix),
            args: () => new[] { typeof(ItemType), typeof(int), typeof(Person) });

        // The staff action's last step puts the delivery into storage. Items that don't fit are
        // queued as a new delivery, so count what actually arrived: storage before and after.
        Install(nameof(GameEvents.DeliveryCompleted),
            () => typeof(DeliverIngredientsActionType.DeliverIngredients), "Enter",
            prefix: nameof(DeliverPrefix), postfix: nameof(DeliverPostfix),
            args: () => new[] { typeof(AgentGhost), typeof(DeliverIngredientsActionType.State) });

        // Player shopping. VendorInteraction.DoInteraction can refuse (e.g. skill too low), so the
        // shop window opening is the reliable signal; it also remembers the vendor for trades.
        Install(nameof(GameEvents.ShopOpened),
            () => typeof(ShopUINew), nameof(ShopUINew.Vendor_OnShopRequested),
            postfix: nameof(ShopOpenedPostfix));
        Install(nameof(GameEvents.ShopClosed),
            () => typeof(ShopUINew), "OnClose",
            postfix: nameof(ShopClosedPostfix), args: () => Type.EmptyTypes);

        // Vendor.BuyItem/SellItem take their request by reference, so hook the player's side,
        // which they call through IShopUser. SellItem uses this TryMakeSale overload (slot 3).
        Install(nameof(GameEvents.PlayerBought),
            () => typeof(PlayerManager.Player), nameof(PlayerManager.Player.TryMakePurchase),
            postfix: nameof(PlayerBoughtPostfix),
            args: () => new[] { typeof(IItemContainer), typeof(ItemType), typeof(int), typeof(IL2List) });
        Install(nameof(GameEvents.PlayerSold),
            () => typeof(PlayerManager.Player), nameof(PlayerManager.Player.TryMakeSale),
            postfix: nameof(PlayerSoldPostfix),
            args: () => new[] { typeof(IItemContainer), typeof(ItemType), typeof(IL2List), typeof(int), typeof(int), typeof(IL2List) });

        // The player's own money event, the one the money display follows. Its source is the
        // local player's inventory, which exists only in gameplay and may be rebuilt on load,
        // so OnPostLoad re-attaches.
        Subscribe(nameof(GameEvents.MoneyChanged), ViaPlayerMoney);

        // Quests. QuestManager's events belong to the instance, so they re-attach after loads.
        Subscribe(nameof(GameEvents.QuestStarted), () => ViaQuestManager(qm => qm.OnQuestStarted, OnQuestStarted));
        Subscribe(nameof(GameEvents.QuestCompleted), () => ViaQuestManager(qm => qm.OnQuestCompleted, OnQuestCompleted));
        Install(nameof(GameEvents.QuestFailed),
            () => typeof(RuntimeQuest), nameof(RuntimeQuest.Fail),
            postfix: nameof(QuestFailedPostfix), args: () => Type.EmptyTypes);
        Install(nameof(GameEvents.QuestObjectiveStarted),
            () => typeof(SubQuest), nameof(SubQuest.InvokeStartActions),
            postfix: nameof(ObjectiveStartedPostfix));
        Install(nameof(GameEvents.QuestObjectiveCompleted),
            () => typeof(SubQuest), nameof(SubQuest.InvokeCompleteActions),
            postfix: nameof(ObjectiveCompletedPostfix));
        Install(nameof(GameEvents.QuestPinnedChanged),
            () => typeof(RuntimeQuest), "set_Pinned",
            prefix: nameof(PinnedPrefix), postfix: nameof(PinnedPostfix));
        // The Internal versions also run for markers the game queued before it was ready.
        Install(nameof(GameEvents.QuestMarkerAdded),
            () => typeof(WorldPointManager), nameof(WorldPointManager.ActivateQuestPointInternal),
            postfix: nameof(MarkerAddedPostfix));
        Install(nameof(GameEvents.QuestMarkerRemoved),
            () => typeof(WorldPointManager), nameof(WorldPointManager.DeactivateQuestPointInternal),
            postfix: nameof(MarkerRemovedPostfix));
        Install(nameof(GameEvents.VenueSetupQuestUpdated),
            () => typeof(VenueSetupManager), nameof(VenueSetupManager.SetupQuestUpdatedListener),
            postfix: nameof(VenueSetupPostfix));

        KitPlugin.L.LogInfo($"Events: {GameEvents.Live.Count} of {attempted} live" +
                            (waiting > 0 ? $", {waiting} waiting for the game" : ""));
    }

    // A patch an event depends on but that isn't an event itself. Failure is logged; the event
    // still works with less information.
    static void Helper(string what, Func<Type> type, string method, string prefix = null, string postfix = null)
    {
        try
        {
            Type t = type() ?? throw new Exception("target type not found");
            MethodBase target = AccessTools.Method(t, method) ?? throw new Exception($"{t.Name}.{method} not found");
            harmony.Patch(target, prefix: Hm(prefix, Priority.Normal), postfix: Hm(postfix, Priority.Normal));
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Patch for {what}: missing ({e.Message})"); }
    }

    // ---------- subscriptions to the game's own events ----------

    static readonly List<object> keepAlive = new();   // the game holds these delegates by pointer
    static int waiting;

    // make() builds the delegate and returns tryAdd, which subscribes and returns true, or returns
    // false while the game hasn't created the event object yet. Both run inside this try, so a
    // missing game type only takes out this event.
    static void Subscribe(string ev, Func<Func<bool>> make)
    {
        attempted++;
        try
        {
            if (simulated.Contains(ev)) throw new Exception("simulated missing");
            var tryAdd = make();
            if (TryAdd(ev, tryAdd)) return;

            waiting++;
            KitPlugin.L.LogInfo($"Event {ev}: waiting for the game");
            Action retry = null;
            retry = () =>
            {
                if (!TryAdd(ev, tryAdd)) return;
                KitLoop.Tick -= retry;
            };
            KitLoop.Tick += retry;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Event {ev}: missing ({e.Message})");
        }
    }

    static bool TryAdd(string ev, Func<bool> tryAdd)
    {
        if (!tryAdd()) return false;
        GameEvents.Live.Add(ev);
        KitPlugin.L.LogInfo($"Event {ev}: live");
        return true;
    }

    static Func<bool> Via(Func<ActionNonAlloc> source, Action handler)
    {
        Il2CppSystem.Action action = handler;
        keepAlive.Add(action);
        return () =>
        {
            var target = source();
            if (target == null) return false;
            target.Add(action);
            return true;
        };
    }

    static Func<bool> ViaAreaArrival()
    {
        Il2CppSystem.Action<WorldLocation> action = (Action<WorldLocation>)OnAreaArrival;
        keepAlive.Add(action);
        return () =>
        {
            var target = TravelManager.OnAreaArrival;
            if (target == null) return false;
            target.AddListener(action);
            return true;
        };
    }

    // Subscriptions to a manager instance that may be replaced when a save loads. OnPostLoad
    // runs each attach again; an attach skips the instance it already holds.
    static readonly List<Func<bool>> reattach = new();

    static Func<bool> ViaQuestManager(Func<QuestManager, SafeEvent<Quest>> source, Action<Quest> handler)
    {
        Il2CppSystem.Action<Quest> action = handler;
        keepAlive.Add(action);
        IntPtr attachedTo = IntPtr.Zero;
        Func<bool> attach = () =>
        {
            if (!Singleton<QuestManager>.InstanceExist(out var qm)) return false;
            if (qm.Pointer == attachedTo) return true;
            var target = source(qm);
            if (target == null) return false;
            target.AddListener(action);
            attachedTo = qm.Pointer;
            return true;
        };
        reattach.Add(attach);
        return attach;
    }

    static void OnQuestStarted(Quest quest)
    {
        try { GameEvents.RaiseQuestStarted(new QuestArgs(quest)); }
        catch (Exception e) { KitPlugin.L.LogError($"OnQuestStarted: {e}"); }
    }

    static void OnQuestCompleted(Quest quest)
    {
        try { GameEvents.RaiseQuestCompleted(new QuestArgs(quest)); }
        catch (Exception e) { KitPlugin.L.LogError($"OnQuestCompleted: {e}"); }
    }

    static void QuestFailedPostfix(RuntimeQuest __instance)
    {
        try { GameEvents.RaiseQuestFailed(new QuestArgs(__instance?.Quest)); }
        catch (Exception e) { KitPlugin.L.LogError($"QuestFailedPostfix: {e}"); }
    }

    static void ObjectiveStartedPostfix(SubQuest __instance, Quest baseQuest)
    {
        try { GameEvents.RaiseQuestObjectiveStarted(new QuestObjectiveArgs(baseQuest, __instance)); }
        catch (Exception e) { KitPlugin.L.LogError($"ObjectiveStartedPostfix: {e}"); }
    }

    static void ObjectiveCompletedPostfix(SubQuest __instance, Quest baseQuest)
    {
        try { GameEvents.RaiseQuestObjectiveCompleted(new QuestObjectiveArgs(baseQuest, __instance)); }
        catch (Exception e) { KitPlugin.L.LogError($"ObjectiveCompletedPostfix: {e}"); }
    }

    static void PinnedPrefix(RuntimeQuest __instance, out bool __state)
    {
        __state = false;
        try { __state = __instance != null && __instance.Pinned; } catch { }
    }

    static void PinnedPostfix(RuntimeQuest __instance, bool value, bool __state)
    {
        try
        {
            if (__instance == null || value == __state) return;
            GameEvents.RaiseQuestPinnedChanged(new QuestPinnedArgs(__instance.Quest, value));
        }
        catch (Exception e) { KitPlugin.L.LogError($"PinnedPostfix: {e}"); }
    }

    static void MarkerAddedPostfix(string id, int sceneIndex, Quest forQuest)
    {
        try { GameEvents.RaiseQuestMarkerAdded(new QuestMarkerArgs(id, sceneIndex, forQuest)); }
        catch (Exception e) { KitPlugin.L.LogError($"MarkerAddedPostfix: {e}"); }
    }

    static void MarkerRemovedPostfix(string id, int sceneIndex, Quest forQuest)
    {
        try { GameEvents.RaiseQuestMarkerRemoved(new QuestMarkerArgs(id, sceneIndex, forQuest)); }
        catch (Exception e) { KitPlugin.L.LogError($"MarkerRemovedPostfix: {e}"); }
    }

    static void VenueSetupPostfix(RuntimeVenueSetupQuest runtimeVenueSetupQuest)
    {
        try { GameEvents.RaiseVenueSetupQuestUpdated(new VenueSetupQuestArgs(runtimeVenueSetupQuest)); }
        catch (Exception e) { KitPlugin.L.LogError($"VenueSetupPostfix: {e}"); }
    }

    static Il2CppSystem.Action<int, int> moneyAction;
    static IntPtr moneySource = IntPtr.Zero;

    static Func<bool> ViaPlayerMoney()
    {
        moneyAction = (Action<int, int>)OnMoneyChanged;
        keepAlive.Add(moneyAction);
        reattach.Add(AttachPlayerMoney);
        return AttachPlayerMoney;
    }

    // True once attached to the current player's inventory.
    static bool AttachPlayerMoney()
    {
        if (moneyAction == null || !Singleton<PlayerManager>.InstanceExist(out var pm)) return false;
        var inventory = pm.LocalPlayer?.Inventory;
        if (inventory == null) return false;
        if (inventory.Pointer == moneySource) return true;

        var target = inventory.OnMoneyChanged;
        if (target == null) return false;
        target.Add(moneyAction);
        moneySource = inventory.Pointer;
        return true;
    }

    static void OnMoneyChanged(int oldValue, int newValue)
    {
        try { GameEvents.RaiseMoneyChanged(new MoneyChangedArgs(oldValue, newValue)); }
        catch (Exception e) { KitPlugin.L.LogError($"OnMoneyChanged: {e}"); }
    }

    static void OnDayChange()
    {
        try
        {
            GameEvents.RaiseDayStarted(new DayStartedArgs(
                TimeOfDayManager.GameplayGameDay, TimeOfDayManager.CurrentDayOfWeek));
        }
        catch (Exception e) { KitPlugin.L.LogError($"OnDayChange: {e}"); }
    }

    static void OnHourTick()
    {
        try
        {
            GameEvents.RaiseHourStarted(new HourStartedArgs(
                TimeOfDayManager.GameplayGameDay, TimeOfDayManager.ClockHour));
        }
        catch (Exception e) { KitPlugin.L.LogError($"OnHourTick: {e}"); }
    }

    static string loadingSave;

    static void LoadPrefix(string saveName) => loadingSave = saveName;

    // The vendor whose shop window is open, or null.
    static Vendor openShop;

    static void ShopOpenedPostfix(Vendor vendor)
    {
        try
        {
            openShop = vendor;
            GameEvents.RaiseShopOpened(new ShopArgs(vendor));
        }
        catch (Exception e) { KitPlugin.L.LogError($"ShopOpenedPostfix: {e}"); }
    }

    static void ShopClosedPostfix()
    {
        try
        {
            var vendor = openShop;
            openShop = null;
            GameEvents.RaiseShopClosed(new ShopArgs(vendor));
        }
        catch (Exception e) { KitPlugin.L.LogError($"ShopClosedPostfix: {e}"); }
    }

    static void PlayerBoughtPostfix(IItemContainer container, ItemType stackType, int totalPrice,
        IL2List boughtInstances, bool __result)
    {
        try
        {
            if (!__result) return;
            GameEvents.RaisePlayerBought(new PlayerTradeArgs(
                openShop, stackType, boughtInstances?.Count ?? 0, totalPrice, container));
        }
        catch (Exception e) { KitPlugin.L.LogError($"PlayerBoughtPostfix: {e}"); }
    }

    static void PlayerSoldPostfix(IItemContainer container, ItemType itemType, int instanceCount,
        int totalPrice, bool __result)
    {
        try
        {
            if (!__result) return;
            GameEvents.RaisePlayerSold(new PlayerTradeArgs(openShop, itemType, instanceCount, totalPrice, container));
        }
        catch (Exception e) { KitPlugin.L.LogError($"PlayerSoldPostfix: {e}"); }
    }

    static void OnPostLoad()
    {
        try
        {
            // Loading doesn't raise the game's arrival event, so start from here.
            WorldLocation district = null;
            try
            {
                if (Singleton<GameSceneManager>.InstanceExist(out var gsm)) district = gsm.CurrentWorldLocation;
            }
            catch { }
            lastDistrict = district?.Pointer ?? IntPtr.Zero;
            foreach (var attach in reattach)
            {
                try { attach(); }
                catch (Exception e) { KitPlugin.L.LogError($"Re-attach after load: {e.Message}"); }
            }
            GameEvents.RaiseGameLoaded(new GameLoadedArgs(loadingSave, district));
        }
        catch (Exception e) { KitPlugin.L.LogError($"OnPostLoad: {e}"); }
    }

    // StartGame begins loading the world, so the managers that instance events attach to don't
    // exist yet. Retry the re-attach each frame until all succeed (or ~10 s at 60 fps).
    static void StartGamePostfix()
    {
        try
        {
            lastDistrict = IntPtr.Zero;   // the first arrival in the new game is reported
            GameEvents.RaiseNewGameStarted();

            int frames = 0;
            Action retry = null;
            retry = () =>
            {
                bool done = true;
                foreach (var attach in reattach)
                {
                    try { done &= attach(); } catch { done = false; }
                }
                if (done || ++frames > 600) KitLoop.Tick -= retry;
            };
            KitLoop.Tick += retry;
        }
        catch (Exception e) { KitPlugin.L.LogError($"StartGamePostfix: {e}"); }
    }

    static void SavePostfix(string saveName, bool isAutoSave, bool __result)
    {
        try { if (__result) GameEvents.RaiseGameSaved(new GameSavedArgs(saveName, isAutoSave)); }
        catch (Exception e) { KitPlugin.L.LogError($"SavePostfix: {e}"); }
    }

    // The game raises its arrival event for every portal, building doors included, so only
    // pass it on when the district actually changes.
    static IntPtr lastDistrict = IntPtr.Zero;

    static void OnAreaArrival(WorldLocation district)
    {
        try
        {
            if (district == null || district.Pointer == lastDistrict) return;
            lastDistrict = district.Pointer;
            GameEvents.RaiseDistrictEntered(new DistrictEnteredArgs(district));
        }
        catch (Exception e) { KitPlugin.L.LogError($"OnAreaArrival: {e}"); }
    }

    static void CraftPostfix(IRecipe recipe, VenueOrderReference forOrder, MealGhost __result)
    {
        try
        {
            if (__result == null) return;
            VenueAreaGhost area = null;
            try { area = forOrder?.Venue?.RuntimeData; } catch { }
            GameEvents.RaiseDishCooked(new DishCookedArgs(area, recipe, __result));
        }
        catch (Exception e) { KitPlugin.L.LogError($"CraftPostfix: {e}"); }
    }

    static VenueAreaGhost saleArea;

    static void LeaveReviewPrefix(Order order)
    {
        try { saleArea = order?.Venue?.RuntimeData; }
        catch { saleArea = null; }
    }

    static void LeaveReviewPostfix() => saleArea = null;

    static void ReceiptPostfix(ItemType meal, int cost, Person person)
    {
        try { GameEvents.RaiseSaleMade(new SaleMadeArgs(saleArea, meal, cost, person)); }
        catch (Exception e) { KitPlugin.L.LogError($"ReceiptPostfix: {e}"); }
    }

    // Storage counts per delivered item type, taken just before the delivery step runs.
    static VenueAreaGhost deliverArea;
    static readonly Dictionary<IntPtr, (ItemType item, int before)> deliverBefore = new();

    static void DeliverPrefix(DeliverIngredientsActionType.State state)
    {
        deliverArea = null;
        deliverBefore.Clear();
        try
        {
            var task = state?.Task;
            if (task == null || !task.IsCompleted || task.Items == null) return;
            var area = task.venue?.RuntimeData;
            var storage = area?.JointInventory;
            if (storage == null) return;

            foreach (var kv in task.Items)
                if (kv.Key != null)
                    deliverBefore[kv.Key.Pointer] = (kv.Key, storage.GetItemCount(kv.Key));
            deliverArea = area;
        }
        catch (Exception e) { KitPlugin.L.LogError($"DeliverPrefix: {e}"); }
    }

    static void DeliverPostfix(AgentGhost agent)
    {
        try
        {
            if (deliverArea == null || deliverBefore.Count == 0) return;
            var storage = deliverArea.JointInventory;
            var arrived = new Dictionary<ItemType, int>();
            foreach (var (item, before) in deliverBefore.Values)
            {
                int n = storage.GetItemCount(item) - before;
                if (n > 0) arrived[item] = n;
            }
            if (arrived.Count > 0)
                GameEvents.RaiseDeliveryCompleted(new DeliveryCompletedArgs(deliverArea, agent, arrived));
        }
        catch (Exception e) { KitPlugin.L.LogError($"DeliverPostfix: {e}"); }
        finally
        {
            deliverArea = null;
            deliverBefore.Clear();
        }
    }

    static void Install(string ev, Func<Type> type, string method,
        string prefix = null, string postfix = null, int priority = Priority.Normal,
        string[] requires = null, Func<Type[]> args = null)
    {
        attempted++;
        try
        {
            var absent = requires?.Where(r => !GameEvents.Live.Contains(r)).ToList();
            if (absent?.Count > 0) throw new Exception($"needs {string.Join(" and ", absent)}");

            string name = simulated.Contains(ev) ? method + "_SimulatedMissing" : method;
            Type t = type() ?? throw new Exception("target type not found");
            // Parameter types pick one overload and stop a base-class method with the same name
            // matching instead (both happened: ReceiptBase.Init(), AgentSubAction.Enter).
            MethodBase target = AccessTools.Method(t, name, args?.Invoke())
                ?? throw new Exception($"{t.Name}.{name} not found");

            harmony.Patch(target, prefix: Hm(prefix, priority), postfix: Hm(postfix, priority));
            GameEvents.Live.Add(ev);
            KitPlugin.L.LogInfo($"Event {ev}: live");
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Event {ev}: missing ({e.Message})");
        }
    }

    static HarmonyMethod Hm(string name, int priority) =>
        name == null ? null : new HarmonyMethod(typeof(EventPatches), name) { priority = priority };

    // ---------- patch bodies: never throw into game code ----------

    // The venue area inside TryPurchaseIngredients, or zero between restock rounds.
    static IntPtr roundArea = IntPtr.Zero;

    static void BuyStartPrefix(VenueAreaGhost __instance, IRecipe recipe)
    {
        try
        {
            roundArea = __instance?.Pointer ?? IntPtr.Zero;
            GameEvents.RaiseBuyIngredientsStarting(new BuyIngredientsArgs(__instance, recipe, false));
        }
        catch (Exception e) { KitPlugin.L.LogError($"BuyStartPrefix: {e}"); }
    }

    static void BuyFinishPostfix(VenueAreaGhost __instance, IRecipe recipe, bool __result)
    {
        try { GameEvents.RaiseBuyIngredientsFinished(new BuyIngredientsArgs(__instance, recipe, __result)); }
        catch (Exception e) { KitPlugin.L.LogError($"BuyFinishPostfix: {e}"); }
        finally { roundArea = IntPtr.Zero; }
    }

    static void VenueHourPostfix(VenueAreaGhost __instance)
    {
        try { GameEvents.RaiseVenueHour(new VenueHourArgs(__instance)); }
        catch (Exception e) { KitPlugin.L.LogError($"VenueHourPostfix: {e}"); }
    }

    static void PurchasePostfix(VenueAreaGhost __instance, ItemType stackType, int totalPrice,
        IL2List boughtInstances, bool __result)
    {
        try
        {
            if (!__result || __instance == null || __instance.Pointer != roundArea) return;
            GameEvents.RaiseIngredientsPurchased(new IngredientsPurchasedArgs(
                __instance, stackType, boughtInstances?.Count ?? 0, totalPrice));
        }
        catch (Exception e) { KitPlugin.L.LogError($"PurchasePostfix: {e}"); }
    }

    static void EquipmentPostfix(VenueAreaGhost __instance, ItemType stackType, int totalPrice,
        IL2List boughtInstances, bool __result)
    {
        try
        {
            if (!__result || __instance == null || __instance.Pointer == roundArea) return;
            GameEvents.RaiseEquipmentPurchased(new EquipmentPurchasedArgs(
                __instance, stackType, boughtInstances?.Count ?? 0, totalPrice));
        }
        catch (Exception e) { KitPlugin.L.LogError($"EquipmentPostfix: {e}"); }
    }
}
