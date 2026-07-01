using Kingmaker;                                          // Game
using Kingmaker.EntitySystem.Entities;                   // BaseUnitEntity
using Kingmaker.Pathfinding;                             // PathfindingService, WarhammerPathPlayerCell, CustomGridNodeBase
using Pathfinding;                                        // GraphNode

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
        if (unit == null || dest == null || unit.View == null) return "Can't reach that tile.";

        // The game's authoritative reachable set for the current unit — null/empty when the unit has no movement this
        // turn (spent out, CantMove, not a controllable turn). Covers every "why can't I move at all" case for free.
        var area = Game.Instance?.UnitMovableAreaController?.CurrentUnitMovableArea;
        if (area == null || area.Count == 0) return "Out of movement.";

        if (!area.Contains(dest))
            return dest.Walkable ? "Out of range." : "Blocked.";

        // Reachable per the game; price it from our own reachable-tiles pathfind (the controller threw its costs away).
        var dict = PathfindingService.Instance?.FindAllReachableTiles_Blocking(
            unit.View.MovementAgent, unit.Position, unit.CombatState.ActionPointsBlue);
        if (dict == null || !dict.TryGetValue(dest, out var cell))
        {
            canMove = true;                       // in the game's set but not priced — still a legal move
            return "Reachable.";
        }
        if (!cell.IsCanStand) return "Occupied, can't stop there.";

        int tiles = TileCount(dest, dict);
        if (tiles == 0) return "You are here.";
        canMove = true;
        return tiles == 1 ? "Reachable, 1 tile." : "Reachable, " + tiles + " tiles.";
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
