using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>How a listed thing relates to the ground the party is standing on.</summary>
internal enum ReachClass
{
    /// <summary>Same walkable island — the party can walk there without changing level.</summary>
    Walkable,
    /// <summary>On the graph, but in a different connected component: another floor / a platform reached by a
    /// ladder, hole, lift or jump. Not walkable-to from here, however close it sounds.</summary>
    Elsewhere,
    /// <summary>No walkable ground within tolerance of the thing's own height — decorative props, objects parked
    /// off the navmesh, anything we cannot honestly place. Never claimed as unreachable.</summary>
    Unknown,
}

/// <summary>
/// Answers "can the party actually walk to that?" — the single most load-bearing fact the scanner was missing in
/// multi-level areas.
///
/// RT bakes one <c>CustomGridGraph</c> per area with <c>maxClimb</c> = 1 m (measured in ForgeWorld), so two floors
/// 3 m apart share no connection and A* splits them into different connected components. Component identity is
/// therefore an exact, O(1) walkability test — <c>GraphNode.Area</c> is a plain field read into A*'s hierarchical
/// component table, the same one the game's own <c>ABPath.Prepare</c> and <c>AbilityTargetIsReachable</c> consult.
/// No pathfind required.
///
/// Why this matters: in the Kiava Gamma manufactorum the party stands on a 604-cell island while the main level is
/// a separate 4881-cell component. Measured there, 202 of 215 map objects were in a different component from the
/// party — so the scanner was reading out ~60 entries, of which about five could actually be reached, all sorted
/// by flat XZ distance as though they were equally available.
///
/// Deliberately conservative: an object we cannot place on the graph is <see cref="ReachClass.Unknown"/>, never
/// "unreachable". Mislabelling the one ladder that leads onward would be far worse than staying quiet, and the
/// height-blind lookup this class routes around gets a third of this area's objects wrong.
/// </summary>
internal static class Reachability
{
    // The party's component, recomputed at most once per frame: a category browse classifies every candidate in
    // one keypress, and they all share the same anchor.
    private static int _frame = -1;
    private static uint _partyArea;
    private static bool _partyKnown;

    /// <summary>Where the party is standing, as a walkable component id. False when the party itself cannot be
    /// placed (mid-transition, no graph) — callers then skip the readout entirely rather than guess.
    ///
    /// Anchored on <see cref="MapCursor.PlayerPosition"/> — the SELECTED unit, falling back to the main character
    /// — because that is the unit every distance in the same spoken line is measured from, and the unit that
    /// would do the walking. Reading the main character unconditionally made the two disagree the moment the
    /// party split across floors: a door the selected companion is standing in read "0 tiles, other level".</summary>
    private static bool PartyArea(out uint area)
    {
        int frame = Time.frameCount;
        if (frame != _frame)
        {
            _frame = frame;
            _partyKnown = false;
            try
            {
                // The anchor ENTITY, not MapCursor.PlayerPosition: that falls back to Vector3.zero with no anchor,
                // and the world origin is a real grid cell whose component id would be pure fiction.
                var anchor = MapCursor.Anchor();
                var node = anchor == null
                    ? null
                    : NavmeshProbe.WalkableNodeOnLevel(Geo.Live(anchor), Geo.LevelThreshold);
                if (node != null) { _partyArea = node.Area; _partyKnown = true; }
            }
            catch (Exception e) { Main.Log?.Error("Reachability.PartyArea failed: " + e); }
        }
        area = _partyArea;
        return _partyKnown;
    }

    /// <summary>Classify a world point against the party's walkable island: walkable when the point has standing
    /// room on that island beside it (see <see cref="NavmeshProbe.AnyWalkableOnLevel"/>), elsewhere when it has
    /// walkable ground of its own but none of it connected, unknown when it has none at all.</summary>
    public static ReachClass Classify(Vector3 point)
    {
        try
        {
            if (!PartyArea(out uint party)) return ReachClass.Unknown;
            if (NavmeshProbe.AnyWalkableOnLevel(point, Geo.LevelThreshold, party, out bool anyWalkable))
                return ReachClass.Walkable;
            return anyWalkable ? ReachClass.Elsewhere : ReachClass.Unknown;
        }
        catch (Exception e)
        {
            Main.Log?.Error("Reachability.Classify failed: " + e);
            return ReachClass.Unknown;
        }
    }

    /// <summary>
    /// Classify a KNOWN grid cell — an exact component comparison, with none of <see cref="Classify"/>'s
    /// neighbourhood tolerance.
    ///
    /// <see cref="Classify"/> takes a world POINT and answers "could the party get to something standing here",
    /// so it spirals a 5x5 window (<see cref="NavmeshProbe.AnyWalkableOnLevel"/>) — correct for a chest or a
    /// ladder, which sit off-grid and are reached from whatever floor is beside them. It is wrong for the tile
    /// cursor, which HAS the node: a single cell fenced off on every edge is its own connected component, but its
    /// neighbour one step across the fence is on the party's island and inside the window, so the tolerant test
    /// would call it reachable — silently, in precisely the case the player is asking about. Compare the cell's
    /// own <c>Area</c> instead, and speak only about the cell the cursor is actually on.
    /// </summary>
    public static ReachClass ClassifyNode(Pathfinding.GraphNode node)
    {
        try
        {
            if (node == null || !node.Walkable) return ReachClass.Unknown;
            if (!PartyArea(out uint party)) return ReachClass.Unknown;
            return node.Area == party ? ReachClass.Walkable : ReachClass.Elsewhere;
        }
        catch (Exception e)
        {
            Main.Log?.Error("Reachability.ClassifyNode failed: " + e);
            return ReachClass.Unknown;
        }
    }

    /// <summary>The word appended to a browse line, or null when there is nothing worth saying. Only the
    /// genuinely-unwalkable case speaks: "reachable" on every one of a dozen entries would be pure noise, and
    /// <see cref="ReachClass.Unknown"/> has nothing honest to claim.</summary>
    public static string Word(ReachClass reach)
        => reach == ReachClass.Elsewhere ? Loc.T("scan.other_level") : null;

    /// <summary>Sort rank: walkable things first, then unknown, then things on another level. Within a rank the
    /// caller's distance ordering still applies, so the nearest reachable thing stays first.</summary>
    public static int Rank(ReachClass reach)
        => reach == ReachClass.Walkable ? 0 : reach == ReachClass.Unknown ? 1 : 2;
}
