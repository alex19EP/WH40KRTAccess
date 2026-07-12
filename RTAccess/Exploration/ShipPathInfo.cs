using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;               // StarshipEntity
using Kingmaker.Pathfinding;                         // ShipPath, CustomGridNodeBase, CustomGraphHelper
using UnityEngine;                                   // Mathf

namespace RTAccess.Exploration;

/// <summary>
/// The starship counterpart of <see cref="PathInfo"/> — the "can I move there, and what happens" readout for
/// voidship turns (Phase 2 of docs/plans/inertial-broadsiding-tsiolkovsky.md). Ships do not use the surface
/// pathfind: movement is inertial (<c>PartStarshipNavigation</c> + <see cref="ShipPath"/>) — a cell is reached
/// ALONG a heading, so the useful answer is not just cost but the facing the ship ARRIVES with, whether it can
/// STOP there at all (pass-through cells exist), and whether the turn could END there (the finishing-window law
/// behind the "must keep moving" state). Everything reads the game's OWN cached reachable set —
/// <c>Navigation.ReachableTiles</c>, which the game's <c>StarshipPathController.Tick</c> recomputes whenever the
/// acting ship's position / movement points / heading change — and the cursor tile is quantized to the ship's
/// metagrid cell exactly the way the game's own click-to-move does (<c>GetNodeInMetagrid</c>, the same call
/// inside <c>FindPath</c>), so the spoken verdict always matches what a commit would actually do. Applies to any
/// acting <see cref="StarshipEntity"/>, torpedo salvos included (their turns run the same navigation part).
/// </summary>
internal static class ShipPathInfo
{
    // The grid direction convention (CustomGraphHelper.GuessDirection: 0=S 1=E 2=N 3=W 4=SE 5=NE 6=NW 7=SW)
    // mapped onto the shared Compass8 table (0=N, clockwise) so ship facings use the same localized words as
    // every other bearing the mod speaks.
    private static readonly int[] GridDirToCompass = { 4, 2, 0, 6, 3, 1, 7, 5 };

    // Reading order for the facing-grouped summary: ahead first, then the gentlest turns outward
    // (starboard before port at each angle), reverse last — tactical relevance order, not enum order.
    private static readonly int[] SummaryOrder = { 0, 1, 7, 2, 6, 3, 5, 4 };

    /// <summary>
    /// Describe the ship move to the <paramref name="dest"/> tile (same contract as
    /// <see cref="PathInfo.Preview"/>: <paramref name="canMove"/> true only for a committable destination).
    /// Speaks cost / budget, the arrival facing, whether the turn could end there, plus the dead-end state —
    /// the audible version of what the sighted path-marker fan + move ghost show on hover.
    /// </summary>
    public static string Preview(StarshipEntity ship, CustomGridNodeBase dest, out bool canMove)
    {
        canMove = false;
        var nav = ship?.Navigation;
        var path = nav?.ReachableTiles;
        if (dest == null || path?.Result == null || path.Result.Count == 0)
            return Loc.T("path.preview.out_of_movement");

        var cellNode = QuantizeToMetagrid(ship, dest);
        if (cellNode == null || !path.Result.TryGetValue(cellNode, out var cell))
            return Loc.T("path.preview.out_of_range");
        if (cell.Length <= 0) return Loc.T("path.preview.here");
        if (!cell.CanStand) return Loc.T("spacecombat.path_pass_only");
        canMove = true;

        var sb = new StringBuilder();
        // In the dead-end state the game swapped the reachable set for the 1-tile escape moves
        // (StarshipPathController) — say so first, or the tiny numbers read like a bug.
        if (InDeadEnd) sb.Append(Loc.T("spacecombat.dead_end")).Append(", ");
        sb.Append(Loc.T("spacecombat.path_reachable", new
        {
            cost = cell.Length,
            budget = Mathf.RoundToInt(ship.CombatState.ActionPointsBlue),
            dir = FacingWord(cell.Direction),
        }));
        // The end-turn consequence (mirrors GetEndNodes' finishing-window test): budget left after this move
        // still at/above the finishing count means the forced-movement law keeps the turn going.
        int left = path.MaxLength - cell.Length;
        sb.Append(", ").Append(left >= nav.FinishingTilesCount
            ? Loc.T("spacecombat.path_must_continue", new { left })
            : Loc.T("spacecombat.path_can_stop"));
        return sb.ToString();
    }

    /// <summary>
    /// One-shot summary of where this turn's movement can END — <c>GetEndNodes()</c> (the legal final positions,
    /// i.e. the sighted marker fan) grouped by the turn made relative to the current heading: "ahead 4 to 7
    /// tiles; 45 degrees starboard 3 to 5 tiles; …". Null-safe; returns the "no moves" line when the set is
    /// empty (spent out / not the ship's turn).
    /// </summary>
    public static string MoveSummary(StarshipEntity ship)
    {
        var nav = ship?.Navigation;
        if (nav?.ReachableTiles?.Result == null || nav.ReachableTiles.Result.Count == 0)
            return Loc.T("spacecombat.move_none");

        int shipCompass = GridDirToCompass[SafeDir(CustomGraphHelper.GuessDirection(ship.Forward))];
        var min = new int[8];
        var max = new int[8];
        foreach (var kv in nav.GetEndNodes())
        {
            var cell = kv.Value;
            if (!cell.CanStand || cell.Length <= 0) continue;
            int delta = (GridDirToCompass[SafeDir(cell.Direction)] - shipCompass + 8) % 8;
            if (max[delta] == 0 || cell.Length < min[delta]) min[delta] = cell.Length;
            if (cell.Length > max[delta]) max[delta] = cell.Length;
        }

        var parts = new List<string>();
        foreach (int delta in SummaryOrder)
        {
            if (max[delta] == 0) continue;
            string turn = TurnWord(delta);
            parts.Add(min[delta] == max[delta]
                ? Loc.T("spacecombat.move_group_one", new { turn, n = min[delta] })
                : Loc.T("spacecombat.move_group_range", new { turn, min = min[delta], max = max[delta] }));
        }
        if (parts.Count == 0) return Loc.T("spacecombat.move_none");

        string list = string.Join("; ", parts);
        string line = Loc.T("spacecombat.move_summary", new { list });
        return InDeadEnd ? Loc.T("spacecombat.dead_end") + ", " + line : line;
    }

    /// <summary>Localized compass word for a grid direction index (arrival facing).</summary>
    internal static string FacingWord(int gridDir)
        => Loc.T(Accessibility.InteractableDescriber.Compass8[GridDirToCompass[SafeDir(gridDir)]]);

    private static bool InDeadEnd => Game.Instance?.StarshipPathController?.IsCurrentShipInDeadEnd ?? false;

    private static int SafeDir(int dir) => dir >= 0 && dir < 8 ? dir : 0;

    // Relative-turn word: delta is compass steps clockwise from the current heading (0 = ahead,
    // clockwise = starboard), so 1..3 are starboard 45/90/135 and 7..5 the port mirrors.
    private static string TurnWord(int delta)
    {
        switch (delta)
        {
            case 0: return Loc.T("spacecombat.turn_ahead");
            case 4: return Loc.T("spacecombat.turn_reverse");
            case 1:
            case 2:
            case 3: return Loc.T("spacecombat.turn_starboard", new { deg = delta * 45 });
            default: return Loc.T("spacecombat.turn_port", new { deg = (8 - delta) * 45 });
        }
    }

    // The metagrid snap the game's own click path applies before the reachable-set lookup (a ship's footprint
    // anchors path cells to a SizeRect-aligned lattice — a raw cursor node is usually NOT a dictionary key).
    // GetNodeInMetagrid is the game's private helper, reachable thanks to the publicized reference assemblies.
    private static CustomGridNodeBase QuantizeToMetagrid(StarshipEntity ship, CustomGridNodeBase dest)
    {
        try
        {
            var start = (CustomGridNodeBase)AstarPath.active.GetNearest(ship.Position).node;
            if (start == null) return null;
            return ship.Navigation.GetNodeInMetagrid(start, ship.SizeRect, dest);
        }
        catch { return null; }
    }
}
