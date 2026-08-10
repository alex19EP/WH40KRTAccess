using Kingmaker.Pathfinding;   // GetNearestNodeXZ (GridAreaHelper), CustomGridNodeBase, CustomGridGraph
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// One home for the grid-node queries the explorer, the shared <see cref="MapCursor"/>, and (later) the overlay
/// systems all need on RT's square <see cref="CustomGridGraph"/>: the node nearest a world point, a cardinal
/// neighbour step, an on-mesh test, and a level-aware walkable lookup. The graph is single-LAYER (one node per XZ
/// column) but emphatically not single-LEVEL: RT stacks floors as XZ-disjoint platforms at different heights, so a
/// height-blind query silently answers about the wrong floor (see <see cref="WalkableNodeOnLevel"/>). Pure grid math
/// with no behaviour of its own: it only centralises the
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

    /// <summary>
    /// The nearest WALKABLE node to <paramref name="p"/> that is also on <paramref name="p"/>'s own level — i.e.
    /// within <paramref name="yTolerance"/> metres of its height. Null when no such node exists.
    ///
    /// Why this exists: the engine's nearest-node lookup is XZ-only (<c>NNConstraint.distanceXZ = true</c>, and
    /// <c>CustomGridGraph.GetNearest</c> indexes <c>nodes[z*width + x]</c>), and the graph stores exactly ONE node
    /// per column — the TOPMOST walkable surface. So in a multi-level area anything standing under a catwalk snaps
    /// to the catwalk. Measured in ForgeWorld (Kiava Gamma): 72 of 215 map objects resolve to a node more than
    /// 1.5 m off in height, one of them by 16 m. Any component/reachability question asked through the plain
    /// lookup is therefore answering about the wrong floor.
    ///
    /// This walks a small square spiral around the XZ hit (the same radius the game's own
    /// <c>ObstacleAnalyzer.FindNearestNodeOnLevel</c> uses) and keeps the closest candidate that is BOTH walkable
    /// and on-level. We do not call the game's version: it searches with a constraint whose
    /// <c>constrainWalkability</c> is false, so it can hand back unwalkable cells, whose connected-component id is
    /// not meaningful.
    /// </summary>
    public static CustomGridNodeBase WalkableNodeOnLevel(Vector3 p, float yTolerance, int radius = 4)
    {
        var seed = NodeAt(p);
        if (seed == null) return null;
        var graph = seed.Graph as CustomGridGraph;
        if (graph == null) return null;

        CustomGridNodeBase best = null;
        float bestSqr = float.MaxValue;
        int cx = seed.XCoordinateInGrid, cz = seed.ZCoordinateInGrid;
        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                var n = graph.GetNode(cx + dx, cz + dz);
                if (n == null || !n.Walkable) continue;
                var v = n.Vector3Position;
                if (Mathf.Abs(v.y - p.y) > yTolerance) continue;
                float ddx = v.x - p.x, ddz = v.z - p.z;
                float sqr = ddx * ddx + ddz * ddz;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = n;
            }
        }
        return best;
    }

    /// <summary>
    /// Does any walkable, on-level node within <paramref name="radius"/> cells of <paramref name="p"/> belong to
    /// connected component <paramref name="area"/>? <paramref name="anyWalkable"/> reports whether a walkable
    /// on-level node was found AT ALL, so the caller can tell "on another island" from "not on the graph".
    ///
    /// This is the reach question, not the nearest-node question. Interactables sit off-grid and often ON the
    /// divide between two islands — a door in its frame, a climb point at a ledge lip — so
    /// <see cref="WalkableNodeOnLevel"/>'s single closest node is a coin toss between the two sides, and picking
    /// the far one reported a doorway you are standing in as being on another level. What actually decides
    /// whether the party can walk to a thing is whether there is standing room NEXT TO it on their own island,
    /// which is exactly what this asks. Non-allocating: it runs per listed thing per keypress.
    /// </summary>
    public static bool AnyWalkableOnLevel(Vector3 p, float yTolerance, uint area, out bool anyWalkable, int radius = 2)
    {
        anyWalkable = false;
        var seed = NodeAt(p);
        var graph = seed?.Graph as CustomGridGraph;
        if (graph == null) return false;

        int cx = seed.XCoordinateInGrid, cz = seed.ZCoordinateInGrid;
        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                var n = graph.GetNode(cx + dx, cz + dz);
                if (n == null || !n.Walkable) continue;
                if (Mathf.Abs(n.Vector3Position.y - p.y) > yTolerance) continue;
                anyWalkable = true;
                if (n.Area == area) return true;
            }
        }
        return false;
    }

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
