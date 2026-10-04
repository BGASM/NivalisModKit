# Custom content

> [!NOTE]
> Experimental: tested in game (items, recipes, icons, models, placed furniture, saving and reloading), but the API may still change.

You add items and recipes by **copying something the game already has** and changing what you list. A copy keeps everything you don't change: an item keeps its model, tags, decay and how it cooks; a recipe keeps its ingredient slots, processing steps and place in the unlock panel; a piece of furniture keeps its placement, collider and sounds. So the first decision is always: *which existing thing is closest to what I want?*

Three worked examples, from smallest to largest:

| Example | Shows | Made with |
|---|---|---|
| [Kit Test Chicken](#example-1-kit-test-chicken-code) | A new ingredient: name, price, icon | C# (`Content.AddItem`) |
| [Espresso Tonic](#example-2-espresso-tonic-json-pack) | A new drink **and** its recipe | A JSON content pack, no code |
| [Thomas the Tank Engine](#example-3-thomas-the-tank-engine-furniture-with-your-own-model) | Furniture with your own 3D model | A JSON content pack plus a Unity AssetBundle |

## Finding a template

Use the dev command `content dump` (dev console or bridge). It writes three files to `BepInEx\cache\NivalisModKit\content\`:

- **items.json**: every item, with its asset name, the name players see, price, stock range, tags, decay, whether it's furniture, its model prefab, the model's size in metres and its icon.
- **recipes.json**: every recipe, with its output, ingredient slots (item, amount, slot, processing, substitutes), unlock cost, prerequisites and whether it's known from the start.
- **vendors.json**: every vendor, with its district, type and what it stocks (by tag or by item).

A template can be named by its asset name, the name players see, or its model prefab's name (`Furniture_Radio_Cyber`, as AssetStudio shows it). An excerpt of items.json:

```json
{
  "asset": "Absinthe",
  "name": "Absinthe",
  "basePrice": 8,
  "stock": "50-150",
  "ingredient": true,
  "furniture": false,
  "tags": [ "Liquid", "Alcohol", "Consumable" ],
  "model": null,
  "icon": "Absinthe"
}
```

Every field is explained in the [content dump reference](content-dump.md). The **tags** decide who sells your copy: vendors stock by tag (see vendors.json), so a copy of Absinthe is sold wherever Absinthe is.

## Example 1: Kit Test Chicken (code)

The smallest useful item: a copy of Chicken with its own name, price and icon. It cooks exactly like chicken, and butchers stock it beside the original. It is in the [KitTester sample](https://github.com/BGASM/NivalisModKit/tree/main/samples/KitTester) behind its `[Content] TestItem` setting.

```csharp
public override void Load()   // register in Load: the game builds its item database after plugins load
{
    string dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
    Content.AddItem("bgasm.nivalis.kittester", "test-chicken", "Chicken", item =>
    {
        item.Name = "Kit Test Chicken";
        item.Description = "A chicken added by the Nivalis ModKit (Content.AddItem). Cooks like chicken.";
        item.BasePrice = 9.99f;
        item.IconPath = Path.Combine(dir, "KitTesterIcon.png");
    });
}
```

- The first two arguments are the **owner** (your plugin GUID) and an **id**. Together they make the item's GUID, which the save file stores. Never change either once players have the item.
- The third is the **template**, `"Chicken"`.
- `IconPath` is a PNG beside your DLL; square, 256 px works well. Without one the copy keeps the template's icon.

Check it in game with the dev commands `content` (lists what mods added, and whether each was built and is sold) and `give item=Kit Test Chicken`.

## Example 2: Espresso Tonic (JSON pack)

A new drink and the recipe that makes it, with no code. This is the [ContentPack sample](https://github.com/BGASM/NivalisModKit/tree/main/samples/ContentPack): a folder with one JSON file and one PNG, dropped anywhere under `BepInEx\plugins`.

```
BepInEx\plugins\EspressoTonic\
    espresso-tonic.content.json
    espresso-tonic.png
```

```json
{
  // Comments and trailing commas are allowed.
  "owner": "bgasm.examples.espressotonic",
  "items": [
    {
      "id": "espresso-tonic",
      "template": "Galaxy Lemonade",
      "name": "Espresso Tonic",
      "description": "Tonic water over a shot of espresso. Bitter, bright, and very awake.",
      "icon": "espresso-tonic.png"
    }
  ],
  "recipes": [
    {
      "id": "espresso-tonic",
      "template": "Galaxy Lemonade",
      "output": "Espresso Tonic",
      "replace": { "Butterfly Pea Flower": "Coffee Beans" },
      "knownFromStart": true
    }
  ]
}
```

- **The item** copies the Galaxy Lemonade drink: same glass, same model, same place in the drinks list.
- **The recipe** copies Galaxy Lemonade's recipe, makes the new drink instead (`output` may name your own item), and swaps one ingredient: the slot that defaulted to Butterfly Pea Flower now defaults to Coffee Beans. recipes.json shows each recipe's slots and their default ingredients.
- `knownFromStart` teaches it on the first load. Without it, the recipe sits in the unlock panel like its template and costs inspiration points (`unlockCost`).
- Paths (`icon`) are relative to the JSON file.

The same in C#:

```csharp
Content.AddItem(MyGuid, "espresso-tonic", "Galaxy Lemonade", item =>
{
    item.Name = "Espresso Tonic";
    item.Description = "Tonic water over a shot of espresso. Bitter, bright, and very awake.";
    item.IconPath = Path.Combine(dir, "espresso-tonic.png");
});
Content.AddRecipe(MyGuid, "espresso-tonic", "Galaxy Lemonade", recipe =>
{
    recipe.Output = "Espresso Tonic";
    recipe.ReplaceIngredient["Butterfly Pea Flower"] = "Coffee Beans";
    recipe.KnownFromStart = true;
});
```

## Example 3: Thomas the Tank Engine (furniture with your own model)

A shelf ornament with a custom model: a free, low-poly Thomas the Tank Engine, scaled down in Blender and built into an AssetBundle. It copies the cyber radio, so it can be bought, placed, picked up, stored and saved like the radio. It also **keeps the radio's behaviour**: Thomas plays music when switched on. Pick a template that behaves the way you want.

This one was a private test and isn't published; use your own model, or one whose license lets you share it.

```
BepInEx\plugins\ThomasPack\
    thomas.content.json
    thomas                (the AssetBundle file, no extension)
    thomas-icon.png
    CREDITS.txt           (the model's license requires credit)
```

```json
{
  "owner": "bgasm.test.thomas",
  "items": [
    {
      "id": "thomas-figure",
      "template": "Furniture_Radio_Cyber",
      "name": "Thomas the Tank Engine",
      "description": "A cheerful little tank engine for your shelf.",
      "basePrice": 25,
      "icon": "thomas-icon.png",
      "model": { "bundle": "thomas", "asset": "Thomas" }
    }
  ]
}
```

- `model.bundle` is the AssetBundle file (relative to the JSON); `model.asset` is the name of the model inside it. [Making an AssetBundle](asset-bundles.md) walks through building one from a Blender model.
- The radio's main mesh and materials become the model's; its shadow mesh and other parts are hidden; the model keeps the size it was built at; the radio's colliders are resized to fit it.
- Furniture vendors stock it like the radio (`basePrice` sets its price).
- A placed copy has its own identity in the save, so Thomas reloads as Thomas, not as a radio.

The log confirms the swap with the model's size:

```
Content: item 'thomas-figure' is placeable; prefab 0475737b-74b6-e9c3-44cc-f51d0952eb67
Content: item 'thomas-figure': model 'Thomas' (0.12 x 0.17 x 0.35 m)
```

## What you can set

| | Item (`ItemSpec` / JSON) | Recipe (`RecipeSpec` / JSON) |
|---|---|---|
| Template | An item: asset name, shown name, or model prefab name | A dish name (its recipe is copied) or a recipe asset name |
| Settings | `Name` / `name` (required), `ShortName`, `Description`, `BasePrice`, `MinStock`, `MaxStock`, `DecayDays`, `IconPath` / `icon`, `ModelBundlePath` + `ModelAsset` / `model` | `Output`, `OutputAmount`, `ReplaceIngredient` / `replace`, `UnlockCost`, `KnownFromStart` |

Set `MinStock = 0, MaxStock = 0` for an item vendors shouldn't sell (one you only give through a recipe or a command).

## Saves

- An item's or recipe's GUID comes from its owner and id. **Never change either once players have it**: a new GUID is a different item, and the old one disappears from their saves.
- **Removing a content mod is safe as long as the player doesn't save without it.** The game skips the missing items, recipes and placed objects, and they come back when the mod is reinstalled. Saving without the mod removes them from that save for good.
- The kit warns the player when a save they load uses content that isn't installed, before they can save (`[Content] WarnMissingContent`, on by default). Still, say it on your mod's page.

## Not yet

Plants and seeds, selling recipes for money, and choosing exactly which vendors stock an item. Researched and planned.
