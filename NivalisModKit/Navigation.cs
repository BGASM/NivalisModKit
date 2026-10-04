using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AI;

namespace NivalisModKit;

/// <summary>The walkable surfaces NPCs path over (Unity's navigation mesh), and walking routes across them.</summary>
[Experimental("New in 0.4.")]
public static class Navigation
{
    /// <summary>A navigation mesh: triangles over the walkable ground.</summary>
    public sealed class Mesh
    {
        /// <summary>Corner positions, world metres.</summary>
        public Vector3[] Vertices { get; internal set; }
        /// <summary>Three vertex indices per triangle.</summary>
        public int[] Indices { get; internal set; }
        /// <summary>Each triangle's area type (0 is Unity's default "Walkable").</summary>
        public int[] Areas { get; internal set; }
        /// <summary>The space the mesh covers.</summary>
        public Bounds Bounds { get; internal set; }
        /// <summary>Number of triangles.</summary>
        public int Triangles => Indices.Length / 3;
    }

    // NavMesh.CalculateTriangulation was stripped from the game's build (it never calls it), and its result type with
    // it, but the engine still registers the native function. It fills a NavMeshTriangulation: three managed arrays,
    // vertices (Vector3[]), indices (int[], three per triangle) and areas (int[], one per triangle), in that order.
    [StructLayout(LayoutKind.Sequential)]
    struct RawTriangulation { public IntPtr Vertices, Indices, Areas; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void TriangulateFn(out RawTriangulation result);

    static TriangulateFn triangulate;

    /// <summary>
    /// The navigation mesh of everything loaded now (Unity merges every loaded scene's), or null if there is none. It
    /// can change slightly over time: moving obstacles cut temporary holes. Copies the mesh; don't call it every frame.
    /// </summary>
    public static Mesh Triangulate()
    {
        triangulate ??= IL2CPP.ResolveICall<TriangulateFn>("UnityEngine.AI.NavMesh::CalculateTriangulation_Injected");
        triangulate(out var raw);
        if (raw.Vertices == IntPtr.Zero || raw.Indices == IntPtr.Zero) return null;
        var srcV = new Il2CppStructArray<Vector3>(raw.Vertices);
        var srcI = new Il2CppStructArray<int>(raw.Indices);
        if (srcV.Length == 0 || srcI.Length < 3) return null;

        // Copied out once: reading the game's arrays element by element across the interop boundary is slow.
        var v = new Vector3[srcV.Length];
        for (int i = 0; i < v.Length; i++) v[i] = srcV[i];
        var idx = new int[srcI.Length - srcI.Length % 3];
        for (int i = 0; i < idx.Length; i++) idx[i] = srcI[i];
        var areas = new int[idx.Length / 3];
        if (raw.Areas != IntPtr.Zero)
        {
            var srcA = new Il2CppStructArray<int>(raw.Areas);
            for (int i = 0; i < areas.Length && i < srcA.Length; i++) areas[i] = srcA[i];
        }
        Vector3 min = v[0], max = v[0];
        foreach (var p in v) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return new Mesh { Vertices = v, Indices = idx, Areas = areas, Bounds = bounds };
    }

    /// <summary>
    /// A walking route from <paramref name="from"/> to <paramref name="to"/> across the navigation mesh: its corners,
    /// start to end. Null if there's no route. Both points should be on or near walkable ground (see <see cref="Nearest"/>).
    /// </summary>
    public static Vector3[] Path(Vector3 from, Vector3 to)
    {
        try
        {
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status == NavMeshPathStatus.PathInvalid) return null;
            var corners = path.corners;
            if (corners == null || corners.Length == 0) return null;
            var result = new Vector3[corners.Length];
            for (int i = 0; i < result.Length; i++) result[i] = corners[i];
            return result;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Navigation.Path: {e.Message}");
            return null;
        }
    }

    /// <summary>The nearest point on walkable ground within <paramref name="maxDistance"/> metres, or null.</summary>
    public static Vector3? Nearest(Vector3 position, float maxDistance = 5f)
    {
        try { return NavMesh.SamplePosition(position, out var hit, maxDistance, NavMesh.AllAreas) ? hit.position : null; }
        catch { return null; }
    }
}
