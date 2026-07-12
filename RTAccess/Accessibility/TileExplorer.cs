using Kingmaker;
using Kingmaker.Controllers.Units;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Pathfinding;
using Kingmaker.UI.Common;          // IsDirectlyControllable() extension
using Kingmaker.View;
using RTAccess.Exploration; // MapCursor (the shared world cursor)
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// The always-active virtual grid cursor — the movement half of the WrathAccess "map viewer" coupling, on RT's
/// discrete square grid. The player steps it tile-by-tile with the arrow keys and hears a readout of each tile
/// (occupant, walkability/reason, cover on every edge, offset from the anchor). Unlike the area scanner (which
/// cycles the interactables the game itself surfaces) this reads ARBITRARY tiles, so a blind player can map a room
/// or scout cover before moving — but only within VISUAL PARITY: the readout is fog-gated so it reveals no more of an
/// unexplored area than a sighted player sees on the local map (never-seen → just "unexplored"; explored-not-visible →
/// static layout but no live creatures; visible → full). The readout is composed by <see cref="InteractableDescriber.DescribeTile"/>; the
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
/// the HUD owns the keyboard. Move-to reproduces the engine turn guards the direct call bypasses
/// (player turn + the active unit selected and controllable) and, in turn-based combat, is the game's own
/// two-click flow: the first press plants the holo unit (move preview + pinned virtual position) and speaks the
/// path preview, the second press runs the pinned move. The cursor is the shared <see cref="MapCursor"/>, so the
/// scanner and later spatial cues all measure from the same point; the camera follows it for sighted helpers.
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
        if (node == null) { Speaker.Speak(Loc.T("cursor.no_reference"), interrupt: true); return; }
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
            var next = NavmeshProbe.Neighbour(cur, dx, dz);
            if (next == null) { Speaker.Speak(Loc.T("cursor.edge"), interrupt: true); return; }
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
        if (node == null) { Speaker.Speak(Loc.T("cursor.no_reference"), interrupt: true); return false; }
        MapCursor.Set(node);
        ScrollTo(node);
        fresh = true;
        return true;
    }

    // ---- move-to (the single guarded order; replaces Scanner.MoveToSelected + the old toggled MoveToCursor) ----

    /// <summary>
    /// Order the party / active unit to walk to the cursor tile. Out of combat this routes through the game's
    /// canonical formation-aware click-to-move (<see cref="UnitCommandsRunner.MoveSelectedUnitsToPoint"/>), refused
    /// while the game is paused. In turn-based combat it is the game's own two-click move for the active unit
    /// (<see cref="RTAccess.Combat.CommandDispatch.MoveStep"/>): the first press plants the holo unit — the move
    /// preview whose pinned virtual position every positional readout answers from — and speaks the path preview
    /// (distance, cost, provokes); the second press on the same tile runs the pinned move. Guarded to the player's
    /// own controllable turn unit (the direct call bypasses the engine guards <see cref="UnitCommandsRunner"/>
    /// enforces, which would otherwise let it command an enemy on its turn). Refusals are spoken.
    /// </summary>
    public static void MoveToCursor()
    {
        try
        {
            // During deployment there is no move-to (Enter places, B starts the battle); swallow Backspace.
            if (RTAccess.Exploration.DeploymentMode.Active) return;
            // While an ability is armed, Backspace cancels the aim instead of moving (see Targeting).
            if (RTAccess.Exploration.Targeting.Aiming) { RTAccess.Exploration.Targeting.Cancel(); return; }
            if (!EnsurePlanted(out bool fresh)) return;
            if (fresh) { Announce(); return; }   // cold press reads the planted tile rather than moving onto it

            var node = MapCursor.Node;
            var game = Game.Instance;
            if (game == null) return;

            if (game.TurnController.TurnBasedModeActive)
            {
                if (!game.TurnController.IsPlayerTurn) { Speaker.Speak(Loc.T("combat.not_your_turn"), interrupt: true); return; }
                var unit = game.SelectionCharacter?.SelectedUnit?.Value as BaseUnitEntity;
                var current = game.TurnController.CurrentUnit as BaseUnitEntity;
                if (unit == null || unit != current || !unit.IsDirectlyControllable())
                { Speaker.Speak(Loc.T("combat.select_active"), interrupt: true); return; }

                // Every TB unit — walker or voidship — rides the game's own two-click preview flow
                // (CommandDispatch.MoveStep): the arm press ALSO plants the game's move preview — the holo
                // unit / destination hologram a sighted co-pilot sees, the path line + provoke markers, and
                // the pinned VIRTUAL position (+ arrival facing for ships), so cycling enemies while armed
                // answers cover, odds, and arcs "from the planned cell". The second press runs the pinned
                // move. A fresh arm always drops any stale pin first, so a lapsed confirm window can never
                // turn an arming press into an instant commit; the game's own Esc cancel stays wired.
                if (_armedNode == node && (Time.unscaledTime - _armTime) <= ConfirmWindow)
                {
                    _armedNode = null;
                    var r = RTAccess.Combat.CommandDispatch.MoveStep(node);
                    if (r == RTAccess.Combat.CommandDispatch.MoveStepResult.Committed)
                        Speaker.Speak(Loc.T("path.moving"), interrupt: true);
                    else if (r == RTAccess.Combat.CommandDispatch.MoveStepResult.Planted)
                    {
                        // The path drifted between presses (budget/heading changed) — the game planted a
                        // FRESH preview instead of confirming; stay armed and re-read the new verdict.
                        _armedNode = node;
                        _armTime = Time.unscaledTime;
                        Speaker.Speak(RTAccess.Exploration.PathInfo.Preview(unit, node, out _)
                            + " " + Loc.T("path.preview.press_again"), interrupt: true);
                    }
                    return;
                }
                _armedNode = node;
                _armTime = Time.unscaledTime;
                RTAccess.Combat.CommandDispatch.MoveCancelLocal();
                var preview = RTAccess.Exploration.PathInfo.Preview(unit, node, out bool canMove);
                if (canMove)
                {
                    // Plant the holo unit; on the rare engine disagreement its refusal already spoke — the
                    // engine's word is authoritative, so ours stays unsaid.
                    if (RTAccess.Combat.CommandDispatch.MoveStep(node)
                        != RTAccess.Combat.CommandDispatch.MoveStepResult.Planted) return;
                    Speaker.Speak(preview + " " + Loc.T("path.preview.press_again"), interrupt: true);
                }
                else Speaker.Speak(preview, interrupt: true);
            }
            else
            {
                if (game.IsPaused) { Speaker.Speak(Loc.T("path.paused_no_move"), interrupt: true); return; }
                if (GetAnchor() == null) { Speaker.Speak(Loc.T("path.no_character"), interrupt: true); return; }
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
        if (!MapCursor.Set(worldPos)) { Speaker.Speak(Loc.T("cursor.cant_place"), interrupt: true); return; }
        _armedNode = null;
        var node = MapCursor.Node;
        if (node == null) { Speaker.Speak(Loc.T("cursor.no_reference"), interrupt: true); return; }
        ScrollTo(node);
        Announce();
    }

    /// <summary>
    /// Interact — the Enter / KeypadEnter half of the verb pair (the I key leads with the scanner SELECTION instead;
    /// see <see cref="RTAccess.Exploration.Scanner"/>). Both keys funnel through the SAME activation path and reach
    /// the same targets — they differ only in order: Enter tries THIS key's cursor first (the object(s) at the tile
    /// cursor, via <see cref="RTAccess.Exploration.Activation.TryCursorObject"/> — which pops a chooser when a tile
    /// has several within reach), then falls back to the review selection
    /// (<see cref="RTAccess.Exploration.Scanner.TryActivateSelection"/>). Interactables live off-grid, so the tile
    /// resolve is a nearest-within-reach proximity query — the same object <see cref="Describe"/> just announced —
    /// driven through the game's own click interaction (<see cref="RTAccess.Exploration.ProxyMapObject.Interact"/>),
    /// the way a mouse click does. Lazy-plants like the other cursor verbs (a cold press reads the tile rather than
    /// acting); speaks "nothing to interact with nearby" only when neither cursor has a target.
    /// </summary>
    public static void InteractAtCursor()
    {
        try
        {
            // While deploying (pre-combat prep), Enter PLACES the selected character on the cursor tile (see DeploymentMode).
            if (RTAccess.Exploration.DeploymentMode.Active) { RTAccess.Exploration.DeploymentMode.CommitAtCursor(); return; }
            // While an ability is armed, Enter commits the aim at the cursor instead of interacting (see Targeting).
            if (RTAccess.Exploration.Targeting.Aiming) { RTAccess.Exploration.Targeting.CommitAtCursor(); return; }
            if (!EnsurePlanted(out bool fresh)) return;
            if (fresh) { Announce(); return; }
            if (RTAccess.Exploration.Activation.TryCursorObject(MapCursor.Node)) return;   // this key's cursor first
            if (RTAccess.Exploration.Scanner.TryActivateSelection()) return;               // then the review selection
            Speaker.Speak(Loc.T("scan.nothing_nearby"), interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.InteractAtCursor failed: " + e); }
    }

    /// <summary>
    /// V — the holographic vantage read: for the acting unit, the cover / in-range / threat it would have if it stood
    /// on the CURSOR tile (the "if I stood here" tactical preview a sighted player reads off the move ghost), computed
    /// as a pure read from the candidate cell (<see cref="RTAccess.Accessibility.CombatReads.VantageFrom"/>) —
    /// followed for surface units by the spoken fan of LOS lines
    /// (<see cref="RTAccess.Accessibility.CombatReads.LosSweep"/>): every visible enemy, nearest first, with
    /// distance, the line's hit% and its cover badge, answered from the DESIRED position exactly as the on-screen
    /// lines are (so it tracks the hover-sim / planted holo unit). Combat only; out of combat or with no acting
    /// unit it says so. Lazy-plants like the other cursor verbs.
    /// </summary>
    public static void ReadVantage()
    {
        try
        {
            if (RTAccess.UI.Navigation.HasFocus) return;   // HUD owns the keys
            var game = Game.Instance;
            var me = game?.TurnController?.CurrentUnit as BaseUnitEntity
                     ?? game?.SelectionCharacter?.SelectedUnit?.Value as BaseUnitEntity;
            if (game?.Player?.IsInCombat != true || me == null)
            { Speaker.Speak(Loc.T("vantage.not_in_combat"), interrupt: true); return; }
            if (!EnsurePlanted(out bool fresh)) return;
            if (fresh) { Announce(); return; }
            // Cover/threat is surface tactics; a ship's "what if I went here" is the inertial path verdict
            // (cost, arrival facing, stop legality) — the same line the move-to arming press speaks.
            var line = me is StarshipEntity ship
                ? RTAccess.Exploration.ShipPathInfo.Preview(ship, MapCursor.Node, out _)
                : CombatReads.VantageFrom(MapCursor.Position, me);
            string text = string.IsNullOrWhiteSpace(line) ? Loc.T("vantage.no_enemies") : line;
            if (!(me is StarshipEntity))
            {
                var sweep = CombatReads.LosSweep(me);
                if (!string.IsNullOrWhiteSpace(sweep)) text += ". " + sweep + ".";
            }
            Speaker.Speak(text, interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.ReadVantage failed: " + e); }
    }

    /// <summary>
    /// Z — the movement-options summary for the acting unit's turn-based turn: surface units get the
    /// reachable-area extent (<see cref="RTAccess.Exploration.PathInfo.MoveAreaSummary"/> — the spoken blue
    /// move-highlight), starships get the end-position fan grouped by resulting facing
    /// (<see cref="RTAccess.Exploration.ShipPathInfo.MoveSummary"/> — the spoken path-marker fan). Cursor-
    /// independent (it summarizes the whole turn, not a tile), pure read, combat-only; says why otherwise.
    /// </summary>
    public static void ReadMoveSummary()
    {
        try
        {
            if (RTAccess.UI.Navigation.HasFocus) return;   // HUD owns the keys
            var tc = Game.Instance?.TurnController;
            if (tc == null || !tc.TurnBasedModeActive)
            { Speaker.Speak(Loc.T("combat.not_turn_based"), interrupt: true); return; }
            if (!tc.IsPlayerTurn) { Speaker.Speak(Loc.T("combat.not_your_turn"), interrupt: true); return; }
            string line = tc.CurrentUnit is StarshipEntity ship
                ? RTAccess.Exploration.ShipPathInfo.MoveSummary(ship)
                : RTAccess.Exploration.PathInfo.MoveAreaSummary();
            Speaker.Speak(string.IsNullOrWhiteSpace(line) ? Loc.T("path.preview.out_of_movement") : line,
                interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.ReadMoveSummary failed: " + e); }
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
            if (n > 1) return Loc.T("path.moving_party");
            var one = (n == 1 ? sel[0] : null) ?? GetAnchor() as BaseUnitEntity;
            return string.IsNullOrWhiteSpace(one?.CharacterName)
                ? Loc.T("path.moving")
                : Loc.T("path.moving_name", new { name = one.CharacterName });
        }
        catch { return Loc.T("path.moving"); }
    }

    // ---- readout ----

    // Each step supersedes the previous, so interrupt — stepping fast naturally clips long lines at the headline.
    private static void Announce() => Speaker.Speak(Describe(), interrupt: true);

    private static string Describe()
    {
        var line = InteractableDescriber.DescribeTile(MapCursor.Node, GetAnchor());
        if (string.IsNullOrWhiteSpace(line)) line = Loc.T("cursor.unknown_tile");
        // While deploying, follow the tile readout with its deploy legality + the holographic vantage from here.
        if (RTAccess.Exploration.DeploymentMode.Active)
        {
            var tail = RTAccess.Exploration.DeploymentMode.CursorTail(MapCursor.Node);
            if (!string.IsNullOrWhiteSpace(tail)) line += ". " + tail;
        }
        // While AIMING, follow the tile readout with: (1) the AoE geometry preview (shape / range / tile count; null
        // for single-target), then (2) the affected-target readout — the aimed unit's hit%/damage + overpenetration
        // chain, or the AoE's caught enemies + friendly-fire warning — read from the game's OWN aim result at our
        // cursor (piloted-aiming: AimRead reading AimReadTap, driven by AimPointerDriver). Mutually exclusive w/ deploy.
        else if (RTAccess.Exploration.Targeting.Aiming)
        {
            var tail = RTAccess.Exploration.AoEPreview.CursorTail(MapCursor.Node);
            if (!string.IsNullOrWhiteSpace(tail)) line += ". " + tail;
            var targets = RTAccess.Combat.AimRead.CursorReadout(verbose: false);
            if (!string.IsNullOrWhiteSpace(targets)) line += ". " + targets;
        }
        return line;
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
