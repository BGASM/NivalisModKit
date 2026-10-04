using UnityEngine;

namespace NivalisModKit;

/// <summary>
/// The game's rendering layers, by name. For cameras (cullingMask), raycasts and finding objects. Layers named CULL_n
/// are props the game's own camera hides beyond n metres.
/// </summary>
[Experimental("New in 0.4.")]
public static class Layers
{
    /// <summary>Unity's default layer: most of the world.</summary>
    public const string Default = "Default";
    /// <summary>NPCs.</summary>
    public const string Character = "Character";
    /// <summary>The player's character.</summary>
    public const string Player = "Player";
    /// <summary>Cars, carts and other traffic.</summary>
    public const string Vehicle = "Vehicle";
    /// <summary>Boats.</summary>
    public const string Boat = "Boat";
    /// <summary>Overhead wires.</summary>
    public const string Wires = "Wires";
    /// <summary>Floors.</summary>
    public const string Floor = "Floor";
    /// <summary>Walls.</summary>
    public const string Wall = "Wall";
    /// <summary>Items lying in the world.</summary>
    public const string Item = "Item";
    /// <summary>Furniture the player can move.</summary>
    public const string MoveableFurniture = "MoveableFurniture";
    /// <summary>Things the player can interact with.</summary>
    public const string Interactable = "Interactable";
    /// <summary>Screen UI.</summary>
    public const string UI = "UI";
    /// <summary>UI floating in the world (labels, markers).</summary>
    public const string WorldUI = "WorldUI";

    /// <summary>The layer's number, or -1 if the game has no layer of that name.</summary>
    public static int Number(string name) => LayerMask.NameToLayer(name);

    /// <summary>A layer mask of the named layers (unknown names are skipped).</summary>
    public static int Mask(params string[] names)
    {
        int mask = 0;
        foreach (var n in names)
        {
            int layer = LayerMask.NameToLayer(n);
            if (layer >= 0) mask |= 1 << layer;
        }
        return mask;
    }

    /// <summary>Every layer except the named ones: a camera's culling mask that leaves them out.</summary>
    public static int AllExcept(params string[] names) => ~Mask(names);
}
