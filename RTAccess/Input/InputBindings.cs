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
            // Alt+T ("tooltip") — the current review line's detail via the game's own tooltip template (a
            // buff's description / non-stack sources). Alt+Space and Alt+Enter are OS-claimed on Windows
            // (system menu / display toggle), so the mnemonic letter is the safe chord in the Alt layer.
            InputManager.Register("buffer.detail", "Read details of the current review line", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Buffers.BufferControls.Detail(); }).AddBinding(KeyCode.T, alt: true);

            // ---- Formation editor field (Formation category — live ONLY while FormationScreen's WASD
            // field is the focused Tab stop, see FormationScreen.InputCategories; ranked above Global there,
            // so Alt+digits shadow the party-select hotkeys only inside the field). WASD step the 2-D
            // cursor; Shift+WASD glide continuously (empty handlers — FormationField.Tick polls them via
            // InputManager.Held); Comma/Shift+Comma review members; Slash plants the cursor on the reviewed
            // member; C re-centres; Alt+1..6 grab the Nth member of the window's character list.
            InputManager.Register("formation.up", "Formation cursor forward", InputCategory.Formation,
                () => RTAccess.Screens.FormationScreen.FocusedField?.MoveStep(0, 1)).AddBinding(KeyCode.W).Repeating();
            InputManager.Register("formation.down", "Formation cursor back", InputCategory.Formation,
                () => RTAccess.Screens.FormationScreen.FocusedField?.MoveStep(0, -1)).AddBinding(KeyCode.S).Repeating();
            InputManager.Register("formation.left", "Formation cursor left", InputCategory.Formation,
                () => RTAccess.Screens.FormationScreen.FocusedField?.MoveStep(-1, 0)).AddBinding(KeyCode.A).Repeating();
            InputManager.Register("formation.right", "Formation cursor right", InputCategory.Formation,
                () => RTAccess.Screens.FormationScreen.FocusedField?.MoveStep(1, 0)).AddBinding(KeyCode.D).Repeating();
            InputManager.Register("formation.glide_up", "Formation glide forward", InputCategory.Formation,
                () => { }).AddBinding(KeyCode.W, shift: true);
            InputManager.Register("formation.glide_down", "Formation glide back", InputCategory.Formation,
                () => { }).AddBinding(KeyCode.S, shift: true);
            InputManager.Register("formation.glide_left", "Formation glide left", InputCategory.Formation,
                () => { }).AddBinding(KeyCode.A, shift: true);
            InputManager.Register("formation.glide_right", "Formation glide right", InputCategory.Formation,
                () => { }).AddBinding(KeyCode.D, shift: true);
            InputManager.Register("formation.cycle_next", "Review next formation member", InputCategory.Formation,
                () => RTAccess.Screens.FormationScreen.FocusedField?.CycleMember(1)).AddBinding(KeyCode.Comma).Repeating();
            InputManager.Register("formation.cycle_prev", "Review previous formation member", InputCategory.Formation,
                () => RTAccess.Screens.FormationScreen.FocusedField?.CycleMember(-1)).AddBinding(KeyCode.Comma, shift: true).Repeating();
            InputManager.Register("formation.jump", "Formation cursor to reviewed member", InputCategory.Formation,
                () => RTAccess.Screens.FormationScreen.FocusedField?.JumpToReviewed()).AddBinding(KeyCode.Slash);
            InputManager.Register("formation.center", "Formation cursor to centre", InputCategory.Formation,
                () => RTAccess.Screens.FormationScreen.FocusedField?.CenterCursor()).AddBinding(KeyCode.C);
            for (int f = 0; f < 6; f++)
            {
                int idx = f;
                InputManager.Register("formation.pick" + (idx + 1), "Grab formation member " + (idx + 1),
                    InputCategory.Formation,
                    () => RTAccess.Screens.FormationScreen.FocusedField?.PickMember(idx))
                    .AddBinding((KeyCode)((int)KeyCode.Alpha1 + idx), alt: true).Grouped("formation");
            }

            // L — open the message-log review (a child overlay: channel tabs + newest-first history, with
            // per-line tooltip / glossary drill-in). Global + self-gated so it opens in surface/space and over
            // windows/dialogue; bare L is free — GameKeybinds moved the game's Encyclopedia onto Ctrl+L.
            InputManager.Register("log.review", "Open the message log", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Screens.LogReviewScreen.Open(); }).AddBinding(KeyCode.L);

            // Ctrl+P — re-announce the current character-creation phase (name + position + progress). Global +
            // self-gated (CharGenAnnounce.ReAnnounce no-ops unless CharGen is open); registered rather than
            // raw-polled so it rides the one category/arbitration path. See RTAccess.Accessibility.CharGenAnnounce.
            InputManager.Register("chargen.reannounce", "Re-announce character-creation phase", InputCategory.Global,
                Ax.CharGenAnnounce.ReAnnounce).AddBinding(KeyCode.P, ctrl: true);

            // F12 speech self-test (was a bare UnityEngine.Input poll in Main.OnUpdate). Global so it works
            // anywhere; a useful first-run "is my TTS alive" check for end users, and bare F12 is free in the game.
            InputManager.Register("diag.speech_test", "Speech self-test", InputCategory.Global, () =>
                Tts.Speak(Loc.T("speech.self_test", new { backend = RTAccess.Speech.Speaker.ActiveBackend }), interrupt: true))
                .AddBinding(KeyCode.F12);
#if DEBUG
            // Dev-only config dumps (F9 Rewired, F10 keybindings). Debug-gated so a Release build neither ships the
            // dev tooling nor permanently claims F9/F10 from the game. Were bare polls in Main.OnUpdate.
            InputManager.Register("diag.dump_rewired", "Dump Rewired config", InputCategory.Global, () =>
            {
                RTAccess.Diagnostics.RewiredDump.Dump(RTAccess.Main.ModDir);
                Tts.Speak("Rewired config dumped.", interrupt: true);
            }).AddBinding(KeyCode.F9);
            InputManager.Register("diag.dump_keybinds", "Dump keybindings", InputCategory.Global, () =>
            {
                RTAccess.Diagnostics.KeybindingsDump.Dump(RTAccess.Main.ModDir);
                Tts.Speak("Keybindings dumped.", interrupt: true);
            }).AddBinding(KeyCode.F10);
            // F11 — dump the scanner's live registry with the per-surface visibility gates, flagging the
            // "phantom" items (IsVisible but not CurrentlySeen) that show in the category browse yet are missing
            // from the M object-cycle / tile exploration. The one-stop probe for the "shows in the scanner but
            // not the M-cycle" report; writes Scanner_visibility_dump.json + logs the phantom list.
            InputManager.Register("diag.dump_scanner", "Dump scanner visibility", InputCategory.Global, () =>
            {
                RTAccess.Diagnostics.ScannerDump.Dump(RTAccess.Main.ModDir);
                Tts.Speak("Scanner objects dumped.", interrupt: true);
            }).AddBinding(KeyCode.F11);
            // F8 — explain why the scanner's I key and the tile cursor's Enter disagree on the CURRENT selection
            // (Enabled vs CanInteract() vs actually-fireable, plus the co-located interactables). Select the
            // offending object with M first, then press F8. Full breakdown → Player.log; see DevApi.DebugScannerInteract.
            InputManager.Register("diag.debug_interact", "Dump selection interactability", InputCategory.Global, () =>
            {
                RTAccess.Exploration.Scanner.DebugInteract();
                Tts.Speak("Interact diagnostic logged.", interrupt: true);
            }).AddBinding(KeyCode.F8);
            // F6 — speak the room-map stats (count + current room) and dump the full room table to the mod log.
            // The /eval-less twin of the watershed prototype; confirms the classifier built and what it produced.
            // (F6 has been free since console-nav retired; F7 is also free but stays unclaimed.)
            InputManager.Register("scan.debug_rooms", "Dump room map stats", InputCategory.Global,
                Ex.RoomMap.DebugSpeak).AddBinding(KeyCode.F6);
#endif

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
            // Escape closes the focused mod screen (window / dialogue / Esc menu / settings) via its Back action —
            // but a context-split (YieldsWhenUnfocused): out on the bare HUD with nothing focused the mod has
            // nothing to back out of, so it yields Escape to the game's own EscHotkeyManager. That opens the game's
            // pause menu (RequestEscMenu) OR closes the topmost native window on the game's back-stack — and the
            // mod's EscMenuScreen then wraps the opened menu (announces "Game Menu", navigable). Without the yield,
            // ui.back claims Escape every frame (InGameScreen always declares the UI category) but can't consume it
            // on the HUD (no Back handler there), so Escape was a dead key — the pause menu was unreachable.
            InputManager.Register("ui.back", "Back / close", InputCategory.UI).AddBinding(KeyCode.Escape).YieldsWhenUnfocused();
            // F1 always reads the focused item's tooltip (a mod-owned, always-safe key). Space ALSO reads it
            // when the HUD is focused, but is a context-split (YieldsWhenUnfocused): out in the world with
            // nothing focused it yields to the game's Space (Pause / End-turn) instead of being eaten. F1 is
            // deliberately NOT split — it stays claimed so it never triggers the game's ActionBar consumable
            // slot 1 (also F1) by accident. Both route to the same tooltip read in the navigator.
            InputManager.Register("ui.tooltip", "Read tooltip", InputCategory.UI).AddBinding(KeyCode.F1);
            InputManager.Register("ui.tooltip.space", "Read tooltip (Space)", InputCategory.UI)
                .AddBinding(KeyCode.Space).YieldsWhenUnfocused();
            InputManager.Register("ui.home", "Jump to first item", InputCategory.UI).AddBinding(KeyCode.Home);
            InputManager.Register("ui.end", "Jump to last item", InputCategory.UI).AddBinding(KeyCode.End);
            // Ctrl+Up/Down jump between regions of a sheet (the navigators consume these only while the
            // focus is inside a regioned structure; elsewhere the chord bubbles). Localized as
            // bind.ui.regionPrev / bind.ui.regionNext in settings.json.
            InputManager.Register("ui.regionPrev", "Previous sheet region", InputCategory.UI)
                .AddBinding(KeyCode.UpArrow, ctrl: true).Repeating();
            InputManager.Register("ui.regionNext", "Next sheet region", InputCategory.UI)
                .AddBinding(KeyCode.DownArrow, ctrl: true).Repeating();

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
            // (Landmarks — area exits and points of interest — are no longer a dedicated V/B cycle: exits appear as
            // their real world objects in the Objects/Exits browse, and the marker-only pins live in the
            // "Points of interest" category. Both are reached via the Ctrl+PageUp/Down category browse; I on a
            // landmark walks the party toward it. V and B are therefore free.)
            // (Live area effects — hazards + buff zones — are no longer a dedicated Z cycle: they browse as the
            // "Hazards" / "Buff zones" categories in the Ctrl+PageUp/Down list, and the tile explorer names the
            // hazard standing on the cursor tile. Z is therefore free.)
            // V / Shift+V — cycle the CURRENT room's exits (doorway openings to neighbouring rooms; see RoomMap),
            // the same keys WrathAccess uses (muscle-memory parity). Speaks "Exit to Room N, class" + bearing/distance
            // and plants the shared cursor on the opening so Backspace walks the party there. Bare V was freed for this
            // by moving the combat vantage read to Semicolon below (GameKeybinds already moved the game's ship
            // customization to Ctrl+V). Grouped with the scanner review cycles.
            InputManager.Register("scan.exit_next", "Scanner: next room exit", InputCategory.Exploration,
                Ex.Scanner.ExitNext).AddBinding(KeyCode.V).Grouped("scanner");
            InputManager.Register("scan.exit_prev", "Scanner: previous room exit", InputCategory.Exploration,
                Ex.Scanner.ExitPrev).AddBinding(KeyCode.V, shift: true).Grouped("scanner");
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
            // O — re-announce the current scanner selection (any group) from the live cursor origin, without
            // stepping the list. O is a confirmed-free letter key (see PartyHotkeys keymap notes).
            InputManager.Register("scan.announce_selection", "Scanner: re-announce selection", InputCategory.Exploration,
                Ex.Scanner.AnnounceSelection).AddBinding(KeyCode.O).Grouped("scanner");
            // U — battlefield summary (C5): counts + in-combat reach/threat vs the acting unit, in one sentence. U is
            // the last confirmed-free bare letter (see PartyHotkeys keymap notes); like the other bare-letter scanner
            // keys it inherits focus mode's game-service-window suppression (verify live it pops no window).
            InputManager.Register("scan.battlefield", "Scanner: battlefield summary", InputCategory.Exploration,
                Ex.Scanner.BattlefieldSummary).AddBinding(KeyCode.U).Grouped("scanner");
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
            // Semicolon — holographic vantage: read the cover / in-range / threat the acting unit would have FROM the
            // cursor tile (the sighted move-ghost read). Combat only; self-gates otherwise. Relocated here from bare V
            // so V/Shift+V can take the room-exit cycle (WrathAccess parity); Semicolon is a free bare key next to the
            // home row. See RTAccess.Accessibility.TileExplorer.ReadVantage.
            InputManager.Register("read.vantage", "Read vantage from cursor tile", InputCategory.Exploration,
                Ax.TileExplorer.ReadVantage).AddBinding(KeyCode.Semicolon).Grouped("cursor");
            // B — start the battle during the pre-combat deployment (preparation) phase. Self-gates: a no-op outside
            // deployment. Bare B is free (GameKeybinds moved cargo management to Ctrl+B). See DeploymentMode; Enter
            // places the selected character on the cursor tile while deploying (routed in InteractAtCursor).
            InputManager.Register("deploy.start_battle", "Deployment: start the battle", InputCategory.Exploration,
                Ex.DeploymentMode.StartBattle).AddBinding(KeyCode.B).Grouped("cursor");

            // ---- Exploration: party commands + status readout. Registered (not raw-polled like PartyHotkeys.Update's
            // member-select): the game's own Select-all / Hold / Stop / status keys live in the PC HUD (dead in our
            // parallel tree), so these are the sole handler — focus mode suppresses the game's duplicates. Ctrl+A is
            // free because the focus toggle is Ctrl+Shift+A; H / G / R are bare letters the game only uses via the HUD.
            // Select-all is load-bearing: it restores a formation move-to (cursor.move_to walks every selected unit)
            // after Alt+1-6 / Shift+A/D collapsed the selection. See RTAccess.Accessibility.PartyHotkeys. ----
            InputManager.Register("party.select_all", "Select whole party", InputCategory.Exploration,
                Ax.PartyHotkeys.SelectAll).AddBinding(KeyCode.A, ctrl: true).Grouped("party");
            InputManager.Register("party.hold", "Party: hold position", InputCategory.Exploration,
                Ax.PartyHotkeys.Hold).AddBinding(KeyCode.H).Grouped("party");
            InputManager.Register("party.stop", "Party: stop", InputCategory.Exploration,
                Ax.PartyHotkeys.Stop).AddBinding(KeyCode.G).Grouped("party");
            InputManager.Register("combat.status", "Read status (AP / MP / turn)", InputCategory.Exploration,
                Ax.PartyHotkeys.CombatStatus).AddBinding(KeyCode.R).Grouped("party");
            // Member selection — the keyboard L2-selector. Registered (not raw-polled) so the keyboard-arbitration
            // patch claims these chords and the game's own Prev/NextCharacter (Shift+A/D) + SelectCharacter
            // (Alt+1..6) on the same keys don't ALSO fire. See RTAccess.Accessibility.PartyHotkeys.
            InputManager.Register("party.member_next", "Select next party member", InputCategory.Exploration,
                Ax.PartyHotkeys.MemberNext).AddBinding(KeyCode.D, shift: true).Grouped("party");
            InputManager.Register("party.member_prev", "Select previous party member", InputCategory.Exploration,
                Ax.PartyHotkeys.MemberPrev).AddBinding(KeyCode.A, shift: true).Grouped("party");
            for (int i = 0; i < 6; i++)
            {
                int slot = i; // capture per iteration
                InputManager.Register("party.member_" + (i + 1), "Select party member " + (i + 1),
                    InputCategory.Exploration, () => Ax.PartyHotkeys.SelectMember(slot))
                    .AddBinding((KeyCode)((int)KeyCode.Alpha1 + i), alt: true).Grouped("party");
            }
            // K — read the RT resource / pressure gauges the Tab tree doesn't carry (momentum, veil,
            // profit factor, boss HP, turn / Necron timers, objective counter). Read-only, self-filtering
            // by each gauge's visibility. K is a confirmed-free letter (see PartyHotkeys keymap notes).
            InputManager.Register("hud.gauges", "Read HUD gauges (momentum / veil / profit factor / timers)",
                InputCategory.Exploration, Ax.HudGauges.ReadAll).AddBinding(KeyCode.K).Grouped("party");

            // Ctrl+F1 — cycle the directional wall-tone bed (Off → When moving → Continuous), spoken. Same chord
            // WrathAccess uses for its wall-tone mode toggle, kept identical for muscle-memory parity. Ctrl+F1 is
            // free (bare F1 is the UI tooltip read; exact-modifier match keeps them apart). See WallTones.
            InputManager.Register("walltones.toggle", "Toggle wall tones (off / when moving / continuous)",
                InputCategory.Exploration, Ex.WallTones.ToggleMode).AddBinding(KeyCode.F1, ctrl: true).Grouped("scanner");

            // Ctrl+F2 — cycle the object sonar (Off → When moving → Continuous), spoken. Same chord WrathAccess
            // uses for its sonar-mode toggle. Ships Off; per-type recorded stems, live-tracked in 3D. See Sonar.
            InputManager.Register("sonar.toggle", "Toggle sonar (off / when moving / continuous)",
                InputCategory.Exploration, Ex.Sonar.ToggleMode).AddBinding(KeyCode.F2, ctrl: true).Grouped("scanner");
        }

        // The review-buffer keys are Global (always polled), so their handlers stand down when not in a
        // loaded game (no party to read).
        private static bool InAGame() => Kingmaker.Game.Instance?.Player != null;
    }
}
