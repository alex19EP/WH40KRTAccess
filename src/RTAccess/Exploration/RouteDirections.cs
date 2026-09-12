using Kingmaker;                                          // Game
using Kingmaker.EntitySystem.Entities;                   // BaseUnitEntity, StarshipEntity
using Kingmaker.Pathfinding;                             // PathfindingService, WarhammerPathPlayer, PathDisposable, CustomGridNodeBase
using Kingmaker.RuleSystem;                               // Rulebook
using Kingmaker.RuleSystem.Rules;                         // RuleCalculateMovementCost (the game's own per-turn cut)
using Pathfinding;                                        // GraphNode, PathCompleteState
using RTAccess.Accessibility;                             // InteractableDescriber.Compass8
using UnityEngine;                                        // Mathf, Vector3

namespace RTAccess.Exploration;

/// <summary>
/// The walked route to a tile, spoken as directions — "Route, 20 tiles: 3 north, 2 east, 15 west" (September
/// 2026 tester item 7). Every other spatial readout is a straight-line projection ("15 tiles, north-west, 6 north
/// 3 east"), which is the bearing of the destination, not the way there: around a wall the walk turns, and a blind
/// player following the bearing walks into the wall. The engine already computes the walk — this only reads it
/// and compresses consecutive same-direction steps into legs.
///
/// <para>Two sources, the same shape the move itself uses. A cell inside this turn's movable area comes from the
/// game's own reachable set (its per-cell parent chain — <see cref="PathInfo.ReachableChain"/>, exactly the walk
/// the planted move takes). Anything else — out of movement range, out of combat — comes from an unbudgeted
/// turn-based pathfind (<c>FindPathTB_Blocking(limitRangeByActionPoints: false)</c>, the call the game's own
/// scripted movers use for "walk there whatever it costs"), and on the player's own turn the game's movement-cost
/// rule (<see cref="RuleCalculateMovementCost"/>, the rule that cuts the on-screen path line at the budget) says
/// how far along it this turn reaches. A ladder / node-link hop in the chain is not a step in any direction, so
/// it reads as the level change it is ("up 3 metres").</para>
///
/// <para>Fog parity: a never-seen destination gets no route at all — the pathfinder knows the whole level, and
/// a route through unrevealed ground would narrate layout a sighted player has not uncovered.</para>
/// </summary>
internal static class RouteDirections
{
    /// <summary>The spoken route from <paramref name="unit"/> to <paramref name="dest"/>; a localized reason
    /// ("no route", "you are here", "unexplored") when there is none. Never throws.</summary>
    public static string Describe(BaseUnitEntity unit, CustomGridNodeBase dest)
    {
        try
        {
            if (unit == null || dest == null || unit.View == null) return Loc.T("route.none");
            if (unit is StarshipEntity ship) return ShipPathInfo.Preview(ship, dest, out _);
            if (dest == unit.CurrentUnwalkableNode) return Loc.T("path.preview.here");
            if (FogProbe.Classify(dest.Vector3Position) == FogProbe.FogState.NeverSeen) return Loc.T("route.unexplored");

            var tc = Game.Instance?.TurnController;
            bool ownTurn = tc != null && tc.TurnBasedModeActive && tc.IsPlayerTurn && ReferenceEquals(tc.CurrentUnit, unit);
            int budget = ownTurn ? Mathf.RoundToInt(unit.CombatState.ActionPointsBlue) : -1;

            // 1. Inside this turn's blue highlight: the reachable set's own chain — the walk the move takes.
            if (ownTurn)
            {
                var chain = PathInfo.ReachableChain(unit, dest, out float chainCost);
                if (chain != null) return Compose(chain, Mathf.RoundToInt(chainCost), budget, null);
            }

            // 2. Everywhere else: an unbudgeted pathfind, released back to the pool like every game caller does.
            var agent = unit.View.MovementAgent;
            var svc = PathfindingService.Instance;
            if (agent == null || svc == null) return Loc.T("route.none");
            var path = svc.FindPathTB_Blocking(agent, dest.Vector3Position, limitRangeByActionPoints: false);
            if (path == null) return Loc.T("route.none");
            using (PathDisposable<WarhammerPathPlayer>.Get(path, unit))
            {
                var nodes = path.path;
                if (path.error || nodes == null || nodes.Count < 2) return Loc.T("route.none");
                var walk = new List<GraphNode>(nodes);   // copy: the pooled path is recycled after the using
                var cells = path.CalculatedPath;
                int cost = cells != null && cells.Length == nodes.Count ? Mathf.RoundToInt(cells[nodes.Count - 1].Length) : -1;

                // Own turn, out of reach: how far along the walk this turn's movement gets — the game's own cut.
                string shortfall = null;
                if (ownTurn)
                {
                    var rule = Rulebook.Trigger(new RuleCalculateMovementCost(unit, path));
                    int reach = Mathf.Max(0, rule.ResultPointCount - 1);
                    int total = nodes.Count - 1;
                    if (reach < total) shortfall = Loc.T("route.shortfall", new { n = reach, total });
                }

                var line = Compose(walk, cost, ownTurn ? budget : -1, shortfall);
                // Partial = the search ran out of open cells short of the destination (a different walkable
                // island): the chain ends at the closest cell it could reach, so say so rather than call it a route.
                return path.CompleteState == PathCompleteState.Complete ? line : Loc.T("route.partial", new { route = line });
            }
        }
        catch (Exception e)
        {
            Main.Log?.Error("RouteDirections.Describe failed: " + e);
            return Loc.T("route.none");
        }
    }

    /// <summary>The legs alone — "3 north, 2 east, up 3 metres, 15 west" — for callers that speak their own
    /// headline (the firing-position readouts). Null when the chain has fewer than two nodes.</summary>
    public static string LegsOnly(List<GraphNode> nodes)
    {
        var legs = Legs(nodes);
        return legs.Count > 0 ? string.Join(", ", legs) : null;
    }

    /// <summary>The legs of the unbudgeted walk from <paramref name="unit"/> to <paramref name="dest"/>, or null.
    /// The one-line form of <see cref="Describe"/> for a headline that already says where and how far.</summary>
    public static string LegsTo(BaseUnitEntity unit, CustomGridNodeBase dest)
    {
        try
        {
            if (unit?.View == null || dest == null || dest == unit.CurrentUnwalkableNode) return null;
            var tc = Game.Instance?.TurnController;
            if (tc != null && tc.TurnBasedModeActive && tc.IsPlayerTurn && ReferenceEquals(tc.CurrentUnit, unit))
            {
                var chain = PathInfo.ReachableChain(unit, dest, out _);
                if (chain != null) return LegsOnly(chain);
            }
            var agent = unit.View.MovementAgent;
            var svc = PathfindingService.Instance;
            if (agent == null || svc == null) return null;
            var path = svc.FindPathTB_Blocking(agent, dest.Vector3Position, limitRangeByActionPoints: false);
            if (path == null) return null;
            using (PathDisposable<WarhammerPathPlayer>.Get(path, unit))
            {
                if (path.error || path.path == null || path.CompleteState != PathCompleteState.Complete) return null;
                return LegsOnly(new List<GraphNode>(path.path));
            }
        }
        catch (Exception e) { Main.Log?.Error("RouteDirections.LegsTo failed: " + e); return null; }
    }

    private static string Compose(List<GraphNode> nodes, int cost, int budget, string shortfall)
    {
        int tiles = nodes.Count - 1;
        var sb = new System.Text.StringBuilder(Loc.T("route.line", new
        {
            tiles,
            tileword = Loc.T(tiles == 1 ? "path.preview.tile_one" : "path.preview.tile_many"),
            legs = LegsOnly(nodes) ?? "",
        }));
        if (cost >= 0 && budget >= 0) sb.Append(", ").Append(Loc.T("route.cost", new { cost, budget }));
        if (shortfall != null) sb.Append(", ").Append(shortfall);
        return sb.ToString();
    }

    /// <summary>Run-length-compress consecutive same-direction steps into "N direction" legs. Direction comes from
    /// the WORLD delta between consecutive cells (the grid is axis-aligned: +X east, +Z north, the same convention
    /// the tile cursor steps by), through the shared 8-way compass so diagonals read "3 north-east". A hop longer
    /// than a cell is a node link (ladder, hatch, jump-down) and reads as its level change.</summary>
    private static List<string> Legs(List<GraphNode> nodes)
    {
        var legs = new List<string>();
        if (nodes == null || nodes.Count < 2) return legs;
        float cell = GraphParamsMechanicsCache.GridCellSize;
        float hop = cell * 1.5f;
        int runDir = -1, runCount = 0;
        for (int i = 1; i < nodes.Count; i++)
        {
            var a = nodes[i - 1];
            var b = nodes[i];
            if (a == null || b == null) continue;
            Vector3 pa = a.Vector3Position, pb = b.Vector3Position;
            float dx = pb.x - pa.x, dz = pb.z - pa.z;
            if (Mathf.Abs(dx) > hop || Mathf.Abs(dz) > hop)
            {
                Flush(legs, ref runDir, ref runCount);
                legs.Add(Geo.Vertical(pa, pb) ?? Loc.T("route.passage"));
                continue;
            }
            if (!Geo.CompassSector(dx, dz, out int dir, cell * 0.25f)) continue;   // same cell twice: skip
            if (dir == runDir) { runCount++; continue; }
            Flush(legs, ref runDir, ref runCount);
            runDir = dir;
            runCount = 1;
        }
        Flush(legs, ref runDir, ref runCount);
        return legs;
    }

    private static void Flush(List<string> legs, ref int dir, ref int count)
    {
        if (dir >= 0 && count > 0)
            legs.Add(Loc.T("geo.offset", new { count, dir = Loc.T(InteractableDescriber.Compass8[dir]) }));
        dir = -1;
        count = 0;
    }
}
