using UnityEngine;
using RTAccess.UI;
using Ax = RTAccess.Accessibility;
using Ex = RTAccess.Exploration;

namespace RTAccess.Input
{
    /// <summary>
    /// Registers the mod's input actions. Phase 2: the focus-mode toggle (Global) + UI navigation
    /// (dispatched into the active <see cref="Navigator"/>, live only while focus mode owns the keyboard).
    /// Keys are captured via raw <c>UnityEngine.Input</c> (mode-independent). Grows per phase
    /// (exploration, service windows, …) as those screens land.
    /// </summary>
    public static class InputBindings
    {
        public static void RegisterDefaults()
        {
            // ---- Global: always live, even with focus mode off ----
            InputManager.Register("toggle_focus", "Toggle focus mode", InputCategory.Global, () =>
            {
                FocusMode.Toggle();
                Tts.Speak(Loc.T(FocusMode.Active ? "focus.on" : "focus.off"), interrupt: true);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
            }).AddBinding(KeyCode.A, ctrl: true, shift: true);

            // ---- Review buffers (Global, always live in a game): a second navigation axis that queries a
            // unit's live state (HP / AP / defenses / buffs) WITHOUT moving UI focus. Alt+Left/Right switch
            // buffer, Alt+Up/Down step lines. Global so they work in mouse mode and even while a HUD/menu is
            // focused; the handlers stand down out of a game. Alt+arrows don't collide with the bare-arrow UI
            // nav (exact-modifier match) or PartyHotkeys' Alt+digits. See RTAccess.Buffers.
            InputManager.Register("buffer.prev", "Previous review buffer", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Buffers.BufferControls.PrevBuffer(); }).AddBinding(KeyCode.LeftArrow, alt: true).Repeating();
            InputManager.Register("buffer.next", "Next review buffer", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Buffers.BufferControls.NextBuffer(); }).AddBinding(KeyCode.RightArrow, alt: true).Repeating();
            InputManager.Register("buffer.line_prev", "Previous review line", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Buffers.BufferControls.PrevItem(); }).AddBinding(KeyCode.UpArrow, alt: true).Repeating();
            InputManager.Register("buffer.line_next", "Next review line", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Buffers.BufferControls.NextItem(); }).AddBinding(KeyCode.DownArrow, alt: true).Repeating();

            // ---- UI: screen/menu navigation (dispatched into the active navigator) ----
            InputManager.Register("ui.up", "Navigate up", InputCategory.UI).AddBinding(KeyCode.UpArrow).Repeating();
            InputManager.Register("ui.down", "Navigate down", InputCategory.UI).AddBinding(KeyCode.DownArrow).Repeating();
            InputManager.Register("ui.left", "Navigate left", InputCategory.UI).AddBinding(KeyCode.LeftArrow).Repeating();
            InputManager.Register("ui.right", "Navigate right", InputCategory.UI).AddBinding(KeyCode.RightArrow).Repeating();
            InputManager.Register("ui.next", "Next region (Tab)", InputCategory.UI).AddBinding(KeyCode.Tab).Repeating();
            InputManager.Register("ui.prev", "Previous region (Shift+Tab)", InputCategory.UI).AddBinding(KeyCode.Tab, shift: true).Repeating();
            InputManager.Register("ui.activate", "Activate control", InputCategory.UI)
                .AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            InputManager.Register("ui.secondary", "Secondary action", InputCategory.UI).AddBinding(KeyCode.Backspace);
            InputManager.Register("ui.back", "Back / close", InputCategory.UI).AddBinding(KeyCode.Escape);
            InputManager.Register("ui.tooltip", "Read tooltip", InputCategory.UI)
                .AddBinding(KeyCode.Space).AddBinding(KeyCode.F1);
            InputManager.Register("ui.home", "Jump to first item", InputCategory.UI).AddBinding(KeyCode.Home);
            InputManager.Register("ui.end", "Jump to last item", InputCategory.UI).AddBinding(KeyCode.End);

            // ---- Exploration: the scanner / review cursor (a categorized, distance-sorted browse of everything in
            // the area). Live only while the in-game screen has world control (see InGameScreen / ControlState), so
            // these go dead in windows/dialogue/cutscenes. The read-only browse chords (PageUp/Down,
            // comma/period/N/M, X, P, and the inspect ' / Y) work whether or not the HUD is focused; Home yields to
            // ui.home when the HUD is focused (chord shadowing — the in-game screen flips UI <-> Exploration priority
            // with focus); interact (I) self-guards on focus since it mutates the world. Driveable via /input for
            // harness verification. See RTAccess.Exploration.Scanner. ----
            InputManager.Register("scan.item_prev", "Scanner: previous item", InputCategory.Exploration,
                Ex.Scanner.ItemPrev).AddBinding(KeyCode.PageUp).Repeating().Grouped("scanner");
            InputManager.Register("scan.item_next", "Scanner: next item", InputCategory.Exploration,
                Ex.Scanner.ItemNext).AddBinding(KeyCode.PageDown).Repeating().Grouped("scanner");
            InputManager.Register("scan.cat_prev", "Scanner: previous category", InputCategory.Exploration,
                Ex.Scanner.CategoryPrev).AddBinding(KeyCode.PageUp, ctrl: true).Grouped("scanner");
            InputManager.Register("scan.cat_next", "Scanner: next category", InputCategory.Exploration,
                Ex.Scanner.CategoryNext).AddBinding(KeyCode.PageDown, ctrl: true).Grouped("scanner");
            InputManager.Register("scan.review_party", "Scanner: cycle party", InputCategory.Exploration,
                () => Ex.Scanner.ReviewParty(false)).AddBinding(KeyCode.Comma).Grouped("scanner");
            InputManager.Register("scan.review_party_back", "Scanner: cycle party (reverse)", InputCategory.Exploration,
                () => Ex.Scanner.ReviewParty(true)).AddBinding(KeyCode.Comma, shift: true).Grouped("scanner");
            InputManager.Register("scan.review_enemies", "Scanner: cycle enemies", InputCategory.Exploration,
                () => Ex.Scanner.ReviewEnemies(false)).AddBinding(KeyCode.Period).Grouped("scanner");
            InputManager.Register("scan.review_enemies_back", "Scanner: cycle enemies (reverse)", InputCategory.Exploration,
                () => Ex.Scanner.ReviewEnemies(true)).AddBinding(KeyCode.Period, shift: true).Grouped("scanner");
            InputManager.Register("scan.review_neutrals", "Scanner: cycle neutrals", InputCategory.Exploration,
                () => Ex.Scanner.ReviewNeutrals(false)).AddBinding(KeyCode.N).Grouped("scanner");
            InputManager.Register("scan.review_neutrals_back", "Scanner: cycle neutrals (reverse)", InputCategory.Exploration,
                () => Ex.Scanner.ReviewNeutrals(true)).AddBinding(KeyCode.N, shift: true).Grouped("scanner");
            InputManager.Register("scan.review_objects", "Scanner: cycle objects", InputCategory.Exploration,
                () => Ex.Scanner.ReviewObjects(false)).AddBinding(KeyCode.M).Grouped("scanner");
            InputManager.Register("scan.review_objects_back", "Scanner: cycle objects (reverse)", InputCategory.Exploration,
                () => Ex.Scanner.ReviewObjects(true)).AddBinding(KeyCode.M, shift: true).Grouped("scanner");
            InputManager.Register("scan.interact", "Scanner: interact with selection", InputCategory.Exploration,
                Ex.Scanner.InteractSelected).AddBinding(KeyCode.I).Grouped("scanner");
            // Home / Slash — plant the movement cursor on the current review selection's tile (the coupling core;
            // the selection itself is unchanged). Home yields to ui.home when the HUD is focused (chord shadowing).
            InputManager.Register("scan.cursor_to_item", "Scanner: cursor to selection", InputCategory.Exploration,
                Ex.Scanner.CursorToSelection).AddBinding(KeyCode.Home).AddBinding(KeyCode.Slash).Grouped("scanner");
            InputManager.Register("scan.where_am_i", "Scanner: where am I", InputCategory.Exploration,
                Ex.Scanner.WhereAmINow).AddBinding(KeyCode.X).Grouped("scanner");
            InputManager.Register("scan.party", "Scanner: read the party", InputCategory.Exploration,
                Ex.Scanner.ReadParty).AddBinding(KeyCode.P).Grouped("scanner");
            // The inspect verb pair — speaks the game's own inspect panel (full sighted parity) and pops it for a
            // sighted helper (see Ex.Inspect). ' inspects the tile cursor's occupant; Y inspects the scanner's
            // review selection.
            InputManager.Register("inspect.cursor", "Inspect: cursor occupant", InputCategory.Exploration,
                Ex.Inspect.InspectCursor).AddBinding(KeyCode.Quote).Grouped("scanner");
            InputManager.Register("inspect.review", "Inspect: review selection", InputCategory.Exploration,
                Ex.Inspect.InspectReview).AddBinding(KeyCode.Y).Grouped("scanner");

            // ---- Exploration: the always-active tile cursor (the movement half of the map-viewer coupling, on RT's
            // square grid; see RTAccess.Accessibility.TileExplorer). No toggle — these live whenever Exploration owns
            // world control. Plain arrows are the PRIMARY slot: they win while the HUD is unfocused and are shadowed
            // by the navigator's ui.* arrows when it is focused (the in-game screen flips UI <-> Exploration priority
            // with focus); Shift+arrows are the SECONDARY, shadow-immune slot (no UI binding sits on Shift+arrows).
            // C recenters on the party, Delete re-announces the tile, Backspace issues the guarded move-to, and
            // Enter / KeypadEnter interact with the nearest interactable to the cursor. Driveable via /input for
            // harness verification. ----
            InputManager.Register("cursor.up", "Cursor: step north", InputCategory.Exploration,
                Ax.TileExplorer.StepNorth).AddBinding(KeyCode.UpArrow).Repeating().Grouped("cursor");
            InputManager.Register("cursor.down", "Cursor: step south", InputCategory.Exploration,
                Ax.TileExplorer.StepSouth).AddBinding(KeyCode.DownArrow).Repeating().Grouped("cursor");
            InputManager.Register("cursor.left", "Cursor: step west", InputCategory.Exploration,
                Ax.TileExplorer.StepWest).AddBinding(KeyCode.LeftArrow).Repeating().Grouped("cursor");
            InputManager.Register("cursor.right", "Cursor: step east", InputCategory.Exploration,
                Ax.TileExplorer.StepEast).AddBinding(KeyCode.RightArrow).Repeating().Grouped("cursor");
            InputManager.Register("cursor.up2", "Cursor: step north (secondary)", InputCategory.Exploration,
                Ax.TileExplorer.StepNorth).AddBinding(KeyCode.UpArrow, shift: true).Repeating().Grouped("cursor");
            InputManager.Register("cursor.down2", "Cursor: step south (secondary)", InputCategory.Exploration,
                Ax.TileExplorer.StepSouth).AddBinding(KeyCode.DownArrow, shift: true).Repeating().Grouped("cursor");
            InputManager.Register("cursor.left2", "Cursor: step west (secondary)", InputCategory.Exploration,
                Ax.TileExplorer.StepWest).AddBinding(KeyCode.LeftArrow, shift: true).Repeating().Grouped("cursor");
            InputManager.Register("cursor.right2", "Cursor: step east (secondary)", InputCategory.Exploration,
                Ax.TileExplorer.StepEast).AddBinding(KeyCode.RightArrow, shift: true).Repeating().Grouped("cursor");
            InputManager.Register("cursor.recenter", "Cursor: recenter on party", InputCategory.Exploration,
                Ax.TileExplorer.Recenter).AddBinding(KeyCode.C).Grouped("cursor");
            InputManager.Register("cursor.reannounce", "Cursor: re-announce tile", InputCategory.Exploration,
                Ax.TileExplorer.ReAnnounce).AddBinding(KeyCode.Delete).Grouped("cursor");
            InputManager.Register("cursor.move_to", "Cursor: move party to cursor", InputCategory.Exploration,
                Ax.TileExplorer.MoveToCursor).AddBinding(KeyCode.Backspace).Grouped("cursor");
            InputManager.Register("cursor.interact", "Cursor: interact at cursor", InputCategory.Exploration,
                Ax.TileExplorer.InteractAtCursor).AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter).Grouped("cursor");
        }

        // The review-buffer keys are Global (always polled), so their handlers stand down when not in a
        // loaded game (no party to read).
        private static bool InAGame() => Kingmaker.Game.Instance?.Player != null;
    }
}
