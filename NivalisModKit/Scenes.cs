using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NivalisModKit;

/// <summary>The loaded scenes and what's in them.</summary>
[Experimental("New in 0.4.")]
public static class Scenes
{
    /// <summary>The active scene's name, e.g. "1_Lowtown". Districts, interiors and curfew are separate scenes.</summary>
    public static string Active => SceneManager.GetActiveScene().name;

    /// <summary>
    /// Components of a type in the loaded scenes. With <paramref name="includeInactive"/> it also finds switched-off
    /// ones: the game switches off much of a district that's far from the player (vendor stalls, markers), so a search
    /// for the whole district needs it. Prefabs and other assets are never included. A full search; don't call it every
    /// frame.
    /// </summary>
    /// <example><code>
    /// foreach (var stall in Scenes.Objects&lt;VendorInteraction&gt;(includeInactive: true)) ...
    /// </code></example>
    public static IEnumerable<T> Objects<T>(bool includeInactive = false) where T : Component
    {
        var found = includeInactive
            ? Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>())
            : Object.FindObjectsOfType(Il2CppType.Of<T>());
        foreach (var o in found)
        {
            var c = o.TryCast<T>();
            if (c == null) continue;
            if (includeInactive)
            {
                // FindObjectsOfTypeAll also returns prefabs and assets, which belong to no loaded scene.
                var scene = c.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded) continue;
            }
            yield return c;
        }
    }
}
