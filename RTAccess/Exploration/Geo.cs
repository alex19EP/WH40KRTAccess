using Kingmaker.EntitySystem.Entities; // MechanicEntity (Live view position)
using Kingmaker.Pathfinding; // GetNearestNodeXZ extension (GridAreaHelper)
using Kingmaker.View;        // ObstacleAnalyzer
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// Small spatial-readout helper shared by the scanner: planar XZ distance (for sorting), navmesh
/// connected-component reachability, an on-mesh test, and a 3x3 compass region word. Bearing/distance
/// announcement strings are produced by <see cref="RTAccess.Accessibility.InteractableDescriber"/> so the
/// scanner speaks the same compass as the other navigators; this type owns only the math that has no home there.
///
/// Reachability mirrors the game's own cross-area block: two world points are mutually walkable iff their
/// nearest navmesh nodes share an <c>Area</c> (connected component) — <see cref="ObstacleAnalyzer.GetArea"/>
/// returns the sentinel <see cref="NoArea"/> when no node is near, which we treat as "don't block".
/// </summary>
internal static class Geo
{
    // ObstacleAnalyzer.GetArea's sentinel when no node is near (decompiled: GetNearestNode(pos).node?.Area ?? 999999).
    private const uint NoArea = 999999u;

    /// <summary>The entity's live VIEW position — the interpolated transform the player sees — rather than the
    /// possibly-lagged logical <see cref="MechanicEntity.Position"/> (which can snap to the node mid-move), so
    /// bearings/distances stay accurate while a unit is walking. Falls back to the logical position when no view
    /// is present (off-screen / not yet spawned).</summary>
    public static Vector3 Live(MechanicEntity e)
    {
        if (e == null) return Vector3.zero;
        var view = e.View;
        return view != null && view.ViewTransform != null ? view.ViewTransform.position : e.Position;
    }

    /// <summary>Flat XZ distance in metres — the metric the scanner sorts and the siblings speak.</summary>
    public static float Distance(Vector3 from, Vector3 to)
    {
        float dx = to.x - from.x, dz = to.z - from.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>True when a and b are mutually reachable (share a navmesh connected component). A point off
    /// the mesh (NoArea) is treated as same, so an unclassifiable snap never wrongly blocks; callers that must
    /// not path onto off-mesh points gate on <see cref="OnNavmesh"/> first.</summary>
    public static bool SameArea(Vector3 a, Vector3 b)
    {
        uint ar = ObstacleAnalyzer.GetArea(a), br = ObstacleAnalyzer.GetArea(b);
        return ar == NoArea || br == NoArea || ar == br;
    }

    /// <summary>Is this point on walkable ground? — its nearest grid node exists and lies within ~2 m on the
    /// XZ plane (the same tolerance LandmarkNav uses to decide a point is "really on-mesh").</summary>
    public static bool OnNavmesh(Vector3 p)
    {
        var node = p.GetNearestNodeXZ();
        if (node == null) return false;
        var d = node.Vector3Position - p;
        d.y = 0f;
        return d.sqrMagnitude <= 4f;
    }

    /// <summary>A 3x3 grid over the area bounds -> "centre" or a compass word (+Z = north, +X = east).
    /// <paramref name="fx"/>/<paramref name="fz"/> are the fractional position within the bounds (0..1).</summary>
    public static string RegionWord(float fx, float fz)
    {
        int col = fx < 1f / 3f ? -1 : fx > 2f / 3f ? 1 : 0;
        int row = fz < 1f / 3f ? -1 : fz > 2f / 3f ? 1 : 0;
        if (col == 0 && row == 0) return "centre";
        if (row > 0) return col < 0 ? "north-west" : col > 0 ? "north-east" : "north";
        if (row < 0) return col < 0 ? "south-west" : col > 0 ? "south-east" : "south";
        return col < 0 ? "west" : "east";
    }
}
