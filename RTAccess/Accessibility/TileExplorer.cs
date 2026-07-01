using Kingmaker;
using Kingmaker.Controllers.Units;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Pathfinding;
using Kingmaker.UI.Common;          // IsDirectlyControllable() extension
using Kingmaker.UnitLogic;
using Kingmaker.View;
using RTAccess.Exploration; // MapCursor (the shared world cursor)
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// The always-active virtual grid cursor — the movement half of the WrathAccess "map viewer" coupling, on RT's
/// discrete square grid. The player steps it tile-by-tile with the arrow keys and hears a full readout of each tile
/// (occupant, walkability/reason, cover on every edge, offset from the anchor). Unlike the area scanner (which
/// cycles the interactables the game itself surfaces) this reads ARBITRARY tiles, so a blind player can map a room
/// or scout cover before moving. The readout is composed by <see cref="InteractableDescriber.DescribeTile"/>; the
/// grid model is the game's pathfinding graph (<see cref="CustomGridGraph"/>, the square 1.35 m grid).
///
/// There is no toggle. The cursor is live whenever the in-game screen owns world control: its keys are registered
/// in the Exploration input category (see <see cref="RTAccess.Input.InputBindings"/>) and the screen takes them
/// dead in windows / dialogue / cutscenes. The cursor is planted lazily — the first step / re-announce / move-to
/// plants it on the anchor unit and reads that tile rather than acting, so a cold press never silently walks the
/// party onto its own tile.
///
/// Keys (all WrathAccess-parity, all registered, not raw-polled): arrows = step N/E/S/W (primary slot); Shift+arrows
/// = step (secondary slot, shadow-immune); C = recenter on the party; Delete = re-announce the current tile;
/// Backspace = guarded move-to; Enter / KeypadEnter = interact with the nearest interactable to the cursor (the I key
/// interacts with the scanner SELECTION instead). The whole primary set stands down while the HUD is focused — the arrows and
/// Backspace/Enter yield to the navigator by chord shadowing, C and Delete by an explicit focus check — so only the
/// shadow-immune Shift+arrows keep stepping the cursor when
/// the HUD owns the keyboard. Move-to reproduces the engine turn guards the hand-rolled command bypasses
/// (player turn + the active unit selected and controllable) and, in turn-based combat, takes a two-step confirm —
/// it commits movement and spends the turn's movement points with no preview, so the first press only announces the
/// distance. The cursor is the shared <see cref="MapCursor"/>, so the scanner and later spatial cues all measure
/// from the same point; the camera follows it for sighted helpers.
/// </summary>
internal static class TileExplorer
{
    // Turn-based move-to is a two-step confirm: the first press arms (and announces the distance), a second press on
    // the SAME tile within this window commits. It re-arms whenever the cursor moves or the window lapses, so a
    // stale arm can never fire a move the player didn't just confirm.
    private const float ConfirmWindow = 3f;
    private static CustomGridNodeBase _armedNode;
    private static float _armTime;

    /// <summary>Drop cursor + confirm state on area change so a stale node from the previous area is never reused.</summary>
    public static void Reset()
    {
        MapCursor.Clear();
        _armedNode = null;
    }

    // ---- registered handlers (InputCategory.Exploration; see InputBindings) ----

    public static void StepNorth() => Step(0, 1);   // +Z
    public static void StepSouth() => Step(0, -1);  // -Z
    public static void StepEast()  => Step(1, 0);   // +X
    public static void StepWest()  => Step(-1, 0);  // -X

    /// <summary>Re-read the cursor tile (planting on the party first if the cursor is cold).</summary>
    public static void ReAnnounce()
    {
        if (RTAccess.UI.Navigation.HasFocus) return;   // HUD owns the keys; the primary cursor reads stand down (Shift+arrows stay live)
        if (EnsurePlanted(out _)) Announce();
    }

    /// <summary>Recenter the cursor on the anchor unit and read its tile.</summary>
    public static void Recenter()
    {
        if (RTAccess.UI.Navigation.HasFocus) return;   // HUD owns the keys; the primary cursor controls stand down
        var node = GetAnchor()?.CurrentUnwalkableNode;
        if (node == null) { Speaker.Speak("No reference point.", interrupt: true); return; }
        MapCursor.Set(node);
        _armedNode = null;   // the destination moved; never carry a pending confirm across a recenter
        ScrollTo(node);
        Announce();
    }

    // ---- stepping ----

    private static void Step(int dx, int dz)
    {
        if (!EnsurePlanted(out bool fresh)) return;
        if (fresh) { Announce(); return; }   // the first touch reads the planted tile; it doesn't also step
        Move(dx, dz);
    }

    private static void Move(int dx, int dz)
    {
        try
        {
            var cur = MapCursor.Node;
            if (cur == null) return;   // EnsurePlanted guarantees this; defensive only
            var grid = cur.Graph as CustomGridGraph;
            var next = grid?.GetNode(cur.XCoordinateInGrid + dx, cur.ZCoordinateInGrid + dz);
            if (next == null) { Speaker.Speak("Edge.", interrupt: true); return; }
            MapCursor.Set(next);
            _armedNode = null;   // any cursor step re-arms the move-to two-step confirm — no stale arm survives a move
            ScrollTo(next);
            Announce();
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.Move failed: " + e); }
    }

    /// <summary>
    /// Plant the cursor on the anchor unit if it is unplanted. Returns false (and speaks) only when there is no
    /// anchor to plant on. <paramref name="fresh"/> is true when this call did the planting — callers read the tile
    /// instead of acting on that first press, so a cold key never walks the party onto its own tile.
    /// </summary>
    private static bool EnsurePlanted(out bool fresh)
    {
        fresh = false;
        if (MapCursor.Has) return true;
        var node = GetAnchor()?.CurrentUnwalkableNode;
        if (node == null) { Speaker.Speak("No reference point.", interrupt: true); return false; }
        MapCursor.Set(node);
        ScrollTo(node);
        fresh = true;
        return true;
    }

    // ---- move-to (the single guarded order; replaces Scanner.MoveToSelected + the old toggled MoveToCursor) ----

    /// <summary>
    /// Order the party / active unit to walk to the cursor tile. Out of combat this routes through the game's
    /// canonical formation-aware click-to-move (<see cref="UnitCommandsRunner.MoveSelectedUnitsToPoint"/>), refused
    /// while the game is paused. In turn-based combat it rebuilds the move command directly to the node — but ONLY
    /// for the player's own active unit (the hand-rolled command bypasses the engine guards
    /// <see cref="UnitCommandsRunner"/> enforces, which would otherwise let it command an enemy on its turn) and
    /// behind a two-step confirm (the move commits and spends the turn's movement points with no preview, so the
    /// first press only announces the distance). Refusals are spoken (out of points, blocked, etc.).
    /// </summary>
    public static void MoveToCursor()
    {
        try
        {
            // While an ability is armed, Backspace cancels the aim instead of moving (see Targeting).
            if (RTAccess.Exploration.Targeting.Aiming) { RTAccess.Exploration.Targeting.Cancel(); return; }
            if (!EnsurePlanted(out bool fresh)) return;
            if (fresh) { Announce(); return; }   // cold press reads the planted tile rather than moving onto it

            var node = MapCursor.Node;
            var game = Game.Instance;
            if (game == null) return;

            if (game.TurnController.TurnBasedModeActive)
            {
                if (!game.TurnController.IsPlayerTurn) { Speaker.Speak("Not your turn.", interrupt: true); return; }
                var unit = game.SelectionCharacter?.SelectedUnit?.Value as BaseUnitEntity;
                var current = game.TurnController.CurrentUnit as BaseUnitEntity;
                if (unit == null || unit != current || !unit.IsDirectlyControllable())
                { Speaker.Speak("Select your active character.", interrupt: true); return; }

                // First press on this tile arms + previews the PATH (reachability + step count, from the game's own
                // movable-area set); a second within the window commits. The arm is unconditional — the engine stays
                // authoritative on commit — so even when the preview reads "out of range" a determined second press
                // still defers to the real move command (which then speaks its own refusal), and the preview can never
                // wrongly block a move the engine would allow.
                if (_armedNode != node || (Time.unscaledTime - _armTime) > ConfirmWindow)
                {
                    _armedNode = node;
                    _armTime = Time.unscaledTime;
                    var preview = RTAccess.Exploration.PathInfo.Preview(unit, node, out bool canMove);
                    Speaker.Speak(canMove ? preview + " Press again to move." : preview, interrupt: true);
                    return;
                }
                _armedNode = null;

                var cmd = unit.TryCreateMoveCommandTB(
                    new MoveCommandSettings { Destination = node.Vector3Position, DisableApproachRadius = true },
                    showMovePrediction: false, out var status);
                if (cmd != null) { unit.Commands.Run(cmd); Speaker.Speak("Moving.", interrupt: true); }
                else Speaker.Speak(MoveFailure(status), interrupt: true);
            }
            else
            {
                if (game.IsPaused) { Speaker.Speak("Paused, unpause to move.", interrupt: true); return; }
                if (GetAnchor() == null) { Speaker.Speak("No character selected.", interrupt: true); return; }
                UnitCommandsRunner.MoveSelectedUnitsToPoint(node.Vector3Position);
                Speaker.Speak(MovingAnnounce(), interrupt: true);
            }
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.MoveToCursor failed: " + e); }
    }

    /// <summary>
    /// Plant the cursor on an arbitrary world point (the scanner's Home/Slash "cursor to selection"), clear any
    /// pending move confirm, follow the camera, and read the new tile. When the point is off-graph
    /// <see cref="MapCursor.Set(Vector3)"/> keeps the previous node and returns false — we say so rather than
    /// re-announcing the old tile as if the cursor had jumped to the selection (which would also leave the scanner
    /// measuring from, and move-to walking to, the wrong tile).
    /// </summary>
    public static void PlantOn(Vector3 worldPos)
    {
        if (!MapCursor.Set(worldPos)) { Speaker.Speak("Can't place the cursor there.", interrupt: true); return; }
        _armedNode = null;
        var node = MapCursor.Node;
        if (node == null) { Speaker.Speak("No reference point.", interrupt: true); return; }
        ScrollTo(node);
        Announce();
    }

    /// <summary>
    /// Interact with the interactable NEAREST the cursor — the Enter / KeypadEnter half of the verb pair (the I key
    /// interacts with the scanner SELECTION instead; see <see cref="RTAccess.Exploration.Scanner"/>). Interactables
    /// live off-grid (not slotted per tile), so this resolves the nearest one within reach of the cursor via
    /// <see cref="InteractableDescriber.InteractableAt"/> — the same object <see cref="Describe"/> just announced —
    /// and drives it through the game's own click interaction (<see cref="RTAccess.Exploration.ProxyMapObject.Interact"/>,
    /// i.e. ClickMapObjectHandler), the way a mouse click does. Lazy-plants like the other cursor verbs (a cold press
    /// reads the tile rather than acting); speaks "nothing to interact with nearby" when there is none.
    /// </summary>
    public static void InteractAtCursor()
    {
        try
        {
            // While an ability is armed, Enter commits the aim at the cursor instead of interacting (see Targeting).
            if (RTAccess.Exploration.Targeting.Aiming) { RTAccess.Exploration.Targeting.CommitAtCursor(); return; }
            if (!EnsurePlanted(out bool fresh)) return;
            if (fresh) { Announce(); return; }
            var obj = InteractableDescriber.InteractableAt(MapCursor.Node);
            if (obj == null) { Speaker.Speak("Nothing to interact with nearby.", interrupt: true); return; }
            var item = new ProxyMapObject(obj);
            Speaker.Speak(item.Interact() ? "Interacting with " + item.Name + "." : "Can't interact with " + item.Name + ".", interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.InteractAtCursor failed: " + e); }
    }

    /// <summary>
    /// The real-time move-to confirmation: "Moving party." when more than one unit is selected — the payoff of
    /// Ctrl+A (<see cref="PartyHotkeys.SelectAll"/>), since <see cref="UnitCommandsRunner.MoveSelectedUnitsToPoint"/>
    /// walks every selected unit — else "Moving &lt;name&gt;." naming the single actor so the player hears WHO got the
    /// order. Reads the live selection set the command actually drives.
    /// </summary>
    private static string MovingAnnounce()
    {
        try
        {
            var sel = Game.Instance?.SelectionCharacter?.SelectedUnits;
            int n = sel?.Count ?? 0;
            if (n > 1) return "Moving party.";
            var one = (n == 1 ? sel[0] : null) ?? GetAnchor() as BaseUnitEntity;
            return string.IsNullOrWhiteSpace(one?.CharacterName) ? "Moving." : "Moving " + one.CharacterName + ".";
        }
        catch { return "Moving."; }
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

    // ---- readout ----

    // Each step supersedes the previous, so interrupt — stepping fast naturally clips long lines at the headline.
    private static void Announce() => Speaker.Speak(Describe(), interrupt: true);

    private static string Describe()
    {
        var line = InteractableDescriber.DescribeTile(MapCursor.Node, GetAnchor());
        return string.IsNullOrWhiteSpace(line) ? "Unknown tile." : line;
    }

    private static MechanicEntity GetAnchor()
    {
        var game = Game.Instance;
        return game?.SelectionCharacter?.SelectedUnit?.Value ?? game?.Player?.MainCharacterEntity;
    }

    private static void ScrollTo(CustomGridNodeBase node)
    {
        if (!CameraFollow()) return;   // exploration.camera_follow gates the follow-cam; review cycles never reach here
        try { CameraRig.Instance?.ScrollTo((Vector3)node.position); }
        catch (Exception e) { Main.Log?.Error("TileExplorer.ScrollTo failed: " + e); }
    }

    // exploration.camera_follow (Off/On, default On). Off = the cursor never drives the camera. Read live each
    // scroll so a mid-session toggle takes effect immediately; defaults On if the setting is somehow absent.
    private static bool CameraFollow()
        => RTAccess.Settings.ModSettings.GetSetting<RTAccess.Settings.BoolSetting>("exploration.camera_follow")?.Get() ?? true;
}
