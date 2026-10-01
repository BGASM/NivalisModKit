# Nivalis ModKit

A shared library for Nivalis Nights mods, by BGASM. It does nothing on its own. Install it when another mod lists it as a requirement.

Mods that need it: [Manager Order Fix](../NivalisOrderFix) 2.0 and later.

## Install

1. Install BepInEx. See the [root README](../../README.md).
2. Launch the game once. Wait for the main menu, then close the game.
3. Extract the release zip into the game folder. The file lands at `BepInEx\plugins\NivalisModKit.dll`.

To confirm it loaded, open `BepInEx\LogOutput.log` and look for `Nivalis ModKit 0.1.0 loaded`, followed by one line per game event:

```
Event BuyIngredientsStarting: live
Event BuyIngredientsFinished: live
...
Event PlayerSold: live
Events: 62 of 62 live
```

After a game update, an event whose hook no longer matches shows as `missing` and never fires. The rest keep working. Mods that use a missing event can check for it and switch the feature off. The log line names the method that couldn't be found, which is the thing to report.

To uninstall, delete `NivalisModKit.dll`, and any mods that require it. Saves are unaffected.

## For modders

Reference `NivalisModKit.dll` (from `BepInEx\plugins`, not copied to your output) and declare the dependency so BepInEx loads the kit first:

```csharp
[BepInPlugin("you.nivalis.yourmod", "Your Mod", "1.0.0")]
[BepInDependency(ModKit.Guid)]
public class Plugin : BasePlugin
```

`NivalisModKit.xml` in the release zip has documentation for every public member. Put it next to the DLL you reference to get it in your editor.

### Events

`GameEvents`. Subscribe in `Load`. Each handler runs in its own `try`, so an exception in your handler is logged with your assembly's name and doesn't stop other mods or the game.

| Event | Fires |
|---|---|
| **Game** | |
| `NewGameStarted` | A new game started. The starting district follows as `DistrictEntered`. |
| `GameLoaded` | A save finished loading. Has the save name and starting district. |
| `GameSaved` | A save was written, manual or autosave. |
| `DayStarted` | A new game day. The game's day turns over at 08:00, not midnight. |
| `HourStarted` | The world clock reached a new hour. Once per change: sleeping from 21:00 fires once at the new hour. |
| `DistrictEntered` | The player arrived in a different district. Building doors don't count. |
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
| `CurfewWarning`, `CurfewStarted`, `CurfewEnded` | Warning at 01:00, curfew 02:00 to 08:00. |
| `PlayerCaught` | Caught breaking curfew by a camera or drone. |
| `AwarenessIncreased` | Security awareness rose: the rise and the new level. |
| `SecurityLevelChanged` | A district's security level changed. |
| **Fishing and farming** | |
| `FishCaught`, `FishDiscovered` | A catch went into the inventory; a species caught for the first time. |
| `CropPlanted`, `CropHarvested` | A greenhouse crop was planted or harvested (with "first time"). |
| **Property** | |
| `PropertyOwnerChanged` | Any venue, apartment or greenhouse bought, sold, rented or given up. |
| `RentStarted`, `RentStopped` | A property started or stopped being rented. |
| `FurniturePlaced`, `FurnitureStored` | The player placed or stored furniture (or another holdable object). |
| `ApartmentEntered`, `ApartmentLeft` | The player entered or left an apartment. |
| **Staff** | |
| `StaffHired`, `StaffFired` | Staff hired at or fired from a venue. |
| `StaffPaid` | A venue paid a staff member (wage, in hundredths). |
| `StaffSkillGained` | Someone gained experience in a skill. Frequent. |
| `StaffRolesChanged`, `StaffHoursChanged` | Roles (serving, cooking, cleaning, managing) or working hours changed. |
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

```csharp
Tuning.FishYield += c => c.Yield *= 2;                 // double catches
Tuning.AwarenessGain += c => c.Amount *= 0.5f;         // cameras notice you half as fast
```

Each context also has the game's original value (`GameYield`, `GameSpeed`, `GamePrice`, `GameAmount`).

### Queries

Read-only lookups. Each returns a fresh list you can keep, or empty/null outside gameplay. Call them from the main thread: `Load`, kit event handlers, or Unity callbacks.

| Class | Members |
|---|---|
| `GameTime` | `Day`, `Hour`, `Minute`, `TotalHours`, `DayOfWeek`. The day turns over at 08:00. |
| `Venues` | `All`, `PlayerOwned`, `InDistrict(district)`, `DistrictOf(area)`, `NameOf(area)`, `Stock(area, item)` |
| `Economy` | (amounts in hundredths: 37526 = 375.26 in game) `PlayerMoney`, `Vendors`, `VendorsFor(item)`, `Offers`, `Price`, `SellPrice`, `Stock(vendor, item)`, `DistrictOf(vendor)`, `IsUnlocked(vendor)` |
| `Items` | `All`, `ByName("chicken")` (ignores case, spaces, underscores), `ById(guid)`, `NameOf(item)` |
| `Recipes` | `All`, `Known`, `ForDish(item)`, `InputsOf(recipe)` (item and amount per serving), `OutputOf(recipe)` |
| `Quests` | `Active`, `Completed`, `Pinned` |

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

`SaveData.Loaded` fires just before `GameLoaded` / `NewGameStarted`, so the data is ready in those handlers. The file sits beside the game's `.sav`; it isn't known whether Steam Cloud syncs it.

### Scheduler

Run code later on the main thread. Each call returns an `IDisposable`; dispose it to cancel.

| Call | Runs |
|---|---|
| `Scheduler.NextFrame(a)` | Next frame |
| `Scheduler.AfterGameHours(h, a)` | Once `h` game hours have passed |
| `Scheduler.AtHour(h, a)` | Next time the clock reaches hour `h`; tomorrow if already past |
| `Scheduler.AfterDays(n, a)` | When `n` game days have started (days turn over at 08:00) |

Game-time jobs compare against the clock each frame, so sleeping past the moment still runs them, once. Pending jobs are cancelled when a save loads or a new game starts; store longer plans in per-save data.

### Helpers

| Helper | Does |
|---|---|
| `World.Locations`, `World.Find(name)` | Districts, and lookup by name ("Meridian Market"), ignoring case and spaces. |
| `World.Hops(from, to)` | District transitions on the shortest route, or `World.Unreachable`. |
| `NativeHook.MethodPointer<T>(name)` | Native address of an interop method, by `NativeMethodInfoPtr_...` field name, or method name if not overloaded. |
| `NativeHook.Install(...)` | Native detour. Keeps your delegates alive. For methods with by-reference struct parameters, which Harmony can't patch safely. |
| `StructLayout.FieldOffset<T>(field)`, `Size<T>()` | IL2CPP field offsets and sizes from the running game. Value types have the object header subtracted. |

See [samples/KitTester](../../samples/KitTester) and [samples/QuantityTester](../../samples/QuantityTester) for working examples.

### Versioning

Public members are added, never removed. Anything replaced is marked `[Obsolete]` first. A mod built against 0.1 keeps working with later 0.x releases.

## Live config reload

With the kit installed, any mod's settings reload when you save its `.cfg` file in `BepInEx\config`, while the game runs. The log shows what changed:

```
Config reloaded: bgasm.nivalis.orderfix.cfg: VendorSort: Balanced -> Cheapest
```

A mod picks up the change the next time it reads the setting. For modders: read `entry.Value` when you use it rather than copying it once in `Load`, or subscribe to `entry.SettingChanged`. Reloads run on the main thread, so handlers can touch game objects.

Some settings only matter at startup, such as ones that decide whether a mod hooks anything at all. Those still need a restart.

## Dev bridge (for mod developers)

A read-only HTTP endpoint for inspecting the running game from scripts or a terminal. Off by default; turn on `[DevBridge] Enabled` and restart. It only listens on `127.0.0.1`, so nothing outside your computer can reach it, and it can't change anything in the game.

```
curl http://127.0.0.1:5710/status      kit and game version, game time, live events
curl http://127.0.0.1:5710/events      how often each kit event fired
curl http://127.0.0.1:5710/money
curl http://127.0.0.1:5710/quests
curl http://127.0.0.1:5710/venues?owned=1
curl http://127.0.0.1:5710/districts
curl "http://127.0.0.1:5710/object?type=Nivalis.NotificationHudUi"
```

`/object` lists the fields and properties of the first live instance of any Unity component or asset type, like a text-only UnityExplorer inspector.

## Settings

`BepInEx\config\bgasm.nivalis.modkit.cfg`

| Setting | Default | Effect |
|---|---|---|
| `[DevBridge] Enabled` | false | Read-only HTTP endpoint on 127.0.0.1 for development tools. Restart to apply. |
| `[DevBridge] Port` | 5710 | Its port. |
| `[General] LiveConfigReload` | true | Reload mods' settings when their `.cfg` file is saved. Takes effect after a restart. |
| `[Debug] SimulateMissing` | empty | Comma-separated event names to treat as missing. For testing a mod's fallback. |

## Compatibility

Tested on Nivalis Nights 1.0 with BepInEx be.788. The log warns when the game version differs from the tested one.

Other mods that detour `Vendor.BuyItem` directly will conflict with the purchasing pipeline. Build on `Purchasing` instead.

## License

MIT
