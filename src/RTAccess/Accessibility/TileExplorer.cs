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
    /// <summary>Drop the cursor on area change so a stale node from the previous area is never reused.</summary>
    public static void Reset()
    {
        MapCursor.Clear();
    }

    // ---- registered handlers (InputCategory.Exploration; see InputBindings) ----

    // Primary arrows: tile steps — but while the FREE cursor mode owns them (exploration.cursor_mode = free,
    // outside combat/deployment) they yield to the per-frame glide (Exploration/CursorGlide), which polls the
    // same actions' HELD state instead of these press handlers.
    public static void StepNorth() => Step(0, 1);   // +Z
    public static void StepSouth() => Step(0, -1);  // -Z
    public static void StepEast()  => Step(1, 0);   // +X
    public static void StepWest()  => Step(-1, 0);  // -X

    // Secondary Shift+arrows: ALWAYS tile steps, in both cursor modes (WA's two-slot idiom — the precision
    // slot). In free mode a secondary step snaps the glide point onto the grid by construction: Move() steps
    // from the derived node and Set(node) clears the sub-tile point.
    public static void StepNorthSecondary() => StepTile(0, 1);
    public static void StepSouthSecondary() => StepTile(0, -1);
    public static void StepEastSecondary()  => StepTile(1, 0);
    public static void StepWestSecondary()  => StepTile(-1, 0);

    /// <summary>Re-read the cursor tile (planting on the party first if the cursor is cold).</summary>
    public static void ReAnnounce()
    {
        if (RTAccess.UI.Navigation.HasFocus) return;   // HUD owns the keys; the primary cursor reads stand down (Shift+arrows stay live)
        if (EnsurePlanted(out _)) Announce();
    }

    /// <summary>Recenter the cursor on the anchor unit and read its tile. In free mode it lands on the anchor's
    /// EXACT live position (the glide's sub-tile truth, WA's glide-Recenter), not the tile centre.</summary>
    public static void Recenter()
    {
        if (RTAccess.UI.Navigation.HasFocus) return;   // HUD owns the keys; the primary cursor controls stand down
        if (RTAccess.Exploration.CursorGlide.FreeModeActive && MapCursor.SetPoint(MapCursor.PlayerPosition))
        {
            ScrollTo(MapCursor.Position);
            Announce();
            return;
        }
        var node = GetAnchor()?.CurrentUnwalkableNode;
        if (node == null) { Speaker.Speak(Loc.T("cursor.no_reference"), interrupt: true); return; }
        MapCursor.Set(node);
        ScrollTo(node);
        Announce();
    }

    // ---- stepping ----

    private static void Step(int dx, int dz)
    {
        if (RTAccess.Exploration.CursorGlide.FreeModeActive) return;   // the glide owns the primary arrows
        StepTile(dx, dz);
    }

    private static void StepTile(int dx, int dz)
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
            string crossed = CrossedWall(cur, next, dx, dz) ? Loc.T("tile.through_wall") : null;
            MapCursor.Set(next);
            ScrollTo(next);
            Announce(crossed);
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.Move failed: " + e); }
    }

    /// <summary>
    /// True when the step from <paramref name="cur"/> to <paramref name="next"/> passed THROUGH a wall — the edge
    /// between the two cells is cut, but the destination cell is itself perfectly walkable.
    ///
    /// This is the case a blind player has no way to detect. RT models thin walls, railings and cover as FENCES on
    /// the edge between two cells (<c>IsConnectionCut</c>), not as unwalkable cells: both cells stay walkable, no
    /// map object sits on either, and the cursor — which navigates by raw grid coordinates
    /// (<see cref="NavmeshProbe.Neighbour"/>) so it can scan through geometry, and must keep doing so — crosses in
    /// silence. The cover readout names such an edge only inside the combat overlay window (your own turn, no
    /// ability armed, and only if that fence grants cover at all), so out of combat or mid-aim there was no signal
    /// whatsoever, and the first hint was a move-to refusal (August field report #5).
    ///
    /// <c>GetNeighbourAlongDirection</c> applies the pathfinder's own connectivity bits — the ones
    /// <c>CustomGridGraph.CalculateConnections</c> wrote, folding in fences, climb height and corner-cutting — so a
    /// null here IS the engine's verdict, not an approximation of it. It also returns null for an unwalkable
    /// neighbour, hence the <c>next.Walkable</c> guard: that case already reads "wall" from the tile description
    /// and does not need a second word. Cardinal steps only (the arrows never move diagonally).
    /// </summary>
    private static bool CrossedWall(CustomGridNodeBase cur, CustomGridNodeBase next, int dx, int dz)
    {
        try
        {
            if (next == null || !next.Walkable) return false;
            // Grid direction indices, same map the cover readout and the wall tones use: N=2 E=1 S=0 W=3.
            int dir = dz > 0 ? 2 : dz < 0 ? 0 : dx > 0 ? 1 : 3;
            return cur.GetNeighbourAlongDirection(dir) == null;
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.CrossedWall failed: " + e); return false; }
    }

    /// <summary>
    /// Plant the cursor on the anchor unit if it is unplanted. Returns false (and speaks) only when there is no
    /// anchor to plant on. <paramref name="fresh"/> is true when this call did the planting — callers read the tile
    /// instead of acting on that first press, so a cold key never walks the party onto its own tile.
    /// Internal: the free-cursor glide's cold hold rides the same plant + read discipline.
    /// </summary>
    internal static bool EnsurePlanted(out bool fresh)
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
            // During deployment there is no move-to (Enter places, Space starts the battle); swallow Backspace.
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

                // "You are here" beats the engine's unhelpful zero-length-path refusal for a press on the
                // unit's own tile.
                if (node == unit.CurrentUnwalkableNode)
                { Speaker.Speak(Loc.T("path.preview.here"), interrupt: true); return; }

                // Every TB unit — walker or voidship — rides the game's own two-click flow, STATELESS
                // (CommandDispatch.MoveStep — no local arm, no confirm window; exactly the mouse loop): a press
                // on a NEW destination plants the game's move preview — the holo unit / destination hologram a
                // sighted co-pilot sees, the path line + provoke markers, and the pinned VIRTUAL position
                // (+ arrival facing for ships), so cycling enemies while planted answers cover, odds, and arcs
                // "from the planned cell" — and speaks the path preview; a press on the ALREADY-PLANTED
                // destination runs the pinned move. The engine's own SamePath detection is the confirm, so the
                // plan survives any amount of browsing / time between the presses (the old 3 s confirm window
                // silently lapsed and forced a re-plant — the reported "it resets" bug), and if the path
                // drifted between presses (budget changed) the engine RE-PLANTS instead of committing and the
                // fresh verdict is spoken — a stale plan can never fire a move the player didn't just hear.
                // Esc cancels via the game's own hotkey (SetVirtualMoveCommand subscribes it).
                var r = RTAccess.Combat.CommandDispatch.MoveStep(node);
                if (r == RTAccess.Combat.CommandDispatch.MoveStepResult.Committed)
                {
                    Speaker.Speak(Loc.T("path.moving"), interrupt: true);
                }
                else if (r == RTAccess.Combat.CommandDispatch.MoveStepResult.Planted)
                {
                    // The engine accepted the plant, so the confirm hint always follows — even if our own
                    // pricing disagrees, the engine's word rules. Refused: MoveStep already spoke the reason.
                    Speaker.Speak(RTAccess.Exploration.PathInfo.Preview(unit, node, out _)
                        + " " + Loc.T("path.preview.press_again"), interrupt: true);
                }
            }
            else
            {
                // No pause guard: the engine ACCEPTS a move order while paused and runs it on unpause — that is
                // the point of real-time-with-pause, and the sighted right-click does exactly this. The command
                // is buffered by UnitCommandBuffer (registered for every game mode, Pause included) and applied
                // once UnitCommandController / UnitMoveController resume, which sit out Pause. Refusing it here
                // was ours, not the game's.
                if (GetAnchor() == null) { Speaker.Speak(Loc.T("path.no_character"), interrupt: true); return; }
                // MapCursor.Position, not node.Vector3Position: identical in tile mode (the point IS the tile
                // centre), and in free mode the party walks to the exact sub-tile point — real-time movement is
                // continuous, so the raw point is the higher-fidelity destination.
                UnitCommandsRunner.MoveSelectedUnitsToPoint(MapCursor.Position);
                Speaker.Speak(MovingAnnounce(), interrupt: true);
            }
        }
        catch (Exception e) { Main.Log?.Error("TileExplorer.MoveToCursor failed: " + e); }
    }

    /// <summary>
    /// Plant the cursor on an arbitrary world point (the scanner's Home/Slash "cursor to selection"), follow the
    /// camera, and read the new tile. When the point is off-graph
    /// <see cref="MapCursor.Set(Vector3)"/> keeps the previous node and returns false — we say so rather than
    /// re-announcing the old tile as if the cursor had jumped to the selection (which would also leave the scanner
    /// measuring from, and move-to walking to, the wrong tile).
    /// </summary>
    public static void PlantOn(Vector3 worldPos) => PlantOn(worldPos, announce: true);

    /// <summary>Plant the cursor without reading the tile — for callers that speak their OWN line about the spot
    /// (the blast-position cycle names what the template catches there; the tile description would bury that under
    /// the cover/offset readout). Delete still re-reads the tile in full, aim tail included.</summary>
    internal static void PlantOn(Vector3 worldPos, bool announce)
    {
        // In free mode the plant keeps the EXACT point (the scanner's selection position), so the glide resumes
        // from the thing itself rather than its tile centre; tile mode snaps to the node as always.
        bool ok = RTAccess.Exploration.CursorGlide.FreeModeActive
            ? MapCursor.SetPoint(worldPos)
            : MapCursor.Set(worldPos);
        if (!ok) { Speaker.Speak(Loc.T("cursor.cant_place"), interrupt: true); return; }
        var node = MapCursor.Node;
        if (node == null) { Speaker.Speak(Loc.T("cursor.no_reference"), interrupt: true); return; }
        ScrollTo(MapCursor.Position);
        if (announce) Announce();
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
                : Loc.T("path.moving_name", new { name = UnitNames.Of(one) });
        }
        catch { return Loc.T("path.moving"); }
    }

    // ---- readout ----

    // Each step supersedes the previous, so interrupt — stepping fast naturally clips long lines at the headline.
    // Internal: the glide's cold plant reads through the same line.
    internal static void Announce() => Announce(null);

    /// <summary>Read the cursor tile, optionally led by a one-word note about the STEP that got here (see
    /// <see cref="Move"/>'s cut-edge check) rather than about the tile itself.</summary>
    internal static void Announce(string prefix)
    {
        var line = Describe();
        Speaker.Speak(string.IsNullOrEmpty(prefix) ? line : prefix + ", " + line, interrupt: true);
    }

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

    private static void ScrollTo(CustomGridNodeBase node) => ScrollTo((Vector3)node.position);

    // Internal: the free-cursor glide follows the camera through the same gate (a per-frame ScrollTo just
    // retargets the rig's lerp — the same thing the game's own edge-scroll does).
    internal static void ScrollTo(Vector3 pos)
    {
        if (!CameraFollow()) return;   // exploration.camera_follow gates the follow-cam; review cycles never reach here
        try { CameraRig.Instance?.ScrollTo(pos); }
        catch (Exception e) { Main.Log?.Error("TileExplorer.ScrollTo failed: " + e); }
    }

    // exploration.camera_follow (Off/On, default On). Off = the cursor never drives the camera. Read live each
    // scroll so a mid-session toggle takes effect immediately; defaults On if the setting is somehow absent.
    private static bool CameraFollow()
        => RTAccess.Settings.ModSettings.GetSetting<RTAccess.Settings.BoolSetting>("exploration.camera_follow")?.Get() ?? true;
}
