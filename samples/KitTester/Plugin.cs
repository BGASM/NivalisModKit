using System;
using System.Collections.Generic;
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
        nameof(GameEvents.FurniturePickedUp),
        nameof(GameEvents.DayEnded),
        nameof(GameEvents.EndOfDayShown),
        nameof(GameEvents.PanelShown),
        nameof(GameEvents.VenueStorageChanged),
        nameof(GameEvents.PanelHidden),
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

        // Off: subscribe to nothing, so no tuning patches install and nothing is logged.
        if (!Config.Bind("General", "Enabled", true, "Run the kit tester (restart to apply).").Value)
        {
            L.LogInfo("Disabled ([General] Enabled = false)");
            return;
        }

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
        // Phase 10 check: set [Ui] Notify or Dialog = true while the game runs (live reload); each
        // resets itself to false.
        var uiNotify = Config.Bind("Ui", "Notify", false, "Set to true to show a test notification (Ui.Notify).");
        var uiDialog = Config.Bind("Ui", "Dialog", false, "Set to true to show a test dialog with two buttons (Ui.Dialog).");
        uiNotify.SettingChanged += (_, _) =>
        {
            if (!uiNotify.Value) return;
            L.LogInfo($"Ui.Notify -> {Ui.Notify("Kit Tester", $"Test notification at {GameTime.Hour:00}:{GameTime.Minute:00}")}");
            uiNotify.Value = false;
        };
        uiDialog.SettingChanged += (_, _) =>
        {
            if (!uiDialog.Value) return;
            // Step 1 stays open; Next replaces it in place with step 2, whose Done closes it.
            bool shown = Ui.Dialog("Kit Tester", "Step 1 of 2: multi-step dialog test. Stay stays open; Next shows step 2.", false,
                ("Stay", () => L.LogInfo("Ui.Dialog: Stay clicked (dialog should stay open)")),
                ("Next", () =>
                {
                    L.LogInfo("Ui.Dialog: Next clicked");
                    bool replaced = Ui.Dialog("Kit Tester", "Step 2 of 2: replaced in place. Done closes it.",
                        ("Done", () => L.LogInfo("Ui.Dialog: Done clicked (dialog should close)")));
                    L.LogInfo($"Ui.Dialog step 2 -> {replaced}");
                }));
            L.LogInfo($"Ui.Dialog -> {shown}");
            uiDialog.Value = false;
        };
        // [Ui] CloneInto = a panel type name (see the bridge's /ui): the next time it opens, its first
        // active button is copied as "Kit Test", which logs when clicked. Once per panel per session.
        var cloneInto = Config.Bind("Ui", "CloneInto", "", "Panel to add a test button to (Ui.CloneButton): Type or Type:GameObject. Empty = off.");
        var cloned = new HashSet<IntPtr>();
        GameEvents.PanelShown += a =>
        {
            L.LogInfo($"PanelShown: {a.Name} ({a.Panel?.gameObject?.name})");
            // "Type" or "Type:GameObject" (e.g. MainMenuUI:P_PauseMenuUI = the Escape menu, not the start menu).
            var want = cloneInto.Value.Split(':');
            if (cloneInto.Value == "" || a.Name != want[0] || a.Panel == null) return;
            if (want.Length > 1 && a.Panel.gameObject.name != want[1]) return;
            if (!cloned.Add(a.Panel.Pointer)) return;
            var template = Ui.ButtonsIn(a.Panel).FirstOrDefault(b => b.button.gameObject.activeInHierarchy);
            if (template.button == null) { L.LogInfo($"Ui.CloneButton: no active button in {a.Name}"); return; }
            var go = Ui.CloneButton(template.button, "Kit Test", () => L.LogInfo("Ui.CloneButton: Kit Test clicked"));
            L.LogInfo($"Ui.CloneButton: copied {template.path} ('{template.label}') -> {(go != null ? go.name : "failed")}");
            L.LogInfo($"Ui.Tooltip on Kit Test -> {Ui.Tooltip(go, "Added by the Nivalis ModKit (Ui.Tooltip)")}");
            LogRect("template", template.button.gameObject);
            if (go != null) Scheduler.NextFrame(() => LogRect("copy", go));   // after layout has run
        };
        GameEvents.PanelHidden += a => L.LogInfo($"PanelHidden: {a.Name}");

        // [Ui] Radial = true: opens the game's radial wheel with two kit actions (resets itself).
        // [Ui] RadialAdd = true: when the game opens the wheel (greenhouse module), adds a kit action.
        var uiRadial = Config.Bind("Ui", "Radial", false, "Set to true to open the radial wheel with test actions (Ui.RadialMenu).");
        var uiRadialAdd = Config.Bind("Ui", "RadialAdd", false, "Add a test action whenever the game opens the radial wheel (Ui.AddRadialAction).");
        uiRadial.SettingChanged += (_, _) =>
        {
            if (!uiRadial.Value) return;
            bool ok = Ui.RadialMenu(("Kit A", () => L.LogInfo("Ui.RadialMenu: Kit A chosen")),
                                    ("Kit B", () => L.LogInfo("Ui.RadialMenu: Kit B chosen")));
            L.LogInfo($"Ui.RadialMenu -> {ok}");
            uiRadial.Value = false;
        };
        GameEvents.PanelShown += a =>
        {
            if (a.Name != "RadialMenuUI" || !uiRadialAdd.Value) return;
            Scheduler.NextFrame(() =>   // after the game has added its own actions
                L.LogInfo($"Ui.AddRadialAction -> {Ui.AddRadialAction("Kit Extra", () => L.LogInfo("Ui.AddRadialAction: Kit Extra chosen"))}"));
        };

        // [Ui] Demo = Popup or Panel: opens a Ui.CreateWindow demo in that style, one of each component
        // (resets itself). The window logs what you do with it.
        var uiDemo = Config.Bind("Ui", "Demo", "", "Open a Ui.CreateWindow demo: Popup or Panel (resets itself).");
        var demos = new Dictionary<WindowStyle, KitWindow>();
        uiDemo.SettingChanged += (_, _) =>
        {
            string what = uiDemo.Value.Trim();
            if (what == "") return;
            uiDemo.Value = "";
            if (!Enum.TryParse<WindowStyle>(what, true, out var style)) { L.LogInfo($"Ui demo: unknown style '{what}' (Popup or Panel)"); return; }
            if (demos.TryGetValue(style, out var old) && old.Root != null) { old.Show(); L.LogInfo($"Ui.CreateWindow {style} demo: shown again"); return; }
            var w = Ui.CreateWindow($"ModKit {style} demo", style);
            if (w == null) { L.LogInfo($"Ui.CreateWindow({style}) -> null (load a save first)"); return; }
            demos[style] = w;
            w.AddText($"A {style.ToString().ToLower()} window built at runtime from the game's own parts, " +
                      "with rows added by the Nivalis ModKit.");
            var toggle = w.AddToggle("Example toggle", true, v => L.LogInfo($"{style} demo toggle -> {v}"));
            var slider = w.AddSlider("Example slider", 0f, 10f, 5f, v => L.LogInfo($"{style} demo slider -> {v:0.0}"), "0.0");
            w.AddButton("Show a notification", () => Ui.Notify($"{style} demo", "Notification from a kit window"));
            w.AddButton("Open a dialog", () => Ui.Dialog($"{style} demo", "A dialog opened from a kit window.", ("OK", null)));
            var tip = w.AddButton("Hover me (tooltip)", () => L.LogInfo($"{style} demo: tooltip button clicked"));
            Ui.Tooltip(tip, "A tooltip from Ui.Tooltip inside a kit window");
            if (style == WindowStyle.Panel)
            {
                w.AddText("Panels scroll when their rows don't fit. A few more rows to show it:");
                w.AddSlider("Whole numbers slider", 1f, 20f, 3f, v => L.LogInfo($"Panel demo whole slider -> {v}"), "0", wholeNumbers: true);
                w.AddToggle("Second toggle", false, v => L.LogInfo($"Panel demo toggle 2 -> {v}"));
                w.AddSlider("Percent slider", 0f, 1f, 0.25f, v => L.LogInfo($"Panel demo percent -> {v:0%}"), "0%");
                for (int i = 1; i <= 4; i++) { int n = i; w.AddButton($"List button {n}", () => L.LogInfo($"Panel demo list button {n}")); }
            }
            w.AddFooterButton("Close", w.Hide);
            w.Closed += () => L.LogInfo($"Ui.CreateWindow {style} demo: closed");
            w.Show();
            L.LogInfo($"Ui.CreateWindow {style} demo: shown (toggle {(toggle != null ? "ok" : "missing")}, slider {(slider != null ? "ok" : "missing")})");
        };

        // [Time] ClockSpeed / SimulationSpeed: GameClock factors under KitTester's name (1 = off). Live.
        var clockSpeed = Config.Bind("Time", "ClockSpeed", 1f, "GameClock.SetClockSpeed factor (2 = days pass twice as fast). 1 = off.");
        var simSpeed = Config.Bind("Time", "SimulationSpeed", 1f, "GameClock.SetSimulationSpeed factor (2 = everything twice as fast). 1 = off.");
        void ApplyTime()
        {
            if (clockSpeed.Value == 1f) GameClock.ClearClockSpeed("bgasm.nivalis.kittester"); else GameClock.SetClockSpeed("bgasm.nivalis.kittester", clockSpeed.Value);
            if (simSpeed.Value == 1f) GameClock.ClearSimulationSpeed("bgasm.nivalis.kittester"); else GameClock.SetSimulationSpeed("bgasm.nivalis.kittester", simSpeed.Value);
        }
        clockSpeed.SettingChanged += (_, _) => ApplyTime();
        simSpeed.SettingChanged += (_, _) => ApplyTime();
        ApplyTime();
        GameClock.TimeSpeedChanged += () => L.LogInfo($"TimeSpeedChanged: clock x{GameClock.ClockSpeed:0.##}, simulation x{GameClock.SimulationSpeed:0.##}");

        // [Ui] Open = Map, Venue, or an in-game menu tab (Inventory, Journal, Characters, Skills,
        // Achievements, Recipes, FishDatabase): opens that screen (resets itself).
        var uiOpen = Config.Bind("Ui", "Open", "", "Screen to open: Map, Venue, or a menu tab name (Ui.OpenMap/OpenVenue/OpenMenu).");
        uiOpen.SettingChanged += (_, _) =>
        {
            string what = uiOpen.Value.Trim();
            if (what == "") return;
            bool ok;
            if (what == "Map") ok = Ui.OpenMap();
            else if (what == "Venue") ok = Ui.OpenVenue(Venues.PlayerOwned.FirstOrDefault());
            else ok = Enum.TryParse<Nivalis.UI.InGameMenu.InGameMenuTab>(what, out var tab) && Ui.OpenMenu(tab);
            L.LogInfo($"Ui.Open {what} -> {ok}");
            uiOpen.Value = "";
        };

        // [Ui] HudText = a panel type name (e.g. WorldStateDisplayUI): copies its first active text label
        // as a kit label showing the game time, updated hourly. Once per session. (No tooltip: the HUD has no cursor.)
        var uiHud = Config.Bind("Ui", "HudText", "", "Panel to add a kit text label to (Ui.CloneText + Ui.Tooltip). Empty = off.");
        TMPro.TMP_Text hudLabel = null;
        void AddHudLabel()
        {
            if (hudLabel != null || uiHud.Value == "") return;
            var panel = Ui.Find(uiHud.Value);
            var template = panel == null ? default : Ui.TextsIn(panel).FirstOrDefault(t => t.text.gameObject.activeInHierarchy);
            if (template.text == null) { L.LogInfo($"Ui.CloneText: no active text in {uiHud.Value}"); return; }
            hudLabel = Ui.CloneText(template.text, $"Kit {GameTime.Hour:00}:00", Ui.Below(template.text.gameObject));   // not on top of the template
            L.LogInfo($"Ui.CloneText: copied {template.path} ('{template.value}') -> {(hudLabel != null ? "ok" : "failed")}");

        }
        uiHud.SettingChanged += (_, _) => AddHudLabel();
        GameEvents.GameLoaded += _ => Scheduler.NextFrame(AddHudLabel);
        GameEvents.HourStarted += _ => { if (hudLabel != null) hudLabel.text = $"Kit {GameTime.Hour:00}:00"; };
        GameEvents.VenueStorageChanged += a =>
        {
            if (a.Area == null || !a.Area.PlayerOwned) return;
            var st = Venues.StorageOf(a.Area);
            L.LogInfo($"VenueStorageChanged: {Venues.NameOf(a.Area)} {(a.Added ? "+" : "-")}{NameOf(a.Furniture)} " +
                      $"(normal {a.Normal}, fridge {a.Refrigerated}) -> capacity {st?.NormalCapacity} / {st?.RefrigeratedCapacity}");
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
        GameEvents.PlayerCaught += a => L.LogInfo($"PlayerCaught: byDrone={a.ByDrone} in {World.NameOf(a.District)}, security {a.SecurityLevel}");
        var letOff = Config.Bind("Tuning", "CancelCatch", false, "Cancel being caught (Tuning.Catch).");
        var letOffTo = Config.Bind("Tuning", "CancelCatchAwareness", Tuning.DefaultAwarenessAfterCancel,
            "Awareness after a cancelled catch (0 to 0.99).");
        int cancels = 0;
        Tuning.Catch += c =>
        {
            if (!letOff.Value) return;
            c.Cancel = true;
            c.AwarenessAfterCancel = letOffTo.Value;
            if (++cancels == 1 || cancels % 25 == 0)   // a drone overhead can cancel many times a minute
                L.LogInfo($"Tuning.Catch: cancelled #{cancels} (byDrone={c.ByDrone}), awareness -> {c.AwarenessAfterCancel:0.##}");
        };
        // Awareness ticks every frame while the player is watched: log only each new tenth reached.
        int awareTenth = -1;
        GameEvents.AwarenessIncreased += a =>
        {
            int tenth = (int)(a.Awareness * 10);
            if (tenth == awareTenth) return;
            awareTenth = tenth;
            L.LogInfo($"AwarenessIncreased: +{a.Delta:0.####} -> {a.Awareness:0.####}");
        };
        GameEvents.SecurityLevelChanged += a => L.LogInfo($"SecurityLevelChanged: {World.NameOf(a.District)} -> {a.Level}");
        GameEvents.CurfewStarted += () => L.LogInfo($"CurfewStarted at {GameTime.Hour:00}:{GameTime.Minute:00} " +
            $"(Security: curfew={Security.IsCurfew} active={Security.IsSecurityActive} level={Security.Level} awareness={Security.Awareness:0.##})");
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
        GameEvents.StaffPaid += a => { if (a.Area != null && a.Area.PlayerOwned) L.LogInfo($"StaffPaid: {NameOf(a.Person)} wage {a.Wage} paid={a.Paid}"); };
        GameEvents.FurniturePickedUp += a => L.LogInfo($"FurniturePickedUp: {NameOf(a.Entity)}");
        GameEvents.DayEnded += a => L.LogInfo($"DayEnded: day {a.Day} at {GameTime.Hour:00}:{GameTime.Minute:00}");
        GameEvents.EndOfDayShown += () => L.LogInfo($"EndOfDayShown at {GameTime.Hour:00}:{GameTime.Minute:00}");
        GameEvents.StaffSkillGained += a => { if (++skillGains <= 5) L.LogInfo($"StaffSkillGained: {NameOf(a.Person)} {NameOf(a.Skill)} +{a.Amount:0.###}"); };
        GameEvents.StaffRolesChanged += a => L.LogInfo($"StaffRolesChanged: {NameOf(a.Venue)} -> {a.Roles}");
        GameEvents.StaffHoursChanged += a => L.LogInfo($"StaffHoursChanged: {NameOf(a.Venue)} {a.Before.x:0}-{a.Before.y:0} -> {a.After.x:0}-{a.After.y:0}");
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
        // Tuning.UseOrder check: skip spoiled items while usable ones remain (what Use Oldest First does).
        // Subscribing installs the hook, so only subscribe when the option is on at startup.
        if (Config.Bind("Tuning", "SkipSpoiled", false, "Never use spoiled items ahead of usable ones (Tuning.UseOrder). Restart to apply.").Value)
        {
            int orders = 0;
            Tuning.UseOrder += ctx =>
            {
                ctx.Sort((a, b) =>
                {
                    bool sa = a.remainingDecayTime <= 0, sb = b.remainingDecayTime <= 0;
                    if (sa != sb) return sa ? 1 : -1;
                    return a.remainingDecayTime.CompareTo(b.remainingDecayTime);
                });
                if (++orders == 1 || orders % 200 == 0)
                    L.LogInfo($"Tuning.UseOrder #{orders}: {NameOf(ctx.Item)} x{ctx.Count} of {ctx.Items.Count}, " +
                              $"first {ctx.Items[0].remainingDecayTime}, last {ctx.Items[ctx.Items.Count - 1].remainingDecayTime}");
            };
        }
        var awareMul = Config.Bind("Tuning", "AwarenessGainMultiplier", 1f, "Multiply security awareness gains (0 = never noticed).");
        Tuning.FishYield += c => { if (fishMul.Value != 1f) { c.Yield = (int)Math.Round(c.Yield * fishMul.Value); L.LogInfo($"Tuning.FishYield: {NameOf(c.Item)} {c.GameYield} -> {c.Yield}"); } };
        Tuning.CropYield += c => { if (cropMul.Value != 1f) { c.Yield = (int)Math.Round(c.Yield * cropMul.Value); L.LogInfo($"Tuning.CropYield: {NameOf(c.Plant)} {c.GameYield} -> {c.Yield}"); } };
        Tuning.CropGrowthSpeed += c => { if (growMul.Value != 1f) c.Speed *= growMul.Value; };
        Tuning.PropertyPrice += c => { if (propMul.Value != 1f) c.Price = (int)Math.Round(c.Price * propMul.Value); };
        int gains = 0;
        Tuning.AwarenessGain += c =>
        {
            if (awareMul.Value == 1f) return;
            c.Amount *= awareMul.Value;
            if (++gains == 1 || gains % 500 == 0)   // per frame while watched
                L.LogInfo($"Tuning.AwarenessGain #{gains}: {c.GameAmount:0.####} -> {c.Amount:0.####} byDrone={c.ByDrone}");
        };
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
    // Layout diagnostics for Ui.Clone placement: rect values and the parent's layout components.
    static void LogRect(string what, UnityEngine.GameObject go)
    {
        try
        {
            var rt = go.transform.TryCast<UnityEngine.RectTransform>();
            var parent = go.transform.parent;
            var comps = parent == null ? "" : string.Join(", ", parent.GetComponents<UnityEngine.Component>().Select(c => c.GetIl2CppType().Name));
            var own = string.Join(", ", go.GetComponents<UnityEngine.Component>().Select(c => c.GetIl2CppType().Name));
            L.LogInfo($"Rect {what}: pos {rt?.anchoredPosition} size {rt?.sizeDelta} rect {(rt == null ? "?" : $"{rt.rect.width:0}x{rt.rect.height:0}")} " +
                      $"pivot {rt?.pivot} anchors {rt?.anchorMin}-{rt?.anchorMax} sibling {go.transform.GetSiblingIndex()} | own: {own} | parent {parent?.name}: {comps}");
        }
        catch (Exception e) { L.LogInfo($"Rect {what}: {e.Message}"); }
    }

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
