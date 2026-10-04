using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Nivalis;
using UnityEngine;

namespace NivalisModKit;

// Models and placed-object identity for Content items.
//
// Placed objects (furniture, decorations) are world ghosts saved with a prefab GUID; when a save loads,
// GhostManager.InstantiateGhostViewIfNeeded looks the GUID up in SerializationManager.Prefabs.prefabDictionary
// (GUID -> prefab) and spawns that. A copied item's prefab carries the template's GUID on its SerializableObject, so a
// placed copy would reload as the template. Each copy gets its own stable prefab GUID, registered in that dictionary.
//
// Model swap: the template prefab keeps its behaviour (placement volume, collider, audio, animation parents); its main
// visual mesh and materials are replaced with the bundle model's, the template's shadow mesh is hidden, the visual's
// own scale and rotation are neutralised so the model keeps the size it was built at, and box colliders are fitted.
internal static class ContentModels
{
    static readonly Dictionary<string, GameObject> prefabs = new();   // prefab GUID -> copied prefab
    static readonly Dictionary<string, AssetBundle> bundles = new(StringComparer.OrdinalIgnoreCase);

    internal static void Install(Harmony harmony)
    {
        // Saves are read after this; the copies must be findable by then.
        harmony.Patch(AccessTools.Method(typeof(SerializationManager), nameof(SerializationManager.Load)),
            prefix: new HarmonyMethod(typeof(ContentModels), nameof(RegisterPrefabs)));
    }

    // Gives a copied prefab its own identity (stable GUID) and remembers it for the prefab dictionary.
    internal static void Identify(GameObject prefab, string prefabGuid, string what)
    {
        var so = prefab.GetComponent<SerializableObject>() ?? prefab.GetComponentInChildren<SerializableObject>(true);
        if (so == null) return;   // not a placeable/saved object (most ingredients and dishes)
        so.prefabGuid = prefabGuid;
        prefabs[prefabGuid] = prefab;
        KitPlugin.L.LogInfo($"Content: {what} is placeable; prefab {prefabGuid}");
        RegisterPrefabs();
    }

    internal static void RegisterPrefabs()
    {
        if (prefabs.Count == 0) return;
        try
        {
            if (!Singleton<SerializationManager>.InstanceExist(out var sm) || sm == null) return;
            var dict = sm.Prefabs?.prefabDictionary;
            if (dict == null) return;
            foreach (var kv in prefabs) dict[kv.Key] = kv.Value;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Content: registering placeable prefabs: {e.Message}"); }
    }

    // ---------- model swap ----------

    internal static void ApplyModel(GameObject prefab, string bundlePath, string assetName, string what)
    {
        var model = LoadModel(bundlePath, assetName);
        if (model == null)
        {
            KitPlugin.L.LogWarning($"Content: {what}: no model '{assetName}' in {bundlePath}; keeping the template's look");
            return;
        }
        var source = model.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null);
        var sourceRenderer = source?.GetComponent<MeshRenderer>();
        if (source == null || sourceRenderer == null)
        {
            KitPlugin.L.LogWarning($"Content: {what}: '{assetName}' has no mesh");
            return;
        }

        // The template's main visual: its biggest mesh that isn't a shadow; shadow meshes are hidden.
        var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true)
            .Select(r => (r, f: r.GetComponent<MeshFilter>()))
            .Where(x => x.f != null && x.f.sharedMesh != null).ToList();
        foreach (var (r, _) in renderers.Where(x => x.r.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0))
            r.enabled = false;
        var visual = renderers.Where(x => x.r.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) < 0)
            .OrderByDescending(x => Volume(x.f.sharedMesh.bounds.size)).FirstOrDefault();
        if (visual.r == null)
        {
            KitPlugin.L.LogWarning($"Content: {what}: the template has no mesh to replace");
            return;
        }
        foreach (var (r, _) in renderers.Where(x => x.r != visual.r && x.r.enabled)) r.enabled = false;   // other parts of the template

        visual.f.sharedMesh = source.sharedMesh;
        visual.r.sharedMaterials = sourceRenderer.sharedMaterials;

        // Undo the template visual's own scale and rotation, so the model keeps the size and facing it was built with.
        var t = visual.r.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        var parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(Safe(1f / parentScale.x), Safe(1f / parentScale.y), Safe(1f / parentScale.z));

        FitColliders(prefab, t, source.sharedMesh.bounds);
        KitPlugin.L.LogInfo($"Content: {what}: model '{assetName}' ({source.sharedMesh.bounds.size.x:0.00} x {source.sharedMesh.bounds.size.y:0.00} x {source.sharedMesh.bounds.size.z:0.00} m)");
    }

    // Box colliders (including placement volumes) are resized to enclose the new mesh.
    static void FitColliders(GameObject prefab, Transform visual, Bounds meshBounds)
    {
        var corners = new List<Vector3>();
        for (int i = 0; i < 8; i++)
            corners.Add(visual.TransformPoint(new Vector3(
                (i & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                (i & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                (i & 4) == 0 ? meshBounds.min.z : meshBounds.max.z)));
        // Mesh colliders keep the template's shape: replace each with a box (same trigger setting), fitted below.
        foreach (var mesh in prefab.GetComponentsInChildren<MeshCollider>(true))
        {
            var box = mesh.gameObject.GetComponent<BoxCollider>() ?? mesh.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = mesh.isTrigger;
            mesh.enabled = false;
        }
        foreach (var box in prefab.GetComponentsInChildren<BoxCollider>(true))
        {
            var local = corners.Select(c => box.transform.InverseTransformPoint(c)).ToList();
            var min = new Vector3(local.Min(p => p.x), local.Min(p => p.y), local.Min(p => p.z));
            var max = new Vector3(local.Max(p => p.x), local.Max(p => p.y), local.Max(p => p.z));
            box.center = (min + max) / 2f;
            box.size = max - min;
        }
    }

    static GameObject LoadModel(string bundlePath, string assetName)
    {
        try
        {
            if (!bundles.TryGetValue(bundlePath, out var bundle) || bundle == null)
            {
                if (!File.Exists(bundlePath)) return null;
                bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null) return null;
                bundles[bundlePath] = bundle;
            }
            var obj = bundle.LoadAsset(assetName, Il2CppType.Of<GameObject>());
            return obj?.TryCast<GameObject>();
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Content: bundle {bundlePath}: {e.Message}"); return null; }
    }

    static float Volume(Vector3 s) => Mathf.Abs(s.x * s.y * s.z);
    static float Safe(float v) => float.IsInfinity(v) || float.IsNaN(v) ? 1f : v;
}
