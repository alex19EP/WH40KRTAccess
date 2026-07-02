using Kingmaker.Pathfinding;   // GetNearestNodeXZ (GridAreaHelper), CustomGridNodeBase, CustomGridGraph
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// One home for the grid-node queries the explorer, the shared <see cref="MapCursor"/>, and (later) the overlay
/// systems all need on RT's square <see cref="CustomGridGraph"/>: the node nearest a world point, a cardinal
/// neighbour step, and an on-mesh test. RT surface areas are planar single-level grids, so — unlike WrathAccess's
/// navmesh <c>NavmeshProbe</c> — there is no vertical / floor-above-below sampling; the name is kept for parity with
/// the plan (echoing Appendix B6). Pure grid math with no behaviour of its own: it only centralises the
/// <c>GetNearestNodeXZ</c> / <c>CustomGridGraph.GetNode</c> / on-mesh-tolerance snippets that <see cref="MapCursor"/>,
/// <c>TileExplorer</c>, and <see cref="Geo"/> previously each spelled out inline.
/// </summary>
internal static class NavmeshProbe
{
    // How far (XZ, metres²) the snapped node may sit from a query point before it counts as off-mesh — the ~2 m
    // tolerance Geo used inline (a tile centre microscopically off the grid still reads as on-mesh).
    private const float OnMeshXZSqr = 4f;

    /// <summary>The grid node nearest a world point (XZ), or null when the point is off-graph. <c>GetNearestNodeXZ</c>
    /// already returns a <see cref="CustomGridNodeBase"/>, so this is the one canonical spelling of that query.</summary>
    public static CustomGridNodeBase NodeAt(Vector3 worldPos) => worldPos.GetNearestNodeXZ();

    /// <summary>The cardinal-neighbour node one step from <paramref name="node"/> (dx = +east / −west, dz = +north /
    /// −south), or null at the graph edge or when the node isn't on a grid graph.</summary>
    public static CustomGridNodeBase Neighbour(CustomGridNodeBase node, int dx, int dz)
        => (node?.Graph as CustomGridGraph)?.GetNode(node.XCoordinateInGrid + dx, node.ZCoordinateInGrid + dz);

    /// <summary>Is <paramref name="p"/> on walkable ground? — its nearest grid node exists and lies within ~2 m on the
    /// XZ plane (the tolerance for "really on-mesh"). <paramref name="node"/> is the snapped node when true.</summary>
    public static bool OnMesh(Vector3 p, out CustomGridNodeBase node)
    {
        node = NodeAt(p);
        if (node == null) return false;
        var d = node.Vector3Position - p; d.y = 0f;
        return d.sqrMagnitude <= OnMeshXZSqr;
    }
}
