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
Events: 28 of 28 live
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
| **Venues** (NPC venues included, check `Area.PlayerOwned`) | |
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
