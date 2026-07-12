using Kingmaker;                                   // Game
using Kingmaker.Controllers.TurnBased;             // VirtualPositionController
using Kingmaker.EntitySystem.Entities;             // BaseUnitEntity, StarshipEntity
using Kingmaker.Pathfinding;                       // CustomGridNodeBase
using Kingmaker.UI.Common;                         // IsDirectlyControllable() extension
using Kingmaker.UnitLogic;                         // UnitPredictionManager (the sighted feeder we mirror)
using Pathfinding;                                 // GraphNode
using RTAccess.Exploration;                        // MapCursor, DeploymentMode
using UnityEngine;

namespace RTAccess.Combat;

/// <summary>
/// The hover half of the accessible "holo unit" — the keyboard-cursor equivalent of the game's own
/// movement-simulation hover. A sighted player hovers a reachable cell in turn-based combat and the game writes
/// that cell into <see cref="VirtualPositionController.VirtualPosition"/>; every position-dependent readout — enemy
/// cover overtips, LOS lines, hit chances, ability range checks — then recomputes "as if the unit stood there"
/// (they all read <c>GetDesiredPosition</c>). This tick mirrors that feeder at the shared tile cursor, SILENTLY:
/// step the cursor inside the movable area and the vantage read (Semicolon), the scanner's per-enemy suffix, and
/// the action-bar availability all answer from the cursor tile — and the game's own on-screen LOS lines / cover
/// badges follow it for any sighted co-pilot. No announcements: the sim is an ambient lens, not an event.
///
/// Semantics mirror <c>UnitPredictionManager.UpdateVirtualLoSPosition</c>: while it is the player's turn and no
/// ability is armed (the game's <c>HasAbility</c> gate — the aim pipeline owns prediction then), the cursor tile
/// is simulated when it lies inside the unit's movable area. A pinned move-plan hologram — the holo unit the
/// move-to arm press plants (<see cref="CommandDispatch.MoveStep"/>) — OUTRANKS the hover sim, exactly as the
/// game's own hover yields to its click-planned hologram: while a plan is pinned, hover-sim hands the slot over
/// (without cleaning it!) so readouts stay anchored to the plan wherever the cursor browses next.
///
/// Writes are edge-triggered and cleanup is ownership-checked (only when the controller still holds OUR value),
/// so a sighted co-pilot's mouse hover — the same last-writer-wins slot the game itself uses — is never fought
/// per-frame. The controller self-clears on turn transitions and only honors the acting party unit with no
/// running command, so a stale write can never leak into mechanics.
/// </summary>
internal static class HoloSim
{
    private static Vector3? _written;              // the sim position we own (null = not ours / nothing written)

    // Movable-area membership cache: Contains is an O(n) reference scan over the game's node list — recompute only
    // when the cursor node or the area list instance changes (the controller swaps the list on every recompute).
    private static CustomGridNodeBase _areaNode;
    private static List<GraphNode> _areaList;
    private static bool _inArea;

    public static void Tick()
    {
        var game = Game.Instance;
        var vpc = game?.VirtualPositionController;
        if (vpc == null) { _written = null; return; }

        var tc = game.TurnController;
        bool live = Main.Enabled
            && tc != null && tc.TurnBasedModeActive && tc.IsPlayerTurn
            && !DeploymentMode.Active
            && game.SelectedAbilityHandler?.Ability == null          // the game's HasAbility hover-sim gate
            && tc.CurrentUnit is BaseUnitEntity unit
            && !(unit is StarshipEntity)                             // ships have no hover sim — the game shows
                                                                     // path markers instead (ShipPathInfo owns that)
            && unit.IsInPlayerParty && unit.IsDirectlyControllable()
            && MapCursor.Has;
        if (!live) { Drop(vpc); return; }

        // A pinned move-plan hologram (the Backspace-armed holo unit) owns the slot — the game's own precedence
        // (hover yields to the click-planned hologram). Hand ownership over WITHOUT cleaning: the pin often holds
        // the very value we just wrote (arm on the simulated tile), and cleaning would kill the plan's anchor.
        if (UnitPredictionManager.Instance?.m_VirtualHologramPosition != null) { _written = null; return; }

        var node = MapCursor.Node;
        var area = game.UnitMovableAreaController?.CurrentUnitMovableArea;
        if (!ReferenceEquals(node, _areaNode) || !ReferenceEquals(area, _areaList))
        {
            _areaNode = node;
            _areaList = area;
            _inArea = area != null && area.Contains(node);
        }
        if (!_inArea) { Drop(vpc); return; }

        Vector3 want = node.Vector3Position;
        if (!_written.HasValue || (_written.Value - want).sqrMagnitude > 1e-6f)
        {
            vpc.VirtualPosition = want;              // raises IVirtualPositionUIHandler → all game readouts follow
            _written = want;
        }
    }

    /// <summary>Release the hover sim. Cleans the controller only when it still holds OUR value — never stomp the
    /// game's own hover/hologram writes.</summary>
    private static void Drop(VirtualPositionController vpc)
    {
        if (vpc != null && _written.HasValue && vpc.VirtualPosition.HasValue
            && (vpc.VirtualPosition.Value - _written.Value).sqrMagnitude < 1e-4f)
            vpc.CleanVirtualPosition();
        _written = null;
    }
}
