# Making an AssetBundle

An AssetBundle is a file Unity builds that holds a model with its materials and textures. A content item can wear a model from one (`"model": { "bundle": "file", "asset": "Name" }` in a content pack, or `ModelBundlePath` + `ModelAsset` in code; see [Custom content](content.md)).

This walks from a model in Blender to a bundle the game loads. You need Blender (any recent version) and Unity **2020.3**.

## Rules first

- **Unity 2020.3, built-in render pipeline.** The game is built with Unity 2020.3.44f1. Bundles from other major versions may fail to load; 2020.3 patch versions work (2020.3.34f1 was used to test). URP and HDRP materials show up pink.
- **One mesh.** The kit uses the first mesh in your model. Join the parts into one object before exporting; one mesh can still have several materials.
- **No scripts.** Only the model, its materials and textures. Scripts in a bundle can't run in the game.
- **Only what you may share.** Your own work, or models whose license allows it (keep their credit in your pack and on your mod page). Never models extracted from the game or other games.

## 1. Prepare the model in Blender

1. **Size it in metres.** Blender's units are metres, and so are the game's. Check the Dimensions in the N panel against your template's `size` in the [content dump](content-dump.md)'s items.json, or a similar item's: that's how big the game's own model is. Typical sizes, width x height x depth, measured from the game:

   | Kind | Examples |
   |---|---|
   | Drinks, dishes | Glass drink 0.10 x 0.15 x 0.09 m; plated dish about 0.26 x 0.06 x 0.23 m; coffee mug 0.16 x 0.10 x 0.12 m |
   | Desk and shelf items | Mouse 0.10 x 0.15 x 0.11 m; headphones 0.18 x 0.22 x 0.11 m; table lamp 0.19 x 0.48 x 0.19 m; radio 0.29 x 0.50 x 0.16 m; small potted plant 0.21 x 0.34 x 0.20 m |
   | Seating | Stool 0.41 x 0.47 x 0.45 m; bar chair 0.39 x 0.80 x 0.39 m; dining chair 0.49 x 1.07 x 0.64 m; two-seat sofa 1.73 x 0.86 x 0.78 m |
   | Tables | Table 0.92 x 0.79 x 0.94 m; long table 1.96 x 0.77 x 0.99 m; high table 1.64 x 1.08 x 0.75 m |
   | Kitchen | Base cabinet 0.60 or 0.90 x 0.99 x 0.66 m; wall cabinet 0.90 x 0.44 x 0.33 m; range 0.90 x 0.99 x 0.66 m; washing machine 0.73 x 1.04 x 0.75 m |
   | Bigger furniture | Book shelf 0.69 x 1.60 x 0.35 m; floor lamp 0.39 x 1.40 x 0.39 m; single bed 1.00 x 0.57 x 2.13 m; vending machine about 1.3 x 2.1 x 0.6 m |
   | On the wall | Painting about 0.92 x 0.92 m, 0.01 to 0.04 m deep; wall TV 1.02 x 0.62 x 0.08 m; small photo frame 0.46 x 0.36 x 0.07 m |
   | Floor | Carpet 1.84 x 1.38 m up to 4.8 x 2.7 m, 0.01 to 0.04 m thick |

   Thomas, as a shelf ornament, is 0.12 x 0.17 x 0.35 m: radio-sized. Matching the template's size also means the placement box (fitted to your model) sits where players expect.
2. **Put the origin at the bottom centre.** The game places an object by its origin, so an origin in the middle sinks half the model into the shelf. Snap the 3D cursor to the base (Shift+S), then Object > Set Origin > Origin to 3D Cursor.
3. **Join the parts** into one object: select them all, Ctrl+J.
4. **Apply transforms**: Ctrl+A > All Transforms. Rotation 0, scale 1, size as shown.
5. **Facing:** the kit keeps your model's rotation as built. If it faces backwards in the game, rotate it in Blender, apply, and export again.
6. **Export:** File > Export > FBX. Limit to *Selected Objects*, Object Types *Mesh*, *Apply Transform* on. For textures, set Path Mode to *Copy* and turn on *Embed Textures* (the button beside it), or copy the image files into Unity yourself.

## 2. Set up a Unity project

1. Install **Unity Hub**, then Unity **2020.3** from the archive (Installs > Install Editor > Archive, or [unity.com/releases/editor/archive](https://unity.com/releases/editor/archive)). No extra modules are needed; if the Visual Studio download fails, untick it.
2. New project with the **3D** template (the built-in pipeline; not "3D (URP)").
3. Make a folder `Assets/Editor` and save this script in it as `BuildBundles.cs`:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildBundles
{
    // Builds every asset marked with an AssetBundle name into Bundles/, for the Windows game.
    [MenuItem("Assets/Build AssetBundles")]
    public static void Build()
    {
        const string output = "Bundles";
        Directory.CreateDirectory(output);
        BuildPipeline.BuildAssetBundles(output, BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);
        EditorUtility.RevealInFinder(output);
    }

    // Marks the selected assets for a bundle named after the first one (lower case), for when the
    // AssetBundle field at the bottom of the Inspector won't take typing.
    [MenuItem("Assets/Put Selection In Bundle")]
    public static void Mark()
    {
        if (Selection.objects.Length == 0) return;
        string bundle = Selection.objects[0].name.ToLowerInvariant().Replace(' ', '-');
        foreach (var o in Selection.objects)
            AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(o)).assetBundleName = bundle;
        Debug.Log($"Marked {Selection.objects.Length} asset(s) for bundle '{bundle}'");
    }
}
```

## 3. Import and check the model

1. Drag the FBX (and any textures) into the Project window.
2. Select the FBX. In the Inspector:
   - **Model** tab: Scale Factor 1, *Convert Units* on. Apply.
   - **Materials** tab: *Extract Materials...* (and *Extract Textures...* if embedded) into a folder, so you can edit them. They should use the **Standard** shader.
3. Drag the model into the scene beside a default cube (GameObject > 3D Object > Cube, which is 1 m) and check the size. Fix it in Blender, not with Unity's scale.
4. Drag the model from the scene back into the Project window to make a **prefab**, and name it. **This name is the `asset` in your content pack** (`"asset": "Thomas"`), and it must match exactly.

## 4. Build the bundle

1. Select the prefab. At the bottom of the Inspector, set **AssetBundle** to a name (New..., e.g. `thomas`). If the field won't take text, right-click the prefab > *Put Selection In Bundle*.
2. **Assets > Build AssetBundles.** The `Bundles` folder opens.
3. Copy the file named after your bundle (`thomas`, no extension) into your pack folder. The `.manifest` files and the file called `Bundles` aren't needed.

## 5. Use it

```
BepInEx\plugins\ThomasPack\
    thomas.content.json     "model": { "bundle": "thomas", "asset": "Thomas" }
    thomas
    thomas-icon.png
```

Start the game and look for the model line in `BepInEx\LogOutput.log`:

```
Content: item 'thomas-figure': model 'Thomas' (0.12 x 0.17 x 0.35 m)
```

Then `give item=Thomas the Tank Engine` in the dev console, and place it.

## An icon

Inventory icons are square PNGs; 256 px works well. One way in Blender: an orthographic camera looking down at a three-quarter angle, a Sun light, a transparent background (Render Properties > Film > Transparent), 256 x 256 output, then F12 and save as PNG. Point `icon` at it.

## If it doesn't look right

| Symptom | Likely cause |
|---|---|
| Log: `no model 'X' in ...; keeping the template's look` | The bundle file isn't where `bundle` points, or `asset` doesn't match the prefab's name |
| Log: `'X' has no mesh` | The prefab has no mesh renderer (an empty or a rig only) |
| Pink model | URP/HDRP project or shader; use the 3D (built-in) template and the Standard shader |
| Way too big or small | Size in Blender, apply scale, Scale Factor 1 in Unity |
| Floats or sinks | The origin isn't at the bottom centre |
| Only part of the model shows | It's several meshes; join them in Blender |
| Faces the wrong way | Rotate in Blender, apply, re-export |
| Bundle won't load at all | Built with a Unity version other than 2020.3, or not for Windows (StandaloneWindows64) |
