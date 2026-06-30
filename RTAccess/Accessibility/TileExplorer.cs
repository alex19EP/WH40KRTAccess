using Kingmaker;
using Kingmaker.Controllers.Units;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.Pathfinding;
using Kingmaker.UnitLogic;
using Kingmaker.View;
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// A virtual grid cursor the user drives tile-by-tile with the arrow keys, speaking a full readout of each tile —
/// occupant, walkability/reason, cover on every edge, and the offset from the anchor unit. Unlike
/// <see cref="ExplorationNav"/> (which cycles the nearby interactables the game itself picks) this reads ARBITRARY
/// tiles, so a blind player can map a room or scout cover before moving. The readout is composed by
/// <see cref="InteractableDescriber.DescribeTile"/>; the grid model is the game's pathfinding graph
/// (<see cref="CustomGridGraph"/>, the square 1.35 m grid).
///
/// Ctrl+T = toggle on/off (turning on recenters the cursor on the selected/lead unit); arrow keys = move N/E/S/W;
/// Delete = re-announce the current tile; Enter = move the selected (combat: current-turn) unit to the cursor tile.
/// The camera follows the cursor for sighted helpers. Active in console (gamepad) UI mode on any on-foot surface
/// (<see cref="GameModeType.Default"/>) — i.e. BOTH exploration and tactical combat (the anchor is the selected
/// unit, which in combat is the current-turn unit). Uses Ctrl+T + arrow keys + Delete/Enter, none of which the
/// other navigators bind, so there is no double-handling in OnUpdate.
/// </summary>
internal static class TileExplorer
{
    private static bool _active;
    private static CustomGridNodeBase _cursor;

    public static void Update()
    {
        var game = Game.Instance;
        // Console mode + an on-foot surface. Default covers BOTH exploration and surface tactical combat, and
        // excludes dialog / full-screen windows / cutscene / global & space maps. If we were active and the mode
        // slips away, auto-exit.
        if (game == null || game.ControllerMode != Game.ControllerModeType.Gamepad ||
            game.CurrentMode != GameModeType.Default)
        {
            if (_active) Deactivate();
            return;
        }

        // Ctrl+T toggles. (Insert proved unreliable — eaten before it reached the mod.)
        bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
        if (ctrl && UnityEngine.Input.GetKeyDown(KeyCode.T)) { Toggle(); return; }
        if (!_active) return;

        if (UnityEngine.Input.GetKeyDown(KeyCode.UpArrow)) Move(0, 1);          // north (+Z)
        else if (UnityEngine.Input.GetKeyDown(KeyCode.DownArrow)) Move(0, -1);  // south (-Z)
        else if (UnityEngine.Input.GetKeyDown(KeyCode.RightArrow)) Move(1, 0);  // east  (+X)
        else if (UnityEngine.Input.GetKeyDown(KeyCode.LeftArrow)) Move(-1, 0);  // west  (-X)
        else if (UnityEngine.Input.GetKeyDown(KeyCode.Delete)) Announce();      // re-read the current tile
        else if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) MoveToCursor();
    }

    /// <summary>Drop cursor state on area change so a stale node from the previous area is never reused.</summary>
    public static void Reset()
    {
        _active = false;
        _cursor = null;
    }

    private static void Toggle()
    {
        if (_active) { Deactivate(); return; }
        var node = GetAnchor()?.CurrentUnwalkableNode;
        if (node == null) { Speaker.Speak("No reference point.", interrupt: true); return; }
        _cursor = node;
        _active = true;
        ScrollTo(node);
        // Key-driven — interrupt (per [[rt-interrupt-speech-rule]]).
        Speaker.Speak("Tile explorer on. " + Describe(), interrupt: true);
    }

    private static void Deactivate()
    {
        _active = false;
        Speaker.Speak("Tile explorer off.", interrupt: true);
    }

    private static void Move(int dx, int dz)
    {
        try
        {
            if (_cursor == null) { Deactivate(); return; }
            var grid = _cursor.Graph as CustomGridGraph;
            var next = grid?.GetNode(_cursor.XCoordinateInGrid + dx, _cursor.ZCoordinateInGrid + dz);
            if (next == null) { Speaker.Speak("Edge.", interrupt: true); return; }
            _cursor = next;
            ScrollTo(next);
            Announce();
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.Move failed: " + e); }
    }

    /// <summary>
    /// Order the controllable unit to walk to the cursor tile. Exploration routes through the game's canonical
    /// click-to-move (<see cref="UnitCommandsRunner.MoveSelectedUnitsToPoint"/>, formation-aware). Combat builds the
    /// turn-based move command directly to this exact node — the same call the ground click handler uses
    /// (<c>TryCreateMoveCommandTB(showMovePrediction:false)</c> + <c>Commands.Run</c>) — and speaks the reason when the
    /// move is refused (out of movement points, blocked, etc.). NOTE: in combat this commits movement and spends the
    /// turn's movement points; there is no preview step.
    /// </summary>
    private static void MoveToCursor()
    {
        try
        {
            if (_cursor == null) { Deactivate(); return; }
            var game = Game.Instance;
            var dest = (Vector3)_cursor.position;

            if (game.TurnController.TurnBasedModeActive)
            {
                var unit = game.TurnController.CurrentUnit as BaseUnitEntity;
                if (unit == null) { Speaker.Speak("No active unit.", interrupt: true); return; }
                var cmd = unit.TryCreateMoveCommandTB(
                    new MoveCommandSettings { Destination = dest, DisableApproachRadius = true },
                    showMovePrediction: false, out var status);
                if (cmd != null)
                {
                    unit.Commands.Run(cmd);
                    Speaker.Speak("Moving.", interrupt: true);
                }
                else
                {
                    Speaker.Speak(MoveFailure(status), interrupt: true);
                }
            }
            else
            {
                if (GetAnchor() == null) { Speaker.Speak("No character selected.", interrupt: true); return; }
                UnitCommandsRunner.MoveSelectedUnitsToPoint(dest);
                Speaker.Speak("Moving.", interrupt: true);
            }
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.MoveToCursor failed: " + e); }
    }

    private static string MoveFailure(UnitHelper.MoveCommandStatus status)
    {
        switch (status)
        {
            case UnitHelper.MoveCommandStatus.NotEnoughMovementPoints: return "Not enough movement points.";
            case UnitHelper.MoveCommandStatus.DestinationUnreachable: return "Path blocked.";
            case UnitHelper.MoveCommandStatus.CannotMove: return "Can't move.";
            case UnitHelper.MoveCommandStatus.SamePath: return "Already moving there.";
            default: return "Can't reach that tile.";
        }
    }

    // Each step supersedes the previous, so interrupt — stepping fast naturally clips long lines at the headline.
    private static void Announce() => Speaker.Speak(Describe(), interrupt: true);

    private static string Describe()
    {
        var line = InteractableDescriber.DescribeTile(_cursor, GetAnchor());
        return string.IsNullOrWhiteSpace(line) ? "Unknown tile." : line;
    }

    private static MechanicEntity GetAnchor()
    {
        var game = Game.Instance;
        return game?.SelectionCharacter?.SelectedUnit?.Value ?? game?.Player?.MainCharacterEntity;
    }

    private static void ScrollTo(CustomGridNodeBase node)
    {
        try { CameraRig.Instance?.ScrollTo((Vector3)node.position); }
        catch (Exception e) { Main.Log?.Error("TileExplorer.ScrollTo failed: " + e); }
    }
}
