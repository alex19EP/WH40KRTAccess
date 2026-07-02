using Kingmaker;                                          // Game
using Kingmaker.EntitySystem.Entities;                   // BaseUnitEntity
using Kingmaker.Pathfinding;                             // PathfindingService, WarhammerPathPlayerCell, CustomGridNodeBase
using Kingmaker.UnitLogic;                                // CalculateAttackOfOpportunity (AttackOfOpportunityHelper ext)
using Pathfinding;                                        // GraphNode
using UnityEngine;                                        // Mathf

namespace RTAccess.Exploration;

/// <summary>
/// The path/reachability half of Phase D — the speech-only "can I move there, and how far" readout for turn-based
/// combat, where movement is a limited resource (movement points) on the square grid. A blind player planning a move
/// has none of the sighted overlay (the coloured reachable-tiles highlight + the on-cursor path-cost pip), so this
/// re-derives the same two facts from the game's own data and phrases them for the tile cursor.
///
/// Reachability comes from the game's OWN cached reachable set — <see
/// cref="Kingmaker.Controllers.Units.UnitMovableAreaController.CurrentUnitMovableArea"/> — which the controller
/// recomputes on every movement-point change with the authoritative formula (CantMove / pet / action-points-blue),
/// so we never duplicate that logic or subscribe to its push. The cost/tile-count then comes from our own
/// <see cref="PathfindingService.FindAllReachableTiles_Blocking"/> over the same unit (the controller keeps only the
/// node keys of its own call and discards the per-cell <see cref="WarhammerPathPlayerCell"/> we need for the length
/// and the parent chain). Both read the same graph node instances, so the cursor node keys straight into both.
///
/// This is invoked at the move-to decision point (<see cref="RTAccess.Accessibility.TileExplorer"/>'s first,
/// arming press) — the moment the player is about to commit a combat move — so the preview costs one pathfind per
/// deliberate key press, the same call the game itself runs on every point change. When the Phase E overlay
/// framework lands, its arm-on-cursor-stop auto-announce reuses <see cref="Preview"/> behind a toggle.
/// </summary>
internal static class PathInfo
{
    /// <summary>
    /// Describe the move from <paramref name="unit"/> to the <paramref name="dest"/> tile. <paramref name="canMove"/>
    /// is set true only when the tile is a legal destination the unit can reach and stand on this turn — the caller
    /// appends its "press again to move" confirm hint only then. Every other state (out of movement, out of range,
    /// blocked, occupied, already here) returns a spoken reason with <paramref name="canMove"/> false. Reachability
    /// is authoritative (the game's own set); the tile count is best-effort (falls back to a bare "Reachable." if the
    /// cost pathfind can't price this exact node — e.g. a pet's master-relative origin).
    /// </summary>
    public static string Preview(BaseUnitEntity unit, CustomGridNodeBase dest, out bool canMove)
    {
        canMove = false;
        if (unit == null || dest == null || unit.View == null) return Loc.T("path.preview.cant_reach");

        // The game's authoritative reachable set for the current unit — null/empty when the unit has no movement this
        // turn (spent out, CantMove, not a controllable turn). Covers every "why can't I move at all" case for free.
        var area = Game.Instance?.UnitMovableAreaController?.CurrentUnitMovableArea;
        if (area == null || area.Count == 0) return Loc.T("path.preview.out_of_movement");

        if (!area.Contains(dest))
            return Loc.T(dest.Walkable ? "path.preview.out_of_range" : "path.preview.blocked");

        // Reachable per the game; price it from our own reachable-tiles pathfind (the controller threw its costs away).
        var dict = PathfindingService.Instance?.FindAllReachableTiles_Blocking(
            unit.View.MovementAgent, unit.Position, unit.CombatState.ActionPointsBlue);
        if (dict == null || !dict.TryGetValue(dest, out var cell))
        {
            canMove = true;                       // in the game's set but not priced — still a legal move
            return Loc.T("path.preview.reachable_bare");
        }
        if (!cell.IsCanStand) return Loc.T("path.preview.occupied");

        int tiles = TileCount(dest, dict);
        if (tiles == 0) return Loc.T("path.preview.here");
        canMove = true;

        // The hop count answers "how many steps"; the movement-point cost is the real budget number (it diverges
        // from the hop count on diagonals and through threatened cells), so speak both. cell.Length is the
        // accumulated MP cost of THIS path; ActionPointsBlueMax is the unit's total movement for the turn.
        int cost = Mathf.RoundToInt(cell.Length);
        int budget = Mathf.RoundToInt(unit.CombatState.ActionPointsBlueMax);
        string tileword = Loc.T(tiles == 1 ? "path.preview.tile_one" : "path.preview.tile_many");
        string line = Loc.T("path.preview.reachable", new { tiles, tileword, cost, budget });

        // Attack-of-opportunity warning: the exact call the game's own move prediction runs. Leaving an enemy's
        // threatened tile provokes; the API self-filters to combat, so out of combat it yields nothing. Name the
        // attacker(s) so the player can weigh the risk before committing (a second press still commits).
        var attackers = unit.CalculateAttackOfOpportunity(PathNodes(dest, dict))
                            .Select(a => a.Attacker).Where(a => a != null).Distinct().ToList();
        if (attackers.Count > 0)
            line += " " + Loc.T("path.preview.provokes", new { names = string.Join(", ", attackers.Select(a => a.CharacterName)) });

        return line;
    }

    /// <summary>The traversed node list origin→dest, from the priced dict's parent chain — fed to the engine's
    /// path-AoO API. Includes the origin (the AoO check compares consecutive nodes, so it needs the full walk).</summary>
    private static List<GraphNode> PathNodes(GraphNode dest, Dictionary<GraphNode, WarhammerPathPlayerCell> dict)
    {
        var nodes = new List<GraphNode>();
        var node = dest;
        for (int guard = 0; guard < 1024; guard++)
        {
            if (node == null || !dict.TryGetValue(node, out var cell)) break;
            nodes.Add(node);
            var parent = cell.ParentNode;
            if (parent == null || parent == node) break;   // reached the origin
            node = parent;
        }
        nodes.Reverse();   // origin → dest
        return nodes;
    }

    /// <summary>Number of grid steps from the origin to <paramref name="dest"/> — walk the per-cell parent chain back
    /// to the origin (whose parent is null) and count the hops. Capped so a malformed chain can never spin.</summary>
    private static int TileCount(GraphNode dest, Dictionary<GraphNode, WarhammerPathPlayerCell> dict)
    {
        int hops = 0;
        var node = dest;
        for (int guard = 0; guard < 1024; guard++)
        {
            if (node == null || !dict.TryGetValue(node, out var cell)) break;
            var parent = cell.ParentNode;
            if (parent == null || parent == node) break;   // reached the origin
            node = parent;
            hops++;
        }
        return hops;
    }
}
