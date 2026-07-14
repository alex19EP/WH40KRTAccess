using System;
using System.Collections.Generic;
using System.Linq;

namespace RTAccess.Screens
{
    /// <summary>
    /// Resolves the active screen stack from RootUIContext each frame (poll-and-diff,
    /// robust to the VM-recreation lifecycle) and dispatches lifecycle events. The
    /// stack is ordered bottom→top by Layer; Current is the top (the focused screen).
    /// Ticked from Main.OnUpdate.
    /// </summary>
    public static class ScreenManager
    {
        private static readonly List<Screen> _registered = new List<Screen>();
        private static List<Screen> _stack = new List<Screen>();
        private static Screen _focused; // the deepest screen the navigator is currently attached to

        // The focused screen = the deepest active child of the top outer screen (== the top when no
        // child sub-screens are pushed).
        public static Screen Current => _stack.Count > 0 ? _stack[_stack.Count - 1].DeepestActiveScreen() : null;
        public static IReadOnlyList<Screen> Stack => _stack;

        /// <summary>Active screens in focus-priority order — the focused screen first (top outer screen's
        /// deepest child), then outward/down to the base context. This is the order the input claim-chain
        /// walks: a deeper screen's categories take precedence (shadow) over a shallower one's.</summary>
        public static IEnumerable<Screen> FocusedFirst()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var chain = new List<Screen>();
                for (var s = _stack[i]; s != null; s = s.ActiveChild) chain.Add(s); // outer → deepest
                for (int j = chain.Count - 1; j >= 0; j--) yield return chain[j];    // deepest → outer
            }
        }

        /// <summary>True when a modal (Exclusive) mod screen is in the active focused chain — the mod owns the
        /// WHOLE keyboard then (the keyboard-arbitration patch mutes every game key), so a stray game hotkey
        /// can't fire under one of our modals (e.g. Ctrl+C popping the character sheet over the New Game wizard).</summary>
        public static bool ExclusiveActive
        {
            get { foreach (var s in FocusedFirst()) if (s.Exclusive) return true; return false; }
        }

        public static void Register(Screen screen) => _registered.Add(screen);

        public static void Tick()
        {
            ApplyDiff(Resolve()); // poll the outer (game-driven) screens → push/pop on the persistent stack
            SyncFocus();          // focus the deepest screen (outer changes; before OnUpdate, as before)
            var cur = Current;    // may push/remove child sub-screens; isolate a faulty screen's build so one
            if (cur != null) Safe(() => cur.OnUpdate(), cur, "OnUpdate"); // bad phase doesn't kill the tick
            SyncFocus();          // re-sync if OnUpdate (or this frame's input) changed the child tree
            // Standardized first-focus: once the focused screen has built its content (some build lazily in
            // OnUpdate), make sure something is focused. No-op when focus already exists or the screen is
            // intentionally unfocused (exploration).
            RTAccess.UI.Navigation.EnsureFocus();
        }

        /// <summary>Active screens, ordered bottom (low layer) → top (high layer).</summary>
        private static List<Screen> Resolve()
        {
            var active = new List<Screen>();
            for (int i = 0; i < _registered.Count; i++)
                if (SafeIsActive(_registered[i])) active.Add(_registered[i]);
            return active.OrderBy(s => s.Layer).ToList();
        }

        private static bool SafeIsActive(Screen s)
        {
            try { return s.IsActive(); }
            catch (Exception e)
            {
                Main.Log?.Error("Screen.IsActive threw for '" + s.Key + "': " + e.Message);
                return false;
            }
        }

        // Diff the polled active set against the persistent stack: pop outer screens that went inactive
        // (each with its whole child subtree, top→bottom) and push newly-active ones (bottom→top). Focus
        // is handled separately by SyncFocus so child-tree changes and outer changes go through one path.
        private static void ApplyDiff(List<Screen> desired)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (!desired.Contains(_stack[i])) PopTree(_stack[i]);
            for (int i = 0; i < desired.Count; i++)
                if (!_stack.Contains(desired[i])) { var s = desired[i]; Safe(() => s.OnPush(), s, "OnPush"); }
            _stack = desired;
        }

        // An outer screen leaving the stack disposes its child subtree (deepest-first), then OnPops itself.
        private static void PopTree(Screen s)
        {
            if (s.ActiveChild != null) s.RemoveChild(s.ActiveChild);
            Safe(() => s.OnPop(), s, "OnPop");
            // Closing a screen clears its nav state (reopening starts fresh) — except screens that opt
            // out because popping isn't really closing (dialogue hiding during a cutscene gap / under
            // the pause menu) or resuming your place is the point (the log).
            if (!s.KeepStateOnPop) RTAccess.UI.Navigation.ScreenClosed(s);
        }

        // Re-attach the navigator whenever the deepest (focused) screen changes — from an outer push/pop
        // OR a child-tree push/remove. No screen overrides OnUnfocus, so firing it after a pop is harmless.
        // Idempotent (no-op when the focused screen is unchanged).
        private static void SyncFocus()
        {
            var cur = Current;
            if (ReferenceEquals(cur, _focused)) return;
            _focused?.OnUnfocus();
            _focused = cur;
            RTAccess.Audio.Earcons.ScreenChange(); // gated cue (no-op unless Earcons.Enabled) — deferred audio
            Safe(() => cur?.OnFocus(), cur, "OnFocus"); // speaks the screen name
            // Bind the navigator; the initial-focus landing is announced by EnsureFocus once the screen's
            // content exists (handles lazy builds + build-time silent Attach uniformly).
            RTAccess.UI.Navigation.Attach(cur);
        }

        private static void Safe(Action a, Screen s, string hook)
        {
            try { a(); }
            catch (Exception e) { Main.Log?.Error("Screen." + hook + " threw for '" + (s?.Key ?? "?") + "': " + e); }
        }

        /// <summary>Register the concrete screens. **Phase 2 fills this in** — each Screen + its
        /// RootUiContext <c>IsActive()</c> predicate (MainMenu/InGame/Inventory/… + service windows), per
        /// docs/plans/mirrored-surfacing-engelbart.md. The engine-generic stack/diff/focus core above is
        /// already live; until Phase 2 no screens are registered, so the framework is exercised by
        /// <see cref="Register"/>ing a scratch <see cref="Screen"/> (e.g. from the dev REPL).</summary>
        public static void Initialize()
        {
            if (_registered.Count > 0) return;
            // Phase 2: base contexts come online one screen at a time. MainMenu is first.
            Register(new MainMenuScreen());
            // In-game surface base context (RootUiContext.IsSurface) — Layer 0, mutually exclusive with the
            // menu. StartUnfocused: exploration owns the arrows, Tab brings up the HUD (party/vitals/combat/
            // service-window openers). Hosts the re-gated exploration helpers in mouse mode.
            Register(new InGameScreen());
            // In-system space map base context (GameModeType.StarSystem, area is a BlueprintStarSystemMap) —
            // Layer 0, mutually exclusive with ctx.ingame (IsSurface is false in space). Objects/Status/Actions
            // tab stops; SpaceEvents voices travel/scan/research. See docs/plans/orbital-listing-wilkes.md.
            Register(new SystemMapScreen());
            // Sector map / warp-travel base context (GameModeType.GlobalMap, area is a BlueprintSectorMapArea) —
            // Layer 0, mutually exclusive with the other base contexts (distinct area type). Systems/Status/Actions
            // tab stops; WarpEvents voices warp/scan/route events. See docs/plans/warp-sector-map-accessibility.md.
            Register(new SectorMapScreen());
            // Voidship combat base context (the game's SpaceCombat HUD component is alive) — Layer 0,
            // mutually exclusive with the other base contexts by that component gate (SystemMapScreen
            // excludes it explicitly: in-system encounter fights keep the star-system AREA). Ship/Battle
            // tab stops; turn/attack narration rides CombatEvents/LogTap. Phase 1 of
            // docs/plans/inertial-broadsiding-tsiolkovsky.md.
            Register(new SpaceCombatScreen());
            // The space-combat victory popup (ExitBattlePopupVM.IsActive, a SpaceCombatVM child) — layer 21,
            // Exclusive. XP / scrap / item / cargo rewards + the popup's own OK / Attune actions. Phase 5 of
            // docs/plans/inertial-broadsiding-tsiolkovsky.md.
            Register(new ExitBattlePopupScreen());
            // The planet-exploration tablet (SpaceStaticPartVM.ExplorationVM.IsExploring — auto-opens on
            // landing) — layer 9, Exclusive: above the map, below service windows (10) / dialogue (15) /
            // loot (24) that stack on it. Scan, POIs, resources/miners, colony + projects. M2 of
            // docs/plans/orbital-listing-wilkes.md.
            Register(new ExplorationScreen());
            // The anomaly research window (SpaceStaticPartVM.AnomalyVM, Show/Hide-tracked) — layer 9,
            // Exclusive; never coexists with the tablet.
            Register(new AnomalyScreen());
            // Stat-check-for-loot modal (POI + anomaly flavours share it) — layer 26 above the tablet;
            // the resulting loot window is the existing LootScreen.
            Register(new StatCheckLootScreen());
            // Expedition send dialog (crew-size slider + reward tiers) — layer 26 above the tablet.
            Register(new ExpeditionScreen());
            // Party picker (GroupChangerContextVM, Surface OR Space) — layer 27; the window GroundOperation
            // POIs raise before loading the ground map, and capital-area party changes elsewhere.
            Register(new GroupChangerScreen());
            // Party-formation window (SurfaceStaticPartVM.FormationVM — HUD menu / Esc menu / Ctrl+N) —
            // layer 16, Exclusive. The WrathAccess-ported preset radio + WASD editing field with member
            // review and Alt+digit grab (main-HUD audit #4).
            Register(new FormationScreen());
            // New Game wizard (MainMenuVM.NewGameVM) — layer 5, above the menu sidebar it's launched from.
            Register(new NewGameScreen());
            // Character generation (MainMenuVM.CharGenContextVM.CharGenVM) — layer 15, full-screen flow.
            Register(new CharGenScreen());
            // Settings (CommonVM.SettingsVM) — layer 25, sits above the menu/in-game context when opened.
            Register(new SettingsScreen());
            // Key-binding capture dialog (SettingsVM.CurrentKeyBindDialog) — layer 27, raw-input
            // passthrough while the game's own capture view reads the pressed combo.
            Register(new KeyBindCaptureScreen());
            // Tutorial popup (CommonVM.TutorialVM, big modal + small hint) — layer 28, a blocking popup that
            // reads its text on appearance even with Focus Mode off. Above windows/dialogue/settings, below
            // the confirm modal (30).
            Register(new TutorialScreen());
            // Message/confirm modal (CommonVM.MessageBoxVM) — layer 30, e.g. the settings save-changes prompt.
            Register(new MessageBoxScreen());
            // The quantity-picker modal (CommonVM.CounterWindowVM) — layer 29, Exclusive, just under the
            // message box. Raised by Split / partial Drop / partial Move from any item context menu.
            Register(new CounterWindowScreen());
            // Chargen character/ship name-entry modal — layer 32, lets the player type a custom name.
            Register(new NameEntryScreen());
            // Dialogue + book-event readers (DialogContextVM under Surface/Space) — layer 15, above the
            // in-game context. These are the cue readers in mouse mode (the console-era cue path was removed).
            Register(new DialogueScreen());
            Register(new BookEventScreen());
            // Service windows (CurrentServiceWindow-driven, Surface OR Space) — layer 10, above the in-game
            // base context. Each reads the live game VM / unit; ServiceWindowAnnounce speaks the window name.
            Register(new InventoryScreen());
            // Empty-slot equip selector (InventoryDollVM.InventorySelectorWindowVM) — layer 12, Exclusive. Raised
            // from a doll slot (Enter on an empty one / "Choose item" on a filled one → HandleChangeItem), it sits
            // just above the InventoryScreen (10) and lists the equippable party items. Confirm/Unequip/Back drive
            // the game's own selector callbacks. See docs/plans/gridless-stocking-babbage.md (slice 4).
            Register(new EquipSelectorScreen());
            // Character visual settings (InventoryDollVM.VisualSettingsVM) — layer 13, Exclusive. The doll's
            // cosmetics panel (helmet/backpack/… visibility + outfit color), raised from the InventoryScreen's
            // "Show visual settings" opener via the doll's own ShowVisualSettings().
            Register(new VisualSettingsScreen());
            // Cargo management service window (party stash ↔ the ship's cargo bays) — layer 10. The stash
            // side reuses the inventory chrome (same InventoryStashVM); the cargo side is one group per bay.
            // Transfer verbs use the window's LIVE routes only (its TryMoveToCargo handler is an empty stub —
            // stash rows go through InventoryHelper directly, the vendor-window lesson).
            Register(new CargoScreen());
            Register(new JournalScreen());
            Register(new CharacterInfoScreen());
            // Colony Management service window (Ctrl+Y; remote colony administration) — layer 10; content
            // renders through the shared ColonyNodes builders the exploration tablet also uses. M3 of
            // docs/plans/orbital-listing-wilkes.md.
            Register(new ColonyManagementScreen());
            // In-game pause/Escape menu (CommonVM.EscMenuContextVM.EscMenu) — layer 20, above contexts/windows,
            // below Settings(25)/MessageBox(30) (it raises a confirm box while staying open).
            Register(new EscMenuScreen());
            // Defeat / game-over screen (GameModeType.GameOver, off the live Surface/Space static part) — layer 21,
            // Exclusive: a terminal modal above the Esc menu, below the Save/Load window (22) its Load button opens.
            // Reads the defeat reason and drives the game's own Quick Load / Load / Main Menu (+ Iron Man) actions.
            Register(new GameOverScreen());
            // Save/Load window (CommonVM.SaveLoadVM) — layer 22, launched from the Esc menu or main menu.
            Register(new SaveLoadScreen());
            // Variative-interaction chooser (SurfaceDynamicPartVM.VariativeInteractionVM) — layer 24, an Exclusive
            // modal raised when interacting with a locked/variative object, so a blind player picks the actor
            // (skill / Tech-Use / Key / Destroy, each with its chance) instead of the game auto-running the first.
            // Raised by RTAccess.Exploration.ProxyMapObject.Interact; outcome voiced by the game log via LogTap.
            Register(new VariativeInteractionScreen());
            // Area-transition / ship-deck fast-travel map (SurfaceStaticPartVM.TransitionVM, a BlueprintMultiEntrance)
            // — layer 24, Exclusive. The window the map key opens inside a ship (Local Map is unbound there) and
            // any multi-entrance object raises. Mirrors the reachable rooms; activating one runs the real
            // AreaTransition. Not a ServiceWindowsType, so it carries its own ScreenName announce.
            Register(new TransitionScreen());
            // Loot window (Surface/Space StaticPartVM.LootContextVM.LootVM) — layer 24, Exclusive. Interacting with
            // a container already opens this window; the screen makes it navigable (item list + Take all + Close).
            // Pass 1 handles the read-and-take modes (StandardChest/Short/ShortUnit); ZoneExit/OneSlot/PlayerChest
            // are gated off in LootScreen.IsActive until their passes. See docs/plans/tiered-gathering-knuth.md.
            Register(new LootScreen());
            // The "collect all before leaving?" confirm the game raises from a ZoneExit loot window's collect-all
            // (ExitLocationWindowVM) — layer 26, Exclusive, sits directly on the LootScreen (24) that spawned it.
            Register(new ExitLocationScreen());
            // OneSlot device-insert loot window (LootVM.IsOneSlot) — layer 24, Exclusive. A device you put a party
            // item into (fuse/cog/key). LootScreen gates OneSlot out of its modes, so exactly one is ever active.
            // Insert via the game's InventoryHelper.InsertToInteractionSlot. See docs/plans/tiered-gathering-knuth.md.
            Register(new OneSlotLootScreen());
            // PlayerChest loot window (LootVM.IsPlayerStash) — layer 24, Exclusive. The personal shared stash: a two-way
            // Chest + Inventory move (withdraw via ILootHandler.HandleChangeLoot, deposit via IInventoryHandler
            // .TryMoveToCargo). LootScreen/OneSlotLootScreen both exclude it, so exactly one loot screen is ever active.
            Register(new PlayerChestScreen());
            // Character level-up (CharacterInfoVM's Progression component in LevelUp mode) — layer 26, Exclusive,
            // sits on and suppresses the read-only CharacterInfoScreen (10). Opened by that screen's "Level Up"
            // action (HandleOpenCharacterInfoPage(LevelProgression)). Career pick → rank selections → commit.
            // See docs/plans/ranked-ascending-lamport.md.
            Register(new LevelUpScreen());
            // Encyclopedia service window (CurrentServiceWindow == Encyclopedia, Surface OR Space) — layer 10.
            // A mod-owned parallel of the game's two-panel window: the fully-expandable chapter/page tree +
            // the selected page's typed content blocks (text/glossary/planet/astropath/book-event/class), with
            // a knowledge-gated bestiary reader for unit blocks. Ported from the WrathAccess recipe.
            Register(new EncyclopediaScreen());
            // Ship management service window (CurrentServiceWindow == ShipCustomization, Surface OR Space —
            // also opens read-only during space combat) — layer 10. Four game-driven tabs (Components /
            // Upgrade / Posts / Accolades) + the always-alive ship status summary; every action drives the
            // game's queued commands. See docs/ship-management-ui-exploration.md.
            Register(new ShipCustomizationScreen());
            // The ship component picker (ShipUpgradeVm.ShipSelectorWindowVM), raised by Enter on a component
            // slot — layer 12, Exclusive, directly above the ship screen (the EquipSelectorScreen shape).
            Register(new ShipItemSelectorScreen());
            // Augmentations service window (CurrentServiceWindow == Augmentations — the DLC3 cybernetics
            // screen, Surface OR Space) — layer 10. Status / body slots / augment stash as Tab stops; the
            // doll is decoration. Two-phase install (dirty slots) and the galvanize toggle are first-class
            // verbs. See docs/plans/galvanized-slotting-ritchie.md.
            Register(new AugmentationsScreen());
            // The augment picker (AugmentationsVM.InventorySelectorWindowVM), raised by Enter on a body
            // slot — layer 12, Exclusive, directly above the augmentations screen (the EquipSelectorScreen shape).
            Register(new AugmentSelectorScreen());
            // Vendor / trade window (Surface OR Space StaticPartVM.VendorVM — a full-screen UI, not a
            // ServiceWindowsType) — layer 24, Exclusive. Trade tab (PF-threshold buys, tiered wares,
            // send-to-cargo stash) + Reputation tab (cargo-for-reputation exchange).
            Register(new VendorScreen());
            // The game's purchase quantity/confirm dialog (VendorTradePartVM.TransitionWindowVM), raised
            // by a wares click or Buy all — layer 26, Exclusive, directly above the VendorScreen.
            Register(new VendorBuyScreen());
            // TODO: the long tail, per docs/plans/mirrored-surfacing-engelbart.md.
            Main.Log?.Log("ScreenManager: " + _registered.Count + " screens registered.");
        }
    }
}
