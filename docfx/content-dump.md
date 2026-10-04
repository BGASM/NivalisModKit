# Content dump reference

The dev command `content dump` (dev console or bridge, in gameplay) writes what the game holds to `BepInEx\cache\NivalisModKit\content\`. Run it on your own game: the files match the build you have, including other mods' content, and they change when the game is patched. This page explains the fields; it doesn't list the data.

| File | One entry per |
|---|---|
| `items.json` | Item: ingredients, dishes, drinks, furniture, decorations, tools |
| `recipes.json` | Recipe, with its ingredient slots |
| `vendors.json` | Vendor, with what it stocks |

A field the kit couldn't read is `null`.

## items.json

| Field | Meaning |
|---|---|
| `asset` | The item's asset name. Use it as a content `template`. |
| `name` | The name players see (also accepted as a template). `null`: the game shows none (decorations, story items). |
| `guid` | The GUID saves store. |
| `custom` | Added by a mod through the kit. |
| `basePrice` | Base price; vendors' prices vary around it day to day. |
| `stock` | Units a vendor stocks, `min-max`. `0-0`: not sold. |
| `buy`, `sell` | The game's flags for buying it from and selling it to vendors. |
| `ingredient` | Usable in recipes. |
| `furniture` | Placeable furniture or appliance. |
| `plant`, `fish` | Grows in a planter; is a catch. |
| `decayDays` | Days until it spoils. `0`: doesn't spoil. |
| `fridge` | Needs refrigeration. |
| `tags` | The item's tags. Vendors stock by tag, and recipe slots accept substitutes by tag, so a copy is sold and cooked wherever its template is. |
| `model` | The model prefab's name (as AssetStudio shows it; also accepted as a template). `null`: no model. |
| `size` | The model's visible size in metres: `x` width, `y` height, `z` depth. Compare it with your own model's size (Blender's Dimensions) before building a bundle. |
| `icon` | The inventory icon's sprite name. |

## recipes.json

| Field | Meaning |
|---|---|
| `asset` | The recipe's asset name (accepted as a recipe `template`, as is the dish name). |
| `guid` | The GUID saves store. |
| `custom` | Added by a mod through the kit. |
| `output`, `amount` | The item it makes, and how many. |
| `type` | Its menu category: Drink, Fish, Soup, Meat, Seafood, Sandwich, Salad, Dessert, Pizza, Snack... |
| `unlockCost` | Inspiration points to unlock it. |
| `known` | Known in the save the dump was taken in. |
| `disabled`, `playerOnly` | The game's flags of those names. |
| `prerequisites` | Recipes that must be unlocked first. |
| `inputs` | The ingredient slots (below). |

Each entry in `inputs`:

| Field | Meaning |
|---|---|
| `item` | The slot's default ingredient. A content recipe's `replace` names this item to swap it. |
| `amount` | How many. |
| `slot` | The slot: Main, Topping, Seasoning, Sides, Sauce. |
| `processing` | How it's prepared: None, Raw, Cooker, Blender, Grill, Pan, Oven, DeepFryer, or several (`Oven, Blender`). |
| `substitutes` | Tags whose items may replace the default. |

## vendors.json

| Field | Meaning |
|---|---|
| `name` | The vendor. |
| `district` | Where it is. |
| `type` | What kind: Greengrocer, Butcher, Fishmonger, Drinks vendor, Hardware vendor, Venue furniture vendor... |
| `tier` | The game's vendor tier (`None`, or a letter). |
| `offers` | What it stocks (below). |
| `excluded` | Items it never stocks, even when an offered tag matches. |

Each entry in `offers`:

| Field | Meaning |
|---|---|
| `by` | `Tag`: every item with `tag`. `Item`: just `item`. |
| `tag`, `item` | What is offered. |
| `priceBonus` | Added to the price at this vendor. |
| `stockMultiplier` | Scales the item's `stock` range at this vendor. |

To see who will sell your copy, look up the template's `tags` in items.json, then the vendors whose `offers` have those tags.
