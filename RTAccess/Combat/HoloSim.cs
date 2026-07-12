using Kingmaker;                                   // Game
using Kingmaker.Controllers.TurnBased;             // VirtualPositionController
using Kingmaker.EntitySystem.Entities;             // BaseUnitEntity
using Kingmaker.Pathfinding;                       // CustomGridNodeBase
using Kingmaker.UI.Common;                         // IsDirectlyControllable() extension
using Kingmaker.UI.InputSystems;                   // KeyboardAccess.IsCtrlHold — the game's own modifier poll
using Kingmaker.UnitLogic;                         // UnitPredictionManager (the sighted feeder we mirror)
using Pathfinding;                                 // GraphNode
using RTAccess.Exploration;                        // MapCursor, FogProbe, DeploymentMode
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Combat;

/// <summary>
/// The accessible "holo unit" — the keyboard-cursor equivalent of the game's own movement-simulation hover. A
/// sighted player hovers a cell in turn-based combat and, after a delay (instantly with Ctrl held), the game writes
/// that cell into <see cref="VirtualPositionController.VirtualPosition"/>; every position-dependent readout — enemy
/// cover overtips, LOS lines, hit chances, ability range checks — then recomputes "as if the unit stood there"
/// (they all read <c>GetDesiredPosition</c>). The ghost hologram is pure visuals; the controller write IS the
/// simulation. This tick mirrors that feeder at the shared tile cursor, so the vantage read (Semicolon), the aim
/// reads, and the action-bar availability all answer from the simulated tile — and the game's own on-screen LOS
/// lines / cover badges follow the blind player's cursor for any sighted co-pilot.
///
/// Semantics mirror <c>UnitPredictionManager.UpdateVirtualLoSPosition</c> exactly: while it is the player's turn
/// and no ability is armed (the game's <c>HasAbility</c> gate — the aim pipeline owns prediction then), the cursor
/// tile is simulated AUTOMATICALLY when it lies inside the unit's movable area (unless a click-planned move
/// hologram is pinned — that outranks plain hover in the game too), and ANYWHERE while Ctrl is held (the sighted
/// Ctrl+hover force). The one divergence is visual parity: Ctrl+hover lets a sighted player point into black fog,
/// but our force-sim refuses never-seen tiles (<see cref="FogProbe"/>) so cover geometry of unexplored space is
/// never leaked. Fog classification is cached per node — the probe is a render-thread-syncing ReadPixels and must
/// never run per-frame.
///
/// Speech: engage/disengage is announced only on the FLIP ("simulating" / "simulation off"), queued so it rides
/// behind the tile readout the same keypress produced; stepping around inside the area stays silent, and a sim that
/// dies with its whole context (turn ended, combat over, cursor unplanted, aim armed) dies silently — the context
/// change already announced itself. Writes are edge-triggered and cleaning is ownership-checked (only when the
/// controller still holds OUR value), so a sighted co-pilot's mouse hover — the same last-writer-wins slot the
/// game itself uses — is never fought per-frame. The controller self-clears on turn transitions and only honors
/// the acting party unit with no running command, so a stale write can never leak into mechanics.
/// </summary>
internal static class HoloSim
{
    private static Vector3? _written;              // the sim position we own (null = not ours / nothing written)
    private static bool _on;                       // spoken engage state (for flip announcements)
    private static bool _far;                      // engaged via the Ctrl force outside the movable area
    private static bool _prevCtrl;                 // Ctrl edge detect (re-announce a blocked tile on re-press)

    // Movable-area membership cache: Contains is an O(n) reference scan over the game's node list — recompute only
    // when the cursor node or the area list instance changes (the controller swaps the list on every recompute).
    private static CustomGridNodeBase _areaNode;
    private static List<GraphNode> _areaList;
    private static bool _inArea;

    // Fog-classification cache (one probe per node, never per-frame) + the blocked-tile announce dedup.
    private static CustomGridNodeBase _fogNode;
    private static bool _fogOk;

    public static void Tick()
    {
        var game = Game.Instance;
        var vpc = game?.VirtualPositionController;
        if (vpc == null) { Drop(null, contextLive: false); return; }

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
        if (!live) { Drop(vpc, contextLive: false); return; }

        var node = MapCursor.Node;
        bool ctrl = KeyboardAccess.IsCtrlHold();
        bool ctrlEdge = ctrl && !_prevCtrl;
        _prevCtrl = ctrl;

        var area = game.UnitMovableAreaController?.CurrentUnitMovableArea;
        if (!ReferenceEquals(node, _areaNode) || !ReferenceEquals(area, _areaList))
        {
            _areaNode = node;
            _areaList = area;
            _inArea = area != null && area.Contains(node);
        }

        // The game's own precedence (UpdateVirtualLoSPosition): plain in-area hover yields to a pinned move-plan
        // hologram; Ctrl overrides both the area gate and the hologram.
        bool hologramPinned = UnitPredictionManager.Instance?.m_VirtualHologramPosition != null;
        bool plain = _inArea && !hologramPinned;
        bool forced = ctrl && !plain;

        if (forced)
        {
            if (!ReferenceEquals(node, _fogNode))
            {
                _fogNode = node;
                _fogOk = FogProbe.IsExplored(node.Vector3Position);
                if (!_fogOk) Speaker.Speak(Loc.T("holo.unexplored"));      // announced once per tile...
            }
            else if (!_fogOk && ctrlEdge)
            {
                Speaker.Speak(Loc.T("holo.unexplored"));                    // ...and again on a deliberate re-press
            }
            if (!_fogOk) { Drop(vpc, contextLive: true); return; }
        }

        if (!plain && !forced) { Drop(vpc, contextLive: true); return; }

        Vector3 want = node.Vector3Position;
        if (!_written.HasValue || (_written.Value - want).sqrMagnitude > 1e-6f)
        {
            vpc.VirtualPosition = want;              // raises IVirtualPositionUIHandler → all game readouts follow
            _written = want;
        }
        if (!_on || _far != forced)                  // flip, or crossing the area boundary with Ctrl held
        {
            _on = true;
            _far = forced;
            Speaker.Speak(Loc.T(forced ? "holo.on_far" : "holo.on"));      // queued behind the step's tile readout
        }
    }

    /// <summary>Release the simulation. Cleans the controller only when it still holds OUR value (never stomp the
    /// game's own hover/hologram writes), and speaks the "off" flip only while the planning context is still live —
    /// a sim that died with its context (turn over, cursor gone, aim armed) goes silently.</summary>
    private static void Drop(VirtualPositionController vpc, bool contextLive)
    {
        if (vpc != null && _written.HasValue && vpc.VirtualPosition.HasValue
            && (vpc.VirtualPosition.Value - _written.Value).sqrMagnitude < 1e-4f)
            vpc.CleanVirtualPosition();
        _written = null;
        if (_on)
        {
            _on = false;
            if (contextLive) Speaker.Speak(Loc.T("holo.off"));
        }
        _far = false;
        if (!contextLive) _prevCtrl = false;
    }
}
