using Il2CppInterop.Runtime;
using Nivalis;
using UnityEngine;

namespace NivalisModKit;

/// <summary>The player in the world: where they are and where they're looking.</summary>
[Experimental("New in 0.4.")]
public static class Player
{
    static Camera camera;
    static float cameraCheckAt;

    /// <summary>The player character's transform, or null outside gameplay.</summary>
    public static Transform Transform
    {
        get
        {
            try { return Singleton<PlayerManager>.InstanceExist(out var pm) ? pm.LocalPlayer?.PlayerGameObject?.transform : null; }
            catch { return null; }
        }
    }

    /// <summary>The player's position, or <see cref="Vector3.zero"/> outside gameplay.</summary>
    public static Vector3 Position => Transform?.position ?? Vector3.zero;

    /// <summary>
    /// Where the player is looking, as a compass heading in degrees (0 = north/+z, 90 = east/+x): the game camera's,
    /// since the character's body doesn't turn when the player looks around.
    /// </summary>
    public static float Heading
    {
        get
        {
            var cam = Camera;
            if (cam != null) return cam.transform.eulerAngles.y;
            var t = Transform;
            return t != null ? t.eulerAngles.y : 0f;
        }
    }

    /// <summary>
    /// The camera drawing the game (Unity's Camera.main doesn't find it): enabled, drawing to the screen, drawing the
    /// world, and the last of those to draw. Rechecked once a second.
    /// </summary>
    public static Camera Camera
    {
        get
        {
            if (camera != null && Time.unscaledTime < cameraCheckAt) return camera;
            cameraCheckAt = Time.unscaledTime + 1f;
            Camera best = null;
            foreach (var o in Object.FindObjectsOfType(Il2CppType.Of<Camera>()))
            {
                var c = o.TryCast<Camera>();
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || c.targetTexture != null) continue;
                if ((c.cullingMask & 1) == 0) continue;   // draws the Default layer: the world, not a UI camera
                if (best == null || c.depth > best.depth) best = c;
            }
            camera = best;
            return best;
        }
    }
}
