# Example content pack

A new drink, **Espresso Tonic**, added with no code: a `*.content.json` file and an icon.

- The item is a copy of Galaxy Lemonade (its glass, its place in menus) with its own name, description and icon.
- The recipe is a copy of Galaxy Lemonade's recipe that makes Espresso Tonic, with Coffee Beans in place of the
  Butterfly Pea Flower. `knownFromStart` makes it known in every game without unlocking it.

To try it: copy this folder into `BepInEx\plugins` (with the Nivalis ModKit installed) and restart the game. The log
says `Content packs: 1 file(s), 1 item(s), 1 recipe(s)`, then `Content: added 1 item(s)` and `added 1 recipe(s)`.

Template and ingredient names are the game's item names. The dev command `content dump` writes every item, recipe and
vendor to `BepInEx\cache\NivalisModKit\content\` to look them up. See the kit README, "Custom content".
