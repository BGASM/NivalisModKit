# Nivalis ModKit

A shared library for Nivalis Nights mods, by BGASM. It does nothing on its own. Install it when another mod lists it as a requirement.

Mods that need it: [Manager Order Fix](../NivalisOrderFix) 2.0 and later (2.1 needs kit 0.2).

What you'll see with it installed:
- the kit and game version on the title screen, above the copyright line
- a **Mods** button in the pause menu, where mods that support it let you change their settings in game

## Install

1. Install BepInEx. See the [root README](../../README.md).
2. Launch the game once. Wait for the main menu, then close the game.
3. Extract the release zip into the game folder. The file lands at `BepInEx\plugins\NivalisModKit.dll`.

To confirm it loaded, open `BepInEx\LogOutput.log` and look for these lines:

```
Nivalis ModKit 0.2.0 loaded
Game build: 1.0 patch 2, Steam build 25680465 (tested)
Event BuyIngredientsStarting: live
...
Events: 56 of 70 live, 14 waiting for the game
```

"Waiting for the game" events attach once a save loads.

After a game update, the build line says `untested` (see [Compatibility](#compatibility)), and an event whose hook no longer matches shows as `missing` and never fires. The rest keep working. Mods that use a missing event can check for it and switch the feature off. The log line names the method that couldn't be found, which is the thing to report.

To uninstall, delete `NivalisModKit.dll`, and any mods that require it. Saves are unaffected.

## For modders

Reference `NivalisModKit.dll` (from `BepInEx\plugins`, not copied to your output) and declare the dependency so BepInEx loads the kit first:

```csharp
[BepInPlugin("you.nivalis.yourmod", "Your Mod", "1.0.0")]
[BepInDependency(ModKit.Guid, ">=0.2.0")]   // the oldest kit you support; BepInEx won't load your mod with an older one
public class Plugin : BasePlugin
```

`NivalisModKit.xml` in the release zip has documentation for every public member. Put it next to the DLL you reference to get it in your editor.

### Lifecycle

Your plugin's `Load` runs at startup, before any game is in progress. From there:

1. `NewGameStarted` or `GameLoaded`: the world is starting or a save finished loading.
2. `GameReady`: gameplay is usable (the game's managers exist). The safest place to read the world, open windows or apply settings.
3. `GameEnded`: the player is leaving for the title screen. Let go of game objects you hold. Loading another save from the pause menu skips this and goes straight to step 1.

`GameEvents.IsInGame` is true from `GameReady` until the player leaves. `GameEvents.WhenInGame(action)` runs `action` now if in game, otherwise once at the next `GameReady`:

```csharp
public override void Load()
{
    GameEvents.WhenInGame(() => Ui.Notify("My Mod is active"));   // waits for gameplay
}
```

### Events

`GameEvents`. Subscribe in `Load`. Each handler runs in its own `try`, so an exception in your handler is logged with your assembly's name and doesn't stop other mods or the game.

| Event | Fires |
|---|---|
| **Game** | |
| `NewGameStarted` | A new game started. The starting district follows as `DistrictEntered`. |
| `GameLoaded` | A save finished loading. Has the save name and starting district. |
| `GameReady` | Gameplay is usable, after either of the above. See Lifecycle. |
| `GameEnded` | The player is leaving for the title screen. |
| `GameSaved` | A save was written, manual or autosave. |
| **UI** | |
| `PanelShown`, `PanelHidden` | Any UI screen (`UIPanel`) shown or hidden: shops, venue tabs, dialogs, the end-of-day summary. `Name` is the panel's type name. |
| `EndOfDayShown` | The end-of-day summary screen opened. |
| **Time and travel** | |
| `DayStarted` | A new game day. The game's day turns over at 08:00, not midnight. |
| `DayEnded` | The day closed: curfew began (02:00) or the player slept. Rent is collected around now. |
| `HourStarted` | The world clock reached a new hour. Once per change: sleeping from 21:00 fires once at the new hour. |
| `DistrictEntered` | The player arrived in a different district. Building doors don't count. |
| `GameClock.TimeSpeedChanged` | The clock or simulation speed changed (see [Game clock](#game-clock)). |
| **Player shopping** | |
| `ShopOpened`, `ShopClosed` | The player opened or closed a vendor's shop window. |
| `PlayerBought`, `PlayerSold` | The player bought or sold something, with vendor, item, count and price. |
| `MoneyChanged` | The player's money changed: old, new and the difference. |
| **Quests** | |
| `QuestStarted`, `QuestCompleted`, `QuestFailed` | A quest changed state. Has the quest, its id and title. |
| `QuestObjectiveStarted`, `QuestObjectiveCompleted` | An objective (the game calls them sub-quests) started or was completed. |
| `QuestPinnedChanged` | The player pinned or unpinned a quest. |
| `QuestMarkerAdded`, `QuestMarkerRemoved` | A quest marker in the world was switched on or off. A refresh shows as Added, Removed, Added. |
| `VenueSetupQuestUpdated` | A venue setup quest changed state or progress. |
| **Curfew and security** | |
| `CurfewWarning`, `CurfewStarted`, `CurfewEnded` | Warning at 01:00, curfew 02:00 to 08:00. The clock stops during curfew; sleeping skips to 08:00. |
| `PlayerCaught` | Caught (awareness reached 1): by drone or camera, district, new security level. Furniture locks against theft for the rest of the curfew. |
| `AwarenessIncreased` | Security awareness rose: the rise and the new level. |
| `SecurityLevelChanged` | A district's security level changed. |
| **Fishing and farming** | |
| `FishCaught`, `FishDiscovered` | A catch went into the inventory; a species caught for the first time. |
| `CropPlanted`, `CropHarvested` | A greenhouse crop was planted or harvested (with "first time"). |
| **Property** | |
| `PropertyOwnerChanged` | Any venue, apartment or greenhouse bought, sold, rented or given up. |
| `RentStarted`, `RentStopped` | A property started or stopped being rented. |
| `FurniturePickedUp`, `FurniturePlaced`, `FurnitureStored` | The player picked up, placed, or stored furniture (or another holdable object). |
| `ApartmentEntered`, `ApartmentLeft` | The player entered or left an apartment. |
| **Staff** | |
| `StaffHired`, `StaffFired` | Staff hired at or fired from a venue. |
| `StaffPaid` | A wage came due: the amount (hundredths) and whether the owner could pay it. |
| `StaffSkillGained` | Someone gained experience in a skill. Frequent. |
| `StaffRolesChanged`, `StaffHoursChanged` | Roles (serving, cooking, cleaning, managing) or shift hours changed in the staff screen. Hours fire once the slider settles, with before and after. |
| **Theft and security (patched)** | |
| `TheftCommitted` | The player stole furniture from a venue they don't own. |
| `CameraDisabled` | The player disabled a security camera. |
| **Boat** | |
| `BoatBoarded`, `BoatLeft` | The player took or left the helm. |
| `BoatDocked`, `BoatUndocked` | The boat docked or left a dock. |
| `BoatTravel` | Fast travel by boat, with the destination. |
| `BoatRefueled` | A refuel finished: fuel added and fuel now. |
| **Venues** (NPC venues included, check `Area.PlayerOwned`) | |
| `VenueOpened`, `VenueClosed` | A venue opened or closed. Detected hourly, so up to an hour late. |
| `VenueOwnerChanged` | A venue changed owner, the player's or an NPC's. |
| `VenueStorageChanged` | Storage furniture added to or removed from a venue (since game patch 2, decorations with storage count too), with the amounts it provides. |
| `VenueHour` | Every venue's hourly update. |
| `BuyIngredientsStarting` | A venue is about to buy ingredients for one recipe. |
| `BuyIngredientsFinished` | That buying finished. Runs after other mods' patches, so purchases are final. |
| `IngredientsPurchased` | One successful ingredient purchase inside a restock round. |
| `EquipmentPurchased` | A venue bought equipment or furniture, such as a drinks machine or chairs. |
| `DeliveryCompleted` | Staff put a delivery into a venue's storage. Reports what actually went in. |
| `DishCooked` | A dish finished cooking, by staff or the player. |
| `SaleMade` | A customer paid. Vending machine sales have no venue. |

`GameEvents.IsAvailable(nameof(GameEvents.VenueHour))` is false if that event's hook failed to install.

### Purchasing

Manager ingredient buying. Once any mod subscribes to `VendorOrdering` or `OrderQuantity`, the kit takes over each recipe's purchases: it collects the vendor offers the game makes, asks handlers for the quantity and vendor order, then buys only up to that quantity. It stops at the first failed purchase, usually the venue out of money. With no subscribers, the game buys as normal.

```csharp
// Cheapest vendor first.
Purchasing.VendorOrdering += ctx =>
    ctx.Offers = ctx.Offers.OrderBy(o => o.Price).ToList();

// Buy double for the player's venues.
Purchasing.OrderQuantity += ctx =>
{
    if (ctx.Area.PlayerOwned) ctx.Quantity *= 2;
};
```

- Each `VendorOffer` has `Vendor`, `Item`, `Price`, `Stock`, `Hops` (district distance from the venue), `Amount` (what the game asked for) and `Sequence` (the game's order).
- Ordering handlers may reorder or remove offers but not add them. A list with added, duplicate or null offers is discarded and logged.
- Raising a quantity above the game's buys from each vendor up to its stock and the venue's per-recipe money limit, as the game does.
- Several handlers run in the order added, each seeing the previous one's result.
- `Purchasing.Decision` reports each offer as bought, skipped or failed. Subscribing to it alone doesn't turn the pipeline on.

### Pricing

Change what vendors charge and pay. The result is both shown in the shop and actually charged,
for the player and for venue managers. Installed only when a mod subscribes.

```csharp
// Chicken 20% cheaper at every vendor.
var chicken = Items.ByName("Chicken");
Pricing.BuyPrice += ctx =>
{
    if (ctx.Item?.Pointer == chicken?.Pointer) ctx.Price = (int)(ctx.Price * 0.8f);
};
```

`PriceContext` has `Vendor`, `Item`, `Freshness`, `GamePrice` and the settable `Price`. Prices and money are whole numbers in hundredths: 611 shows in game as 6.11. The shop's offer list and the amount charged use the adjusted price; the shop's Bonuses breakdown panel computes its own figures and still shows the game's price. Prices are
looked up very often (shop screens, manager restocks), so keep handlers cheap: cache item lookups.

### Tuning

Change game rules. Like `Pricing`, each is an event whose handlers adjust a value, and nothing is
patched until a mod subscribes to that one.

| Event | Adjust |
|---|---|
| `Tuning.CropYield` | Crops a greenhouse module yields (`Yield`) |
| `Tuning.CropGrowthSpeed` | A module's growth speed multiplier (`Speed`) |
| `Tuning.FishYield` | Items a catch gives (`Yield`) |
| `Tuning.PropertyPrice` | Purchase price of a venue, apartment or greenhouse (`Price`, hundredths) |
| `Tuning.AwarenessGain` | How much security awareness rises (`Amount`; 0 = not noticed) |
| `Tuning.Catch` | About to be caught: set `Cancel` to let the player off (awareness drops to `AwarenessAfterCancel`, 0.5 by default; furniture stays unlocked) |
| `Tuning.UseOrder` | Which items in a stack get used first (cooking, selling, moving). Since game patch 2 the game uses least-fresh first, spoiled included; `ctx.Sort(...)` overrides that |

```csharp
Tuning.FishYield += c => c.Yield *= 2;                 // double catches
Tuning.AwarenessGain += c => c.Amount *= 0.5f;         // cameras notice you half as fast
```

Each context also has the game's original value (`GameYield`, `GameSpeed`, `GamePrice`, `GameAmount`).

### Game clock

`GameClock`. How fast time passes, shared between mods. Each mod sets its own factor under its GUID; the kit applies the product, so two mods asking for 2x give 4x, and clearing one leaves the other's. Factors survive save loads.

| Member | What |
|---|---|
| `SetClockSpeed(owner, factor)`, `ClearClockSpeed(owner)` | How fast game time passes (days, opening hours, curfew). People, animations and cooking run at normal speed, so a venue serves fewer customers per game hour at 2x. Combined value kept within 0.1 to 20 |
| `SetSimulationSpeed(owner, factor)`, `ClearSimulationSpeed(owner)` | Fast-forward or slow motion of everything (Unity's time scale). Costs performance above 1. Combined value kept within 0.25 to 4 |
| `ClockSpeed`, `SimulationSpeed`, `ClockFactors`, `SimulationFactors` | What's in effect, and who set what |
| `Pause(owner)` | Pauses the clock until you dispose the returned handle. Uses the game's own pause, so time only runs again when nobody holds one |
| `IsPaused`, `TimeSpeedChanged` | Paused by anyone; raised when a speed changes |

```csharp
GameClock.SetClockSpeed(MyGuid, 2f);        // days pass twice as fast
using (GameClock.Pause(MyGuid)) { ... }     // clock stopped inside the block
```

### Staff

`Staff`. The task priority lists the game gives staff (which job a worker picks first).

| Member | What |
|---|---|
| `PriorityLists`, `PriorityListOf(person)` | The lists, and the one a worker uses |
| `Order(list)`, `NameOf(action)` | A list's actions, most important first, and their names |
| `SetOrder(list, names...)` | Reorder a list (experimental: it changes the game's shared list directly; see [Versioning](#versioning)) |

### Queries

Read-only lookups. Each returns a fresh list you can keep, or empty/null outside gameplay. Call them from the main thread: `Load`, kit event handlers, or Unity callbacks.

| Class | Members |
|---|---|
| `GameTime` | `Day`, `Hour`, `Minute`, `TotalHours`, `DayOfWeek`. The day turns over at 08:00. |
| `Venues` | `All`, `PlayerOwned`, `InDistrict(district)`, `DistrictOf(area)`, `NameOf(area)`, `Stock(area, item)` |
| `Economy` | (amounts in hundredths: 37526 = 375.26 in game) `NameOf(vendor)`, `PlayerMoney`, `Vendors`, `VendorsFor(item)`, `Offers`, `Price`, `SellPrice`, `Stock(vendor, item)`, `DistrictOf(vendor)`, `IsUnlocked(vendor)` |
| `Items` | `All`, `ByName("chicken")` (ignores case, spaces, underscores), `ById(guid)`, `NameOf(item)` |
| `Recipes` | `All`, `Known`, `ForDish(item)`, `InputsOf(recipe)` (item and amount per serving), `OutputOf(recipe)` |
| `Quests` | `Active`, `Completed`, `Pinned` |
| `Venues.StorageOf(area)` | Items stored and capacity, normal and refrigerated |
| `Security` | `IsCurfew`, `IsSecurityActive`, `Awareness` (0 to 1), `IsCaught`, `Level`, `LevelOf(district)` |

```csharp
// Cheapest vendor for chicken, and how far it is from your first venue.
var chicken = Items.ByName("Chicken");
var home = Venues.DistrictOf(Venues.PlayerOwned.FirstOrDefault());
var best = Economy.VendorsFor(chicken).OrderBy(v => Economy.Price(v, chicken)).FirstOrDefault();
int hops = World.Hops(home, Economy.DistrictOf(best));
```

Prices change every game day and when a save loads. If your own namespace starts with `Nivalis.`, write `NivalisModKit.Economy`, since the game also has a `Nivalis.Economy` namespace.

### Per-save data

Values that belong to one save, kept next to it as `<save>.modkit.json` in the game's save folder. Read and set them any time in gameplay; the kit writes the file when the game saves (manual or autosave) and reads it back when that save loads. A new game starts empty.

```csharp
static readonly ModSaveData data = SaveData.For("you.nivalis.yourmod");

GameEvents.GameLoaded += _ =>
{
    int visits = data.Get("visits", 0);        // 0 when the save has no value yet
    data.Set("visits", visits + 1);            // any JSON-serializable value
};
SaveData.Saving += () => data.Set("snapshot", myState);   // store cached state before the file is written
```

`SaveData.Loaded` fires just before `GameLoaded` / `NewGameStarted`, so the data is ready in those handlers. The file sits beside the game's `.sav`; it isn't known whether Steam Cloud syncs it. The game's own save-crash recovery (since patch 2) only covers `.sav` and `.png` files: if the game crashes mid-save and restores the previous `.sav`, the `.modkit.json` may be one save newer.

### Scheduler

Run code later on the main thread. Each call returns an `IDisposable`; dispose it to cancel.

| Call | Runs |
|---|---|
| `Scheduler.NextFrame(a)` | Next frame |
| `Scheduler.AfterSeconds(s, a)` | After `s` real seconds (counts while paused) |
| `Scheduler.AfterGameHours(h, a)` | Once `h` game hours have passed |
| `Scheduler.AtHour(h, a)` | Next time the clock reaches hour `h`; tomorrow if already past |
| `Scheduler.AfterDays(n, a)` | When `n` game days have started (days turn over at 08:00) |

Game-time jobs compare against the clock each frame, so sleeping past the moment still runs them, once. Pending jobs are cancelled when a save loads or a new game starts; store longer plans in per-save data.

### UI

`Ui`. The game's screens are `UIPanel` prefabs the game shows and hides; the kit reports them and lets you use the game's own notifications and dialogs. Main thread only.

| Member | What |
|---|---|
| `Ui.Notify(header, text)` | A toast in the game's notification feed |
| `Ui.Dialog(title, message, (label, action)...)` | The game's popup dialog with your buttons; each closes it and runs its action. Pass `closeOnClick: false` (after `message`) to keep it open; the player closes it with its X |
| `Ui.OpenPanels`, `Ui.Find(typeName)`, `Ui.NameOf(panel)` | Which screens are open; find one by type name |
| `Ui.IsVisible`, `Ui.IsDialogOpen` | UI shown (not hidden for screenshots); a dialog on screen |
| `Ui.ButtonsIn(panel)`, `Ui.LabelOf(button)` | A panel's buttons with paths and labels, to pick a template |
| `Ui.CloneButton(template, label, onClick)` | Adds your button by copying one of the game's (experimental) |
| `Ui.Clone(template)`, `Ui.CloneText(template, text)`, `Ui.TextsIn(panel)`, `Ui.FindChild(panel, path)`, `Ui.SetText(element, text)` | Copy any element or label, find templates, set text (experimental) |
| `Ui.Tooltip(element, text)` | The game's hover tooltip on any element |
| `Ui.OpenMenu(tab)`, `Ui.OpenJournal(quest)`, `Ui.OpenMap()`, `Ui.OpenVenue(area, tab)` | Open the game's screens |
| `Ui.CreateWindow(title, WindowStyle.Popup / Panel)` | A window of your own from the game's parts: `AddHeader`, `AddText`, `AddButton`, `AddToggle`, `AddSlider`, `AddChoice`, `AddTextField`, `AddValue`, `AddFooterButton`, `Clear`, `Show`, `Hide`, `Closed`. Popup = the game's small confirm popup; Panel = the Settings frame with a scrolling list (experimental) |
| `Ui.RequestMenuMode(owner)` | Menu mode (cursor on, movement and mouse-look off) until you dispose the handle, for UI you build yourself. Shared: the game leaves menu mode when the last mod releases. `Ui.IsMenuModeRequested`, `Ui.MenuModeOwners` |
| `Ui.MakeLive(element)` | Make a copy usable: controls interactable, canvas groups clickable, its layout on (Clone and windows do this) |
| `Ui.Relayout(element)` | Recompute a copy's layout after changing it (Clone/SetText do this) |
| `Ui.RadialMenu((label, action)...)`, `Ui.AddRadialAction(label, action)`, `Ui.IsRadialOpen` | The game's radial wheel: open it with your actions, or add to it while open (experimental) |

```csharp
GameEvents.PanelShown += a =>
{
    if (a.Name == "EndOfDayWindow") Ui.Notify("My Mod", "Day summary is up");
};
```

The button helpers use `UnityEngine.UI.Button` and the text helpers `TMPro.TMP_Text`: add references to `BepInEx\interop\UnityEngine.UI.dll` and `Unity.TextMeshPro.dll` in your project to call them.

The dev bridge's `/ui` path lists the open panels, the quickest way to learn a screen's name; `/ui?panel=Name` lists that panel's buttons.

### Mod menu

The kit adds one **Mods** button to the pause menu, shared by every mod. It opens the mod browser, a sub-screen like the game's Settings: the menu behind it stops taking input, and Escape or Close goes back.

The kit's browser lists:
- pages mods add with `ModMenu.AddPage`
- the settings of mods that opted in, edited live. Changes are saved to the mod's `.cfg` and raise `SettingChanged`, the same as editing the file

```csharp
ModMenu.AddPage(MyGuid, "My Mod", w =>
{
    w.AddText("Anything a KitWindow can show.");
    w.AddButton("Do the thing", DoTheThing);
});
```

**Which settings show.** Listing is opt-in, so nothing appears unless the mod asks:
- `ModMenu.ListSettings(myGuid)` in `Load` lists all your settings.
- A `ModSetting` tag lists one setting.

Players can also turn on the kit's `[ModMenu] ShowOtherMods` (in the browser, under Nivalis ModKit). The browser then lists all other mods' settings too, **read-only**, with a note to edit the `.cfg` and restart. The kit can't tell whether a mod that never heard of it reads a changed value live, so it doesn't offer to change them.

Either way, the flags below adjust how settings appear:

```csharp
ModMenu.ListSettings(MyGuid);
Config.Bind("Debug", "Trace", false, new ConfigDescription("Log everything.", null,
    new ModSetting { IsAdvanced = true }));
```

| Flag | Effect |
|---|---|
| `Browsable = false` | Hidden. |
| `ReadOnly = true` | Shown, not editable. |
| `IsAdvanced = true` | Shown only with "Show advanced" ticked. |
| `RequiresRestart = true` | Marked "(restart)": the mod reads it once at startup. |
| `Order`, `DisplayName` | Order within the section (higher first), and the name shown. |

The browser also reads BepInEx ConfigurationManager's `ConfigurationManagerAttributes` tag (`Browsable`, `ReadOnly`, `IsAdvanced`, `Order`, `DispName`) for listed settings. That tag alone doesn't list a setting.

**How each setting is edited:**

| Setting | Editor |
|---|---|
| `bool` | Toggle |
| Number with an `AcceptableValueRange` | Slider |
| `enum` with up to 12 values | Button that steps through the values |
| Value with an `AcceptableValueList` of up to 12 | Button that steps through the values |
| Anything else (text, numbers without a range, key bindings, colours, long lists) | Text box. The text is read the same way as in the `.cfg`; text that can't be read is ignored and the box shows the stored value again |

**Replacing the browser.** A mod can provide a better browser:
- Call `ModMenu.SetBrowser(myGuid, open)` and show your window with `ModMenu.OpenAsChild(window)`.
- If two mods replace it, the kit's `[ModMenu] Browser` setting picks one; otherwise the last one wins, and the log says so.
- `ModMenu.Pages` gives your browser the pages other mods added.

### Helpers

| Helper | Does |
|---|---|
| `World.Locations`, `World.Find(name)` | Districts, and lookup by name ("Meridian Market"), ignoring case and spaces. |
| `World.Hops(from, to)` | District transitions on the shortest route, or `World.Unreachable`. |
| `NativeHook.MethodPointer<T>(name)` | Native address of an interop method, by `NativeMethodInfoPtr_...` field name, or method name if not overloaded. |
| `NativeHook.Install(...)` | Native detour. Keeps your delegates alive. For methods with by-reference struct parameters, which Harmony can't patch safely. |
| `StructLayout.FieldOffset<T>(field)`, `Size<T>()` | IL2CPP field offsets and sizes from the running game. Value types have the object header subtracted. |

**Reference example:** [Manager Order Fix](../NivalisOrderFix/Plugin.cs) is a complete kit mod in about 250 lines: a `Purchasing` handler, settings listed in the Mods browser with sliders and an advanced flag, a page of its own (`ModMenu.AddPage`), a dev command, daily stats from `GameEvents`, and a version dependency. [samples/KitTester](../../samples/KitTester) exercises every kit feature for testing, and [samples/QuantityTester](../../samples/QuantityTester) shows `OrderQuantity`.

### Versioning

Public members are added, never removed. Anything replaced is marked `[Obsolete]` first and kept for at least one release. A mod built against 0.2 keeps working with later 0.x releases.

The exception is members marked `[Experimental]`. They may change in any release; their note says what to expect. Currently experimental:

| Member | Why |
|---|---|
| `Staff.SetOrder` | Changes the game's shared list directly. Will become a `Tuning` hook so several mods can combine. |
| `Tuning.UseOrder` | The context may change (for example a perishable flag). |

Declare the oldest kit you support with `[BepInDependency(ModKit.Guid, ">=0.2.0")]`. BepInEx then refuses to load your mod with an older kit and says why in the log.

## Live config reload

With the kit installed, any mod's settings reload when you save its `.cfg` file in `BepInEx\config`, while the game runs. The log shows what changed:

```
Config reloaded: bgasm.nivalis.orderfix.cfg: VendorSort: Balanced -> Cheapest
```

Changes made in the in-game Mods browser are written to the same files, so the two always agree. A mod picks up the change the next time it reads the setting. For modders: read `entry.Value` when you use it rather than copying it once in `Load`, or subscribe to `entry.SettingChanged`. Reloads run on the main thread, so handlers can touch game objects.

Some settings only matter at startup, such as ones that decide whether a mod hooks anything at all. Those still need a restart.

## Dev bridge (for mod developers)

An HTTP endpoint for inspecting the running game from scripts or a terminal, and optionally for running dev commands. Off by default; turn on `[DevBridge] Enabled` and restart. It only listens on `127.0.0.1`, so nothing outside your computer can reach it. Its read paths can't change anything in the game; commands are a separate opt-in (below).

| Path | Returns |
|---|---|
| `/status` | Kit version, game build, game time, live events |
| `/events` | How often each kit event fired |
| `/time` | Clock, speed factors, pause state |
| `/money`, `/quests`, `/security`, `/districts` | Player money, quests, curfew and security, districts |
| `/venues`, `/venues?owned=1` | Venues: open, in staff hours, storage |
| `/vendors`, `/items`, `/recipes`, `/restock` | Economy lookups |
| `/priorities` | Staff task priority lists |
| `/ui`, `/ui?panel=Name`, `&texts`, `&rects` | Open panels; a panel's buttons, texts and element positions |
| `/perf` | Per-event timings (with `[Debug] FrameTiming`) |
| `/object?type=Nivalis.NotificationHudUi` | Fields and properties of the first live instance of any Unity type, like a text-only UnityExplorer |

```
curl http://127.0.0.1:5710/status
curl "http://127.0.0.1:5710/ui?panel=SettingsPanel&rects"
```

### Dev commands

Mods can register named commands that tools run in the game: open a window, set up a situation, return a result. They run on the main thread, so they can use the game and the kit freely.

```csharp
DevCommands.Register(MyGuid, "give-money", "amount=N: add money (hundredths)", args =>
{
    int amount = args.GetInt("amount", 10000);
    AddMoney(amount);
    return new { added = amount, now = Economy.PlayerMoney };   // the reply, as JSON
});
```

`CommandArgs` has `Get`, `GetInt`, `GetFloat`, `GetBool` (on/off/true/false/1/0, or a bare `?flag`) and `Has`. A name another mod already registered is refused, and the log says which. Throwing returns the message as an error.

Three ways to run them:

- **In game:** turn on `[DevConsole] Enabled` and press `` ` `` (`[DevConsole] Key`): a terminal drops from the top of the screen. Type `demo style=Panel`, Enter. Up/Down recall earlier commands, Tab completes names, the mouse wheel or Page Up/Down scroll back, `clear` empties it, Esc or `` ` `` closes it. Quote values with spaces: `notify text="hello there"`. It needs no bridge, and works on the title screen too (commands that need a save say so).
- **Windows terminal:** `tools\kit.cmd demo style=Panel` (`tools\kit.cmd` alone lists them). Needs `[DevBridge] Enabled` and `AllowCommands`.
- **Git Bash or scripts:** `tools/bridge.sh cmd demo style=Panel`, same requirements.

Without the script: `POST http://127.0.0.1:5710/cmd/<name>?arg=value` with the header `X-Kit-Token`, whose value the kit writes to `BepInEx\cache\nivalismodkit-bridge.token` at every start. The token is what stops a web page from running commands: browsers can't send custom headers to localhost. Requests from a browser (with an `Origin` header) are refused too.

The kit's own commands:

| Command | Does |
|---|---|
| `help` | Lists the commands |
| `notify text=... [header=...]` | A notification in the game's feed |
| `open what=Map / Venue / <menu tab>` | Opens a game screen |
| `mods` | Opens the Mods browser |
| `money amount=N` | Adds money in hundredths (10000 = 100.00; negative takes it away) |
| `give item=Name [amount=N]` | Puts items in the player's inventory |
| `clock [speed=X] [sim=X] [pause=on/off]` | Clock and simulation speed (1 clears), shared pause |

**Without commands.** Live reload works as a command channel too: saving a `.cfg` applies within a second, so a test mod can treat settings as triggers. [KitTester](../../samples/KitTester) still accepts `[Ui] Demo = Panel` and `[Ui] Open = Map` in its `.cfg`.

## Settings

`BepInEx\config\bgasm.nivalis.modkit.cfg`, or in game: pause menu > Mods > Nivalis ModKit (Debug and Dev bridge under "Show advanced").

| Setting | Default | Effect |
|---|---|---|
| `[General] LiveConfigReload` | true | Reload mods' settings when their `.cfg` file is saved. Restart to apply. |
| `[General] UntestedBuild` | Warn | On a game build the kit wasn't tested on: `Warn` or `Disable` (see [Compatibility](#compatibility)). Restart to apply. |
| `[ModMenu] Enabled` | true | The Mods button in the pause menu. |
| `[ModMenu] ShowOtherMods` | false | Also list mods that didn't opt in, read-only. |
| `[ModMenu] ButtonLabel` | Mods | Text on the pause menu button. Restart to apply. |
| `[ModMenu] Browser` | empty | Which mod's browser the button opens when more than one mod provides one (a GUID). |
| `[DevBridge] Enabled` | false | Read-only HTTP endpoint on 127.0.0.1 for development tools. Restart to apply. |
| `[DevBridge] Port` | 5710 | Its port. Restart to apply. |
| `[DevBridge] AllowCommands` | false | Let tools run mods' dev commands through the bridge (token required). Leave off unless developing. |
| `[DevConsole] Enabled` | false | In-game console for dev commands. |
| `[DevConsole] Key` | Backquote | Key that opens and closes it (Unity Input System name; Backquote is `` ` ``). |
| `[DevConsole] Font` | empty | Console font by name (the log lists the game's fonts); empty picks a plain one. Restart to apply. |
| `[Debug] FrameTiming` | false | Log slow frames with the kit's share of them; per-event timings at `/perf`. For diagnosing lag. |
| `[Debug] FrameThresholdMs` | 50 | What counts as a slow frame. |
| `[Debug] SimulateMissing` | empty | Comma-separated event names to treat as missing. For testing a mod's fallback. Not shown in game. |

## Compatibility

Tested on Nivalis Nights 1.0 patch 2 (Steam build 25680465) with BepInEx be.788.

The game's version string stays "1.0" across patches, so the kit identifies the build by a fingerprint of `GameAssembly.dll`. The title screen shows the kit version and game build above the copyright line. On a build this kit wasn't tested on (usually right after a game patch), what happens depends on `[General] UntestedBuild`:
- `Warn` (default): the kit runs normally. The title line turns orange, the log warns, and a notification appears once in game.
- `Disable`: the kit installs nothing. Mods using it are inactive and the game runs unmodded until the kit is updated.

Mods can check the build too: `GameBuild.IsTested`, `GameBuild.Describe()` and `GameBuild.SteamBuildId`.

Other mods that detour `Vendor.BuyItem` directly will conflict with the purchasing pipeline. Build on `Purchasing` instead.

## License

MIT
