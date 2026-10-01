using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Nivalis;
using Nivalis.Economy;
using Nivalis.InventorySystem;
using NivalisModKit;

namespace KitTester;

// Logs every kit event. Doubles as the regression test after game updates:
// every kit feature should be exercised here.
[BepInPlugin("bgasm.nivalis.kittester", "Kit Tester", "0.1.0")]
[BepInDependency(ModKit.Guid)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource L;

    static readonly string[] Events =
    {
        nameof(GameEvents.BuyIngredientsStarting),
        nameof(GameEvents.BuyIngredientsFinished),
        nameof(GameEvents.VenueHour),
        nameof(GameEvents.IngredientsPurchased),
        nameof(GameEvents.EquipmentPurchased),
        nameof(GameEvents.DayStarted),
        nameof(GameEvents.HourStarted),
        nameof(GameEvents.NewGameStarted),
        nameof(GameEvents.GameLoaded),
        nameof(GameEvents.GameSaved),
        nameof(GameEvents.DistrictEntered),
        nameof(GameEvents.DishCooked),
        nameof(GameEvents.SaleMade),
        nameof(GameEvents.DeliveryCompleted),
        nameof(GameEvents.ShopOpened),
        nameof(GameEvents.ShopClosed),
        nameof(GameEvents.PlayerBought),
        nameof(GameEvents.PlayerSold),
        nameof(GameEvents.MoneyChanged),
        nameof(GameEvents.QuestStarted),
        nameof(GameEvents.QuestCompleted),
        nameof(GameEvents.QuestFailed),
        nameof(GameEvents.QuestObjectiveStarted),
        nameof(GameEvents.QuestObjectiveCompleted),
        nameof(GameEvents.QuestPinnedChanged),
        nameof(GameEvents.QuestMarkerAdded),
        nameof(GameEvents.QuestMarkerRemoved),
        nameof(GameEvents.VenueSetupQuestUpdated),
        nameof(GameEvents.PlayerCaught),
        nameof(GameEvents.AwarenessIncreased),
        nameof(GameEvents.SecurityLevelChanged),
        nameof(GameEvents.CurfewStarted),
        nameof(GameEvents.CurfewEnded),
        nameof(GameEvents.CurfewWarning),
        nameof(GameEvents.FishCaught),
        nameof(GameEvents.FishDiscovered),
        nameof(GameEvents.CropPlanted),
        nameof(GameEvents.CropHarvested),
        nameof(GameEvents.PropertyOwnerChanged),
        nameof(GameEvents.RentStarted),
        nameof(GameEvents.RentStopped),
        nameof(GameEvents.FurniturePlaced),
        nameof(GameEvents.FurnitureStored),
        nameof(GameEvents.ApartmentEntered),
        nameof(GameEvents.ApartmentLeft),
        nameof(GameEvents.VenueOwnerChanged),
        nameof(GameEvents.StaffHired),
        nameof(GameEvents.StaffFired),
        nameof(GameEvents.StaffPaid),
        nameof(GameEvents.StaffSkillGained),
        nameof(GameEvents.StaffRolesChanged),
        nameof(GameEvents.StaffHoursChanged),
        nameof(GameEvents.TheftCommitted),
        nameof(GameEvents.CameraDisabled),
        nameof(GameEvents.BoatBoarded),
        nameof(GameEvents.BoatLeft),
        nameof(GameEvents.BoatDocked),
        nameof(GameEvents.BoatUndocked),
        nameof(GameEvents.BoatTravel),
        nameof(GameEvents.BoatRefueled),
        nameof(GameEvents.VenueOpened),
        nameof(GameEvents.VenueClosed),
    };

    // City-wide counts since the last HourStarted; the player's venues are logged line by line.
    static int dishes, sales, deliveries, salesTotal;

    static ConfigEntry<string> snapshotItem;
    static ConfigEntry<string> priceItem;
    static ConfigEntry<float> priceMultiplier;
    static string priceItemName;
    static IntPtr priceItemPtr;   // cached: price lookups happen constantly
    static readonly ModSaveData save = SaveData.For("bgasm.nivalis.kittester");
    static int hourUpdates;
    static int skillGains;   // skill gains are frequent; log the first few
    static bool worldChecked;
    static bool pipelineChecked;
    static int bought, skipped, failed;

    public override void Load()
    {
        L = Log;

        // Phase 6 check: set [Snapshot] Print = true in bgasm.nivalis.kittester.cfg while the game
        // runs (live config reload) to print a snapshot. It resets itself to false.
        snapshotItem = Config.Bind("Snapshot", "Item", "Chicken", "Item to list vendors for in the snapshot.");
        var print = Config.Bind("Snapshot", "Print", false, "Set to true to print a snapshot of the query API.");

        // Phase 8 tier 1 check: change Multiplier while the game runs; the item's price at every
        // vendor follows (check with tools/bridge.sh "vendors?item=Chicken").
        priceItem = Config.Bind("Pricing", "Item", "Chicken", "Item whose vendor prices are multiplied.");
        priceMultiplier = Config.Bind("Pricing", "Multiplier", 1.0f, "Price multiplier for that item. 1 = unchanged.");
        Pricing.BuyPrice += ctx =>
        {
            if (priceMultiplier.Value == 1f || ctx.Item == null) return;
            if (priceItemPtr == IntPtr.Zero || priceItemName != priceItem.Value)
            {
                priceItemName = priceItem.Value;
                priceItemPtr = Items.ByName(priceItemName)?.Pointer ?? IntPtr.Zero;
            }
            if (ctx.Item.Pointer == priceItemPtr)
                ctx.Price = (int)Math.Round(ctx.Price * priceMultiplier.Value);
        };
        print.SettingChanged += (_, _) =>
        {
            if (!print.Value) return;
            PrintSnapshot();
            print.Value = false;
        };
        L.LogInfo($"Kit Tester loaded against {ModKit.Name} {ModKit.Version}");

        foreach (string ev in Events)
            L.LogInfo($"IsAvailable({ev}) = {GameEvents.IsAvailable(ev)}");

        CheckStructLayout();
        CheckNativeHook();

        GameEvents.BuyIngredientsStarting += a =>
        {
            if (!pipelineChecked) { pipelineChecked = true; L.LogInfo($"Purchasing.IsAvailable = {Purchasing.IsAvailable}"); }
            bought = skipped = failed = 0;
            L.LogInfo($"BuyIngredientsStarting: {NameOf(a.Area?.Venue)} / {RecipeName(a)}");
        };
        GameEvents.BuyIngredientsFinished += a =>
            L.LogInfo($"BuyIngredientsFinished: {NameOf(a.Area?.Venue)} / {RecipeName(a)} bought={a.Bought} " +
                      $"(pipeline: {bought} bought, {skipped} skipped, {failed} failed)");

        // Read-only; doesn't turn the pipeline on. Counts per recipe, shown on the Finished line.
        Purchasing.Decision += d =>
        {
            if (d.Result == PurchaseResult.Bought) bought++;
            else if (d.Result == PurchaseResult.Skipped) skipped++;
            else failed++;
        };
        GameEvents.IngredientsPurchased += a =>
            L.LogInfo($"IngredientsPurchased: {NameOf(a.Area?.Venue)} / {NameOf(a.Item)} x{a.Count} for {a.TotalPrice}");
        GameEvents.VenueHour += OnVenueHour;
        GameEvents.EquipmentPurchased += a =>
            L.LogInfo($"EquipmentPurchased: {NameOf(a.Area?.Venue)} / {NameOf(a.Item)} x{a.Count} for {a.TotalPrice}");
        GameEvents.DayStarted += a => L.LogInfo($"DayStarted: day {a.Day}, {a.DayOfWeek}");
        GameEvents.HourStarted += a =>
        {
            L.LogInfo($"HourStarted: day {a.Day}, {a.Hour:00}:00 (last hour, city: {dishes} dishes, " +
                      $"{sales} sales for {salesTotal}, {deliveries} deliveries)");
            dishes = sales = deliveries = salesTotal = 0;
        };

        GameEvents.NewGameStarted += () => { L.LogInfo("NewGameStarted"); CheckPhase7(); };
        GameEvents.GameLoaded += a =>
        {
            L.LogInfo($"GameLoaded: {a.SaveName ?? "?"} in {World.NameOf(a.District)}");
            CheckPhase7();
        };
        SaveData.Saving += () =>
        {
            save.Set("lastSaved", $"day {GameTime.Day} {GameTime.Hour:00}:{GameTime.Minute:00}");
            L.LogInfo($"SaveData: storing loads={save.Get("loads", 0)}, lastSaved={save.Get<string>("lastSaved")}");
        };
        GameEvents.GameSaved += a => L.LogInfo($"GameSaved: {a.SaveName} autosave={a.IsAutoSave}");
        GameEvents.DistrictEntered += a => L.LogInfo($"DistrictEntered: {World.NameOf(a.District)}");

        GameEvents.DishCooked += a =>
        {
            dishes++;
            if (a.Area != null && a.Area.PlayerOwned)
                L.LogInfo($"DishCooked: {NameOf(a.Area.Venue)} / {NameOf(a.Meal?.Type)} failed={a.Meal?.IsFailed}");
        };
        GameEvents.SaleMade += a =>
        {
            sales++;
            salesTotal += a.Price;
            if (a.Area == null || a.Area.PlayerOwned)
                L.LogInfo($"SaleMade: {(a.Area == null ? "vending machine" : NameOf(a.Area.Venue))} / " +
                          $"{NameOf(a.Meal)} for {a.Price} to {NameOf(a.Customer)}");
        };
        GameEvents.QuestStarted += a => L.LogInfo($"QuestStarted: {a.Title} [{a.Id}]");
        GameEvents.QuestCompleted += a => L.LogInfo($"QuestCompleted: {a.Title} [{a.Id}]");
        GameEvents.QuestFailed += a => L.LogInfo($"QuestFailed: {a.Title} [{a.Id}]");
        GameEvents.QuestObjectiveStarted += a => L.LogInfo($"QuestObjectiveStarted: {a.Quest.Title} / {a.Text} [{a.Id}]");
        GameEvents.QuestObjectiveCompleted += a => L.LogInfo($"QuestObjectiveCompleted: {a.Quest.Title} / {a.Text} [{a.Id}]");
        GameEvents.QuestPinnedChanged += a => L.LogInfo($"QuestPinnedChanged: {a.Quest.Title} pinned={a.Pinned}");
        GameEvents.QuestMarkerAdded += a => L.LogInfo($"QuestMarkerAdded: {a.PointId} scene {a.SceneIndex} for {a.Quest.Title}");
        GameEvents.QuestMarkerRemoved += a => L.LogInfo($"QuestMarkerRemoved: {a.PointId} scene {a.SceneIndex} for {a.Quest.Title}");
        GameEvents.VenueSetupQuestUpdated += a => L.LogInfo($"VenueSetupQuestUpdated: {a.Title} state={a.State}");
        // Phase 9 batch (a)
        GameEvents.PlayerCaught += () => L.LogInfo("PlayerCaught");
        GameEvents.AwarenessIncreased += a => L.LogInfo($"AwarenessIncreased: +{a.Delta:0.##} -> {a.Awareness:0.##}");
        GameEvents.SecurityLevelChanged += a => L.LogInfo($"SecurityLevelChanged: {World.NameOf(a.District)} -> {a.Level}");
        GameEvents.CurfewStarted += () => L.LogInfo($"CurfewStarted at {GameTime.Hour:00}:{GameTime.Minute:00}");
        GameEvents.CurfewEnded += () => L.LogInfo($"CurfewEnded at {GameTime.Hour:00}:{GameTime.Minute:00}");
        GameEvents.CurfewWarning += () => L.LogInfo($"CurfewWarning at {GameTime.Hour:00}:{GameTime.Minute:00}");
        GameEvents.FishCaught += a => L.LogInfo($"FishCaught: {NameOf(a.Item)}");
        GameEvents.FishDiscovered += a => L.LogInfo($"FishDiscovered: {NameOf(a.Item)}");
        GameEvents.CropPlanted += a => L.LogInfo($"CropPlanted: {NameOf(a.Item)}");
        GameEvents.CropHarvested += a => L.LogInfo($"CropHarvested: {NameOf(a.Plant)} firstTime={a.FirstTime}");
        GameEvents.PropertyOwnerChanged += a => L.LogInfo($"PropertyOwnerChanged: {NameOf(a.Property)} playerOwned={a.PlayerOwned}");
        GameEvents.RentStarted += a => L.LogInfo($"RentStarted: {NameOf(a.Property)}");
        GameEvents.RentStopped += a => L.LogInfo($"RentStopped: {NameOf(a.Property)}");
        GameEvents.FurniturePlaced += a => L.LogInfo($"FurniturePlaced: {NameOf(a.Entity)}");
        GameEvents.FurnitureStored += a => L.LogInfo($"FurnitureStored: {NameOf(a.Entity)}");
        GameEvents.ApartmentEntered += a => L.LogInfo($"ApartmentEntered: {NameOf(a.Apartment)}");
        GameEvents.ApartmentLeft += a => L.LogInfo($"ApartmentLeft: {NameOf(a.Apartment)}");
        GameEvents.VenueOwnerChanged += a => L.LogInfo($"VenueOwnerChanged: {NameOf(a.Venue)} playerOwned={a.PlayerOwned}");
        // Phase 9 batch (b)
        GameEvents.StaffHired += a => L.LogInfo($"StaffHired: {NameOf(a.Person)} at {NameOf(a.Venue)}");
        GameEvents.StaffFired += a => L.LogInfo($"StaffFired: {NameOf(a.Person)} at {NameOf(a.Venue)}");
        GameEvents.StaffPaid += a => { if (a.Area != null && a.Area.PlayerOwned) L.LogInfo($"StaffPaid: {NameOf(a.Person)} {a.Wage:0}"); };
        GameEvents.StaffSkillGained += a => { if (++skillGains <= 5) L.LogInfo($"StaffSkillGained: {NameOf(a.Person)} {NameOf(a.Skill)} +{a.Amount:0.###}"); };
        GameEvents.StaffRolesChanged += a => L.LogInfo($"StaffRolesChanged: {NameOf(a.Venue)} -> {a.Roles}");
        GameEvents.StaffHoursChanged += a => L.LogInfo($"StaffHoursChanged: {NameOf(a.Area?.Venue)}");
        GameEvents.TheftCommitted += a => L.LogInfo($"TheftCommitted: {NameOf(a.Furniture)} at {NameOf(a.Area?.Venue)}");
        GameEvents.CameraDisabled += a => L.LogInfo($"CameraDisabled: {NameOf(a.Camera)}");
        GameEvents.BoatBoarded += () => L.LogInfo("BoatBoarded");
        GameEvents.BoatLeft += () => L.LogInfo("BoatLeft");
        GameEvents.BoatDocked += a => L.LogInfo($"BoatDocked: {NameOf(a.Dock)}");
        GameEvents.BoatUndocked += a => L.LogInfo($"BoatUndocked: {NameOf(a.Dock)}");
        GameEvents.BoatTravel += a => L.LogInfo($"BoatTravel: to {NameOf(a.Destination)}");
        GameEvents.BoatRefueled += a => L.LogInfo($"BoatRefueled: +{a.FuelAdded:0.##} -> {a.Fuel:0.##}");
        GameEvents.VenueOpened += a => { if (a.Area != null && a.Area.PlayerOwned) L.LogInfo($"VenueOpened: {NameOf(a.Area.Venue)} at {GameTime.Hour:00}:00"); };
        GameEvents.VenueClosed += a => { if (a.Area != null && a.Area.PlayerOwned) L.LogInfo($"VenueClosed: {NameOf(a.Area.Venue)} at {GameTime.Hour:00}:00"); };

        // Phase 9 batch (c): tuning multipliers, live-reloadable. 1 = unchanged (handler does nothing).
        var fishMul = Config.Bind("Tuning", "FishYieldMultiplier", 1f, "Multiply fish yield.");
        var cropMul = Config.Bind("Tuning", "CropYieldMultiplier", 1f, "Multiply crop yield.");
        var growMul = Config.Bind("Tuning", "CropGrowthMultiplier", 1f, "Multiply crop growth speed.");
        var propMul = Config.Bind("Tuning", "PropertyPriceMultiplier", 1f, "Multiply property purchase prices.");
        var awareMul = Config.Bind("Tuning", "AwarenessGainMultiplier", 1f, "Multiply security awareness gains (0 = never noticed).");
        Tuning.FishYield += c => { if (fishMul.Value != 1f) { c.Yield = (int)Math.Round(c.Yield * fishMul.Value); L.LogInfo($"Tuning.FishYield: {NameOf(c.Item)} {c.GameYield} -> {c.Yield}"); } };
        Tuning.CropYield += c => { if (cropMul.Value != 1f) { c.Yield = (int)Math.Round(c.Yield * cropMul.Value); L.LogInfo($"Tuning.CropYield: {NameOf(c.Plant)} {c.GameYield} -> {c.Yield}"); } };
        Tuning.CropGrowthSpeed += c => { if (growMul.Value != 1f) c.Speed *= growMul.Value; };
        Tuning.PropertyPrice += c => { if (propMul.Value != 1f) c.Price = (int)Math.Round(c.Price * propMul.Value); };
        Tuning.AwarenessGain += c => { if (awareMul.Value != 1f) { c.Amount *= awareMul.Value; L.LogInfo($"Tuning.AwarenessGain: {c.GameAmount:0.##} -> {c.Amount:0.##}"); } };
        GameEvents.MoneyChanged += a => L.LogInfo($"MoneyChanged: {a.Old} -> {a.New} ({a.Delta:+#;-#;0})");
        GameEvents.ShopOpened += a => L.LogInfo($"ShopOpened: {NameOf(a.Vendor)}");
        GameEvents.ShopClosed += a => L.LogInfo($"ShopClosed: {NameOf(a.Vendor)}");
        GameEvents.PlayerBought += a =>
            L.LogInfo($"PlayerBought: {NameOf(a.Item)} x{a.Count} for {a.TotalPrice} at {NameOf(a.Vendor)}");
        GameEvents.PlayerSold += a =>
            L.LogInfo($"PlayerSold: {NameOf(a.Item)} x{a.Count} for {a.TotalPrice} at {NameOf(a.Vendor)}");

        GameEvents.DeliveryCompleted += a =>
        {
            deliveries++;
            if (a.Area != null && a.Area.PlayerOwned)
                L.LogInfo($"DeliveryCompleted: {NameOf(a.Area.Venue)} by {NameOf(a.Staff)}: " +
                          string.Join(", ", a.Items.Select(kv => $"{NameOf(kv.Key)} x{kv.Value}")));
        };
    }

    // VenueHour fires for every venue in the world, so only the player's are logged,
    // with a count of all updates to show the rest are firing.
    static void OnVenueHour(VenueHourArgs a)
    {
        hourUpdates++;
        if (!worldChecked) { worldChecked = true; CheckWorld(); }
        if (a.Area == null || !a.Area.PlayerOwned) return;

        L.LogInfo($"VenueHour: {NameOf(a.Area.Venue)} ({hourUpdates} venue updates so far)");
    }

    // ---------- helper checks ----------

    // Offsets should match Order Fix 1.0's verbose "Hooked BuyItem" line.
    static void CheckStructLayout()
    {
        try
        {
            L.LogInfo("StructLayout ShopTradeRequest: " +
                      $"customer={StructLayout.FieldOffset<ShopTradeRequest>("customer")} " +
                      $"itemType={StructLayout.FieldOffset<ShopTradeRequest>("itemType")} " +
                      $"freshness={StructLayout.FieldOffset<ShopTradeRequest>("freshness")} " +
                      $"amount={StructLayout.FieldOffset<ShopTradeRequest>("amount")} " +
                      $"size={StructLayout.Size<ShopTradeRequest>()} " +
                      $"valueType={StructLayout.IsValueType<ShopTradeRequest>()}");
            L.LogInfo("StructLayout BasicTemp: " +
                      $"StackCount={StructLayout.FieldOffset<ItemStack.BasicTemp>("StackCount")} " +
                      $"size={StructLayout.Size<ItemStack.BasicTemp>()}");
        }
        catch (Exception e) { L.LogError($"StructLayout check failed: {e.Message}"); }

        try
        {
            StructLayout.FieldOffset<ShopTradeRequest>("noSuchField");
            L.LogError("StructLayout: missing field did not throw");
        }
        catch (MissingFieldException) { L.LogInfo("StructLayout: missing field throws as expected"); }
    }

    static void CheckNativeHook()
    {
        try
        {
            IntPtr p = NativeHook.MethodPointer<Vendor>(
                "NativeMethodInfoPtr_BuyItem_Public_Void_byref_ShopTradeRequest_Single_byref_BasicTemp_0");
            L.LogInfo($"NativeHook.MethodPointer(Vendor.BuyItem) = 0x{p.ToInt64():X}");
        }
        catch (Exception e) { L.LogError($"NativeHook full-name lookup failed: {e.Message}"); }

        try
        {
            NativeHook.MethodPointer<Vendor>("BuyItem");
            L.LogError("NativeHook: overloaded short name did not throw");
        }
        catch (System.Reflection.AmbiguousMatchException e)
        {
            L.LogInfo($"NativeHook: short name is ambiguous as expected ({e.Message.Split(',')[0]})");
        }
        catch (Exception e) { L.LogError($"NativeHook short-name lookup: unexpected {e.GetType().Name}: {e.Message}"); }
    }

    // Expected from Meridian Market: Docks 1, Calypso Island 5.
    static void CheckWorld()
    {
        try
        {
            var from = World.Find("Meridian Market");
            L.LogInfo($"World: {World.Locations.Count} districts, Find(\"Meridian Market\") = {World.NameOf(from)}");
            if (from == null) return;

            var hops = World.Locations
                .Select(l => (name: World.NameOf(l), hops: World.Hops(from, l)))
                .OrderBy(x => x.hops).ThenBy(x => x.name)
                .Select(x => $"{x.name} {x.hops}");
            L.LogInfo($"World.Hops from Meridian Market: {string.Join(", ", hops)}");
        }
        catch (Exception e) { L.LogError($"World check failed: {e}"); }
    }

    // ---------- Phase 6: query API snapshot ----------

    static void PrintSnapshot()
    {
        try
        {
            L.LogInfo($"Snapshot: day {GameTime.Day} {GameTime.Hour:00}:{GameTime.Minute:00} {GameTime.DayOfWeek}, " +
                      $"money {NivalisModKit.Economy.PlayerMoney}, {Venues.All.Count} venues, " +
                      $"{NivalisModKit.Economy.Vendors.Count} vendors, {Items.All.Count} items, " +
                      $"{Recipes.Known.Count}/{Recipes.All.Count} recipes known");

            var quests = Quests.Active;
            L.LogInfo($"Snapshot: {quests.Count} active quests, pinned: {Quests.Pinned?.Quest?.Title ?? "none"}");

            // Ingredients the player's known recipes use.
            var ingredients = Recipes.Known.SelectMany(Recipes.InputsOf).Select(i => i.Item)
                .GroupBy(i => i.Pointer).Select(g => g.First()).OrderBy(Items.NameOf).ToList();

            foreach (var area in Venues.PlayerOwned)
            {
                var stock = ingredients.Select(i => $"{Items.NameOf(i)} {Venues.Stock(area, i)}");
                L.LogInfo($"Snapshot: {Venues.NameOf(area)} in {World.NameOf(Venues.DistrictOf(area))}: " +
                          string.Join(", ", stock));
            }

            var item = Items.ByName(snapshotItem.Value);
            if (item == null) { L.LogInfo($"Snapshot: no item named {snapshotItem.Value}"); return; }
            var home = Venues.PlayerOwned.Select(Venues.DistrictOf).FirstOrDefault(d => d != null);
            var vendors = NivalisModKit.Economy.VendorsFor(item)
                .OrderBy(v => NivalisModKit.Economy.Price(v, item) ?? int.MaxValue).ToList();
            L.LogInfo($"Snapshot: {vendors.Count} vendors for {Items.NameOf(item)}" +
                      (home == null ? "" : $", hops from {World.NameOf(home)}"));
            foreach (var v in vendors)
                L.LogInfo($"Snapshot:   {NameOf(v)} ({World.NameOf(NivalisModKit.Economy.DistrictOf(v))}" +
                          (home == null ? "" : $", {World.Hops(home, NivalisModKit.Economy.DistrictOf(v))} hops") +
                          $") price {NivalisModKit.Economy.Price(v, item)}, stock {NivalisModKit.Economy.Stock(v, item)}" +
                          (NivalisModKit.Economy.IsUnlocked(v) ? "" : ", locked"));
        }
        catch (Exception e) { L.LogError($"Snapshot failed: {e}"); }
    }

    // ---------- Phase 7: per-save data and scheduler ----------

    // What this save remembers from earlier sessions, then count this load. After saving and
    // loading the same save, loads and lastSaved should come back.
    static void CheckPhase7()
    {
        int loads = save.Get("loads", 0);
        L.LogInfo($"SaveData: read loads={loads}, lastSaved={save.Get<string>("lastSaved") ?? "none"}, " +
                  $"keys=[{string.Join(", ", save.Keys)}]");
        save.Set("loads", loads + 1);

        string Now() => $"day {GameTime.Day} {GameTime.Hour:00}:{GameTime.Minute:00}";
        L.LogInfo($"Scheduler: queued at {Now()}");
        Scheduler.NextFrame(() => L.LogInfo($"Scheduler: NextFrame ran at {Now()}"));
        Scheduler.AfterGameHours(0.5f, () => L.LogInfo($"Scheduler: AfterGameHours(0.5) ran at {Now()}"));
        int nextHour = (GameTime.Hour + 1) % 24;
        Scheduler.AtHour(nextHour, () => L.LogInfo($"Scheduler: AtHour({nextHour}) ran at {Now()}"));
        Scheduler.AfterDays(1, () => L.LogInfo($"Scheduler: AfterDays(1) ran at {Now()}"));
    }

    // ---------- names ----------

    static string RecipeName(BuyIngredientsArgs a)
    {
        try { return NameOf(a.Recipe?.Output.type); }
        catch { return "?"; }
    }

    // Unity objects print as "name (Type)"; keep the name.
    static string NameOf(Il2CppSystem.Object o)
    {
        if (o == null) return "?";
        try
        {
            string s = o.ToString();
            int i = s.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : s;
        }
        catch { return "?"; }
    }
}
