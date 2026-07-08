using Kingmaker.EntitySystem.Entities; // MechanicEntity (Live view position)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// Small spatial-readout helper shared by the scanner: planar XZ distance (for sorting), an on-mesh test, a
/// walkable-snap march for landmark travel, and a 3x3 compass region word. Bearing/distance announcement strings
/// are produced by <see cref="RTAccess.Accessibility.InteractableDescriber"/> so the scanner speaks the same
/// compass as the other navigators; this type owns only the math that has no home there.
/// </summary>
internal static class Geo
{
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

    /// <summary>Map-relative 8-sector compass index (0 = north, 1 = north-east … 7 = north-west) from an
    /// east(+X) / north(+Z) delta. Returns false with <paramref name="sector"/> = 0 when the delta is shorter
    /// than <paramref name="minDist"/> (too close to carry a meaningful bearing); callers pass their own
    /// threshold and map the sector onto their own localized direction words. One home for the Atan2→sector
    /// math the focused readout, the system map and the AoE facing all shared.</summary>
    public static bool CompassSector(float dx, float dz, out int sector, float minDist = 0f)
    {
        sector = 0;
        if (dx * dx + dz * dz < minDist * minDist) return false;
        float angle = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg; // 0 = north (+Z), +90 = east (+X)
        sector = ((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8;
        return true;
    }

    /// <summary>
    /// March from <paramref name="from"/> toward <paramref name="target"/> ~2 m at a time and return the farthest
    /// point still on the navmesh (its nearest node within ~2 m), stopping at the first gap. Local-map landmark pins
    /// sit off the navmesh (far exits, floating markers), so targeting one directly makes the pathfinder report "no
    /// node near the end point" and the move is silently dropped; this instead heads as far toward the pin as
    /// continuous walkable floor allows. Returns <paramref name="from"/> when no floor leads toward the target
    /// (callers treat a near-zero advance as blocked). Used by the scanner's landmark travel.
    /// </summary>
    public static Vector3 SnapToWalkable(Vector3 target, Vector3 from)
    {
        float dist = Vector3.Distance(from, target);
        if (dist < 0.1f) return from;
        int steps = Mathf.Clamp(Mathf.RoundToInt(dist / 2f), 1, 200);
        Vector3 best = from;
        for (int i = 1; i <= steps; i++)
        {
            var p = Vector3.Lerp(from, target, (float)i / steps);
            if (!NavmeshProbe.OnMesh(p, out _)) break;        // off-graph, or nearest floor >2 m away — navmesh ended
            best = p;                                         // still on walkable floor; keep advancing
        }
        return best;
    }

    /// <summary>A 3x3 grid over the area bounds -> the localized "centre" word or a compass region
    /// ("{region} of the map"), +Z = north, +X = east. <paramref name="fx"/>/<paramref name="fz"/> are the
    /// fractional position within the bounds (0..1).</summary>
    public static string RegionWord(float fx, float fz)
    {
        int col = fx < 1f / 3f ? -1 : fx > 2f / 3f ? 1 : 0;
        int row = fz < 1f / 3f ? -1 : fz > 2f / 3f ? 1 : 0;
        if (col == 0 && row == 0) return Loc.T("where.center");
        string dir;
        if (row > 0) dir = col < 0 ? "geo.northwest" : col > 0 ? "geo.northeast" : "geo.north";
        else if (row < 0) dir = col < 0 ? "geo.southwest" : col > 0 ? "geo.southeast" : "geo.south";
        else dir = col < 0 ? "geo.west" : "geo.east";
        return Loc.T("where.region", new { region = Loc.T(dir) });
    }
}
