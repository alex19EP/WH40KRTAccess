using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;      // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.Code.UI.MVVM.VM.Surface;             // SurfaceStaticPartVM
using Kingmaker.Code.UI.MVVM.VM.SurfaceCombat;       // SurfaceHUDVM, InitiativeTrackerUnitVM
using Kingmaker.Code.UI.MVVM.VM.ActionBar;           // ActionBarSlotVM
using Kingmaker.Code.UI.MVVM.VM.ActionBar.Surface;   // SurfaceActionBarVM
using Kingmaker.Code.UI.MVVM.VM.IngameMenu;          // IngameMenuVM
using Kingmaker.Controllers.Combat;                  // GetCombatStateOptional extension
using Kingmaker.EntitySystem.Entities;               // BaseUnitEntity, MechanicEntity
using Kingmaker.GameModes;                           // GameModeType
using Kingmaker.Mechanics.Entities;                  // AbstractUnitEntity
using Kingmaker.UnitLogic;                           // HasMechanicFeature() extension (PartMechanicFeaturesExtension)
using Kingmaker.UnitLogic.Enums;                     // MechanicsFeatureType.HideRealHealthInUI
using Kingmaker.UI.Common;                           // IsDirectlyControllable() extension
using Kingmaker.UI.Models.UnitSettings;              // MechanicActionBarSlotEmpty (overdrive keybind filter)
using Kingmaker.UI.Selection;                        // SelectionManagerBase (Hold/Stop/SelectAll)
using Kingmaker.Blueprints.Root.Strings;             // UIStrings (reuse the game's localized HUD labels)
using Kingmaker.UI.Models.SettingsUI;                // UISettingsRoot (keybind Description labels)
using Kingmaker.Stores;                              // StoreManager (Augmentations DLC gate)
using Kingmaker.Stores.DlcInterfaces;                // DlcNameEnum
using RTAccess.UI;
using RTAccess.UI.Graph;
using UnityEngine;                                   // Mathf

namespace RTAccess.Screens
{
    /// <summary>
    /// The on-foot surface world (<c>RootUiContext.IsSurface</c>) as a navigable screen, graph-native — the
    /// Layer-0 base context the player lands in after character creation. It is UNFOCUSED by default: the
    /// arrows belong to the exploration helpers (cursor/scan/landmarks) and <b>Tab enters the HUD</b>, then
    /// cycles its stops (Tab off the end returns to exploration). Stops, in order: <b>Status</b> (selected
    /// character + wounds, and in combat their AP/MP + whose turn), the <b>Action bar</b> (the selected
    /// unit's usable slots), the <b>Party</b> roster (each member's wounds; Enter selects them), the
    /// <b>Combat</b> panel (turn-based only: status + End turn + initiative order; nothing is emitted out of
    /// combat, so Tab skips it), the <b>Windows</b> list, and the <b>Menu</b> controls.
    ///
    /// Everything renders live per frame — a character swap, an item being consumed, or the initiative order
    /// shifting just re-renders; action-bar focus rides the bar POSITION (the old restore-to-index behavior,
    /// now by construction) while party/initiative focus follows the UNIT through the reconciler. The old
    /// signature/rebuild/restore machinery is gone. <see cref="ExplorationActive"/> is the single gate the
    /// console-era exploration helpers ride (mouse mode, while this screen is up and the world is
    /// interactive) in place of their old gamepad-mode check.
    /// </summary>
    public sealed class InGameScreen : Screen
    {
        public override string Key => "ctx.ingame";
        public override string ScreenName => null;          // regions self-label; no per-focus screen announce
        public override int Layer => 0;                     // base context (mutually exclusive with MainMenu/Space)
        public override bool StartUnfocused => true;        // exploration owns the arrows; Tab brings up the HUD
        public override bool AllowsTypeahead => false;      // letters/chords stay exploration hotkeys

        public override bool IsActive() => Game.Instance?.RootUiContext?.IsSurface ?? false;

        // Both UI and exploration are live in-game; HUD focus decides which wins shared chords (arrows/Enter).
        // Without world control (cutscene/dialogue/loading) drop exploration but keep the HUD reachable.
        private static readonly RTAccess.Input.InputCategory[] FocusedCats =
        {
            RTAccess.Input.InputCategory.UI, RTAccess.Input.InputCategory.InGame,
            RTAccess.Input.InputCategory.Exploration, RTAccess.Input.InputCategory.Windows,
        };
        private static readonly RTAccess.Input.InputCategory[] UnfocusedCats =
        {
            RTAccess.Input.InputCategory.Exploration, RTAccess.Input.InputCategory.InGame,
            RTAccess.Input.InputCategory.UI, RTAccess.Input.InputCategory.Windows,
        };
        private static readonly RTAccess.Input.InputCategory[] NoControlCats =
        {
            RTAccess.Input.InputCategory.InGame, RTAccess.Input.InputCategory.UI,
        };
        public override IReadOnlyList<RTAccess.Input.InputCategory> InputCategories =>
            !ControlState.HasControl ? NoControlCats
          : Navigation.HasFocus     ? FocusedCats
          :                           UnfocusedCats;

        // Escape backs out of the focused HUD to exploration — a direct exit (Tab off the last region does the
        // same, announced via UnfocusedAnnouncement below). Only acts while something is focused: on the bare
        // HUD nothing is focused, and ui.back is a context-split (YieldsWhenUnfocused) that hands Escape to the
        // game there, so it opens the game's pause menu instead. The no-op guard means that yield path is
        // untouched even though ui.back is still dispatched to us this frame. The guard is the node-based
        // HasFocus, NOT Navigation.Current — graph-native nodes have no backing UIElement, so Current is null
        // even while a HUD node is focused.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Raw("Back"), _ =>
            {
                if (!Navigation.HasFocus) return; // unfocused → let the yield reach the game (pause menu)
                // #3: an open variant/convert flyout claims Escape first — mirroring the sighted
                // ActionBarConvertedPCView, whose Escape closes the flyout. Close() runs the parent slot's
                // own CloseConvert, so the variant rows drop out of the next render.
                if (CloseOpenConverts())
                {
                    Tts.Speak(Loc.T("slot.variants_closed"), interrupt: true);
                    return;
                }
                Navigation.Blur();
                Tts.Speak(Loc.T("nav.exploration"), interrupt: true);
            });
        }

        // Close every open variant/convert flyout on the bar; true when any was open. Nothing in the game
        // enforces a single open flyout (each slot toggles its own; only turn start / unbind mass-close), so
        // Escape sweeps them all rather than guessing which one the player means (review finding).
        private static bool CloseOpenConverts()
        {
            bool any = false;
            foreach (var (s, _) in BarSlots())
            {
                var c = s?.ConvertedVm?.Value;
                if (c != null && !c.IsDisposed) { c.Close(); any = true; }
            }
            return any;
        }

        // Tab off either end of the HUD lands the keys back in exploration — say so (ScreenName is null by
        // design, so the navigator's default exit announce would be silent). Same line Escape speaks.
        public override string UnfocusedAnnouncement => Loc.T("nav.exploration");

        /// <summary>The shared gate the exploration helpers ride: this screen is the live top screen (no window
        /// or dialogue layered over it), the game exists, and we're on-foot (Default = exploration AND surface
        /// tactical combat). Replaces the helpers' old <c>ControllerMode == Gamepad</c> check so they work in
        /// mouse mode.</summary>
        public static bool ExplorationActive =>
            ScreenManager.Current?.Key == "ctx.ingame"
            && Game.Instance != null
            && Game.Instance.CurrentMode == GameModeType.Default;


        public override void Build(GraphBuilder b)
        {
            BuildStatus(b);
            BuildActions(b);
            BuildParty(b);
            BuildCombat(b);
            BuildWindows(b);
            BuildMenu(b);
        }

        // ---- Status stop ----

        private static void BuildStatus(GraphBuilder b)
        {
            b.BeginStop("status").SetRegion("hud:status");
            b.AddItem(ControlId.Structural("hud:status"), GraphNodes.Text(() => StatusLine()));
        }

        // ---- Windows stop (static set) ----

        private static readonly ServiceWindowsType[] WindowButtons =
        {
            ServiceWindowsType.Inventory, ServiceWindowsType.CharacterInfo, ServiceWindowsType.Journal,
            ServiceWindowsType.LocalMap, ServiceWindowsType.Encyclopedia,
            // The Rogue-Trader-specific service windows the game gates on ship access / colonization / DLC.
            ServiceWindowsType.ShipCustomization, ServiceWindowsType.ColonyManagement,
            ServiceWindowsType.CargoManagement, ServiceWindowsType.Augmentations,
        };

        private static void BuildWindows(GraphBuilder b)
        {
            b.BeginStop("windows").SetRegion("hud:windows");
            b.PushContext(Loc.T("hud.windows"), Loc.T("role.list"));
            foreach (var t in WindowButtons)
            {
                var type = t; // capture for the live closures
                // The HUD service-window openers are Plastick in the game (IngameMenuNewPCView.SetClickAndHoverSound).
                b.AddItem(ControlId.Structural("hud:win:" + type), GraphNodes.Button(
                    () => WindowLabel(type), () => ServiceWindows()?.HandleOpenWindowOfType(type),
                    () => WindowEnabled(type),
                    hoverSound: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound,
                    clickSound: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound));
            }
            // The message-log review — our own overlay (not a game ServiceWindowsType), listed beside the game
            // windows for discoverability. Also on bare L (see InputBindings). See LogReviewScreen.
            b.AddItem(ControlId.Structural("hud:win:log"), GraphNodes.Button(
                () => Loc.T("hud.log"), LogReviewScreen.Open));
            b.PopContext();
        }

        // internal: SystemMapScreen offers the same openers on the space map (same labels/gates, Space VM).
        // The label + gate live in the shared ServiceWindowInfo home so the HUD list, the star-system openers,
        // and the on-open announcer never drift; the HUD list keeps its own type.ToString() belt for an
        // unexpected type (the shared Label returns null for None/unknown so the announcer can stay silent).
        internal static string WindowLabel(ServiceWindowsType type)
            => ServiceWindowInfo.Label(type) ?? type.ToString();

        internal static bool WindowEnabled(ServiceWindowsType type) => ServiceWindowInfo.Enabled(type);

        // ---- Action bar region (abilities / weapon attacks / consumables the selected unit can use) ----

        // All action-bar slots in a stable order — current weapon set, abilities, consumables, heroic acts,
        // desperate measures, overdrive. RT has no unified slot list (the bar is split into part VMs); this
        // mirrors the set the game itself refreshes in SurfaceActionBarVM.UpdateSlotsCommandHandler.
        // Each slot is paired with the game's own direct-activation keybinding NAME for that bar section
        // (main-HUD audit #10: ActionBarSlotPCView.GetBindName — Weapon/Ability/Consumable + the index WITHIN
        // its own section over the raw list, matching the game's per-part SetKeyBinding(i) loops; those bare
        // 1–0 hotkeys still reach the game for a blind player, so the slot node advertises them). Heroic-act,
        // desperate-measure and overdrive slots never receive a binding — null.
        private static IEnumerable<(ActionBarSlotVM vm, string bind)> BarSlots()
        {
            var bar = ActionBar();
            if (bar == null) yield break;
            var set = bar.Weapons?.CurrentSet?.Value;
            if (set?.AllSlots != null)
            { int i = 0; foreach (var s in set.AllSlots) yield return (s, $"ActionBarWeaponButton{i++:D2}"); }
            if (bar.Abilities?.Slots != null)
            {
                // The game does NOT keybind the raw Slots list: SurfaceActionBarPartAbilitiesBaseView
                // .GetGridSlots filters out a slot duplicating the augments-overdrive ability (same KeyName)
                // BEFORE SetKeyBinding numbers the rest — so that duplicate gets no key and must not shift
                // the numbering, or every later spoken hotkey fires the wrong ability (review finding).
                var od = bar.Abilities.OverdriveSlotVM?.MechanicActionBarSlot;
                string odKey = od != null && !(od is MechanicActionBarSlotEmpty) ? od.KeyName : null;
                int i = 0;
                foreach (var s in bar.Abilities.Slots)
                {
                    bool odDup = odKey != null && s?.MechanicActionBarSlot?.KeyName == odKey;
                    yield return (s, odDup ? null : $"ActionBarAbilityButton{i:D2}");
                    if (!odDup) i++;
                }
            }
            if (bar.Consumables?.Slots != null)
            { int i = 0; foreach (var s in bar.Consumables.Slots) yield return (s, $"ActionBarConsumableButton{i++:D2}"); }
            var momentum = bar.SurfaceMomentumVM;
            if (momentum?.HeroicActSlots != null) foreach (var s in momentum.HeroicActSlots) yield return (s, null);
            if (momentum?.DesperateMeasureSlots != null) foreach (var s in momentum.DesperateMeasureSlots) yield return (s, null);
            var overdrive = bar.Abilities?.OverdriveSlotVM;
            if (overdrive != null) yield return (overdrive, null);
        }

        // A real, usable slot: not a non-current-weapon-set skeleton (IsFake), not empty, backed by a
        // supported mechanic (not IsBad).
        private static bool Usable(ActionBarSlotVM s)
            => s != null && !s.IsFake.Value && !s.IsEmpty.Value
               && s.MechanicActionBarSlot != null && !s.MechanicActionBarSlot.IsBad();

        // The bar as one stop, slots keyed by POSITION among the emitted rows: a character swap or an item
        // being consumed re-renders and focus stays at the same bar index (the old restore-to-index
        // behavior, now by construction) — the slot's LIVE label re-reads the new occupant under focus.
        private static void BuildActions(GraphBuilder b)
        {
            b.BeginStop("actions").SetRegion("hud:actions");
            b.PushContext(Loc.T("hud.actions"), Loc.T("role.list"));
            // The augmentation-overdrive slot carries themed hover/click sounds in the game; tag it so the
            // factory replays them (every other slot uses the generic hover + its own click).
            var overdrive = ActionBar()?.Abilities?.OverdriveSlotVM;
            int i = 0;
            foreach (var (s, bind) in BarSlots())
            {
                if (!Usable(s)) continue;
                b.AddItem(ControlId.Referenced(s, "hud:act:" + i++),
                    ActionBarNodes.Slot(s, isOverdrive: ReferenceEquals(s, overdrive), bindName: bind));
                // #3 (main-HUD audit) — an OPEN variant/convert flyout renders its choices as ordinary slot
                // rows right after their parent (immediate mode: they appear the frame OnMainClick opens the
                // flyout and vanish when it closes). Each row casts through its own converted slot's
                // OnMainClick, exactly like the sighted flyout buttons; Escape closes (see GetActions).
                var conv = s.ConvertedVm?.Value;
                if (conv != null && !conv.IsDisposed)
                    for (int j = 0; j < conv.Slots.Count; j++)
                    {
                        var vs = conv.Slots[j];
                        if (Usable(vs)) b.AddItem(ControlId.Referenced(vs, "hud:actvar:" + i + ":" + j),
                            ActionBarNodes.Slot(vs));
                    }
            }
            b.PopContext();
        }

        // ---- Menu stop (the compass-corner control cluster) ----

        // RT keeps no menu VM for these controls (in RT the IngameMenuVM is only window-openers) — each is a
        // direct game API. Toggles speak their new state on press and re-read their live state into the label
        // per render. (No five-foot step / delay-turn / surface Rest in RT; the turn-based and speed-up
        // toggles are omitted — no clean stateful API.)
        private static void BuildMenu(GraphBuilder b)
        {
            b.BeginStop("menu").SetRegion("hud:menu");
            b.PushContext(GameText.Or(() => UIStrings.Instance.CommonTexts.Menu, "hud.menu"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural("hud:menu:pause"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.CommonTexts.Pause, "hudmenu.pause") + ", " + OnOff(Game.Instance != null && Game.Instance.IsPaused),
                TogglePause));
            b.AddItem(ControlId.Structural("hud:menu:hold"), GraphNodes.Button(
                () => GameText.Or(() => UISettingsRoot.Instance.UIKeybindGeneralSettings.Hold.Description, "hudmenu.hold"),
                () => SelectionManagerBase.Instance?.Hold()));
            b.AddItem(ControlId.Structural("hud:menu:stop"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.ActionTexts.Stop, "hudmenu.stop"),
                () => SelectionManagerBase.Instance?.Stop()));
            b.AddItem(ControlId.Structural("hud:menu:select_all"), GraphNodes.Button(
                () => GameText.Or(() => UISettingsRoot.Instance.UIKeybindSelectCharacterSettings.SelectAll.Description, "hudmenu.select_all"),
                () => SelectionManagerBase.Instance?.SelectAll()));
            b.AddItem(ControlId.Structural("hud:menu:formation"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.FormationTexts.FormationLabel, "hudmenu.formation") + (IngameMenu()?.IsFormationActive?.Value == true ? ", " + Loc.T("combat.active_marker") : ""),
                () => IngameMenu()?.OpenFormation(),
                hoverSound: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound,     // Plastick in IngameMenuNewPCView
                clickSound: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound));
            b.AddItem(ControlId.Structural("hud:menu:inspect"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.MainMenu.Inspect, "hudmenu.inspect") + ", " + OnOff(Game.Instance?.Player?.UISettings?.ShowInspect ?? false),
                ToggleInspect));
            b.AddItem(ControlId.Structural("hud:menu:reset_camera"), GraphNodes.Button(
                () => GameText.Or(() => UISettingsRoot.Instance.UIKeybindGeneralSettings.CameraRotateToPointNorth.Description, "hudmenu.reset_camera"),
                () => Kingmaker.View.CameraRig.Instance?.ResetCameraRotate()));
            b.PopContext();
        }

        private static string OnOff(bool on) => Loc.T(on ? "value.on" : "value.off");

        private static void TogglePause()
        {
            var g = Game.Instance;
            if (g == null) return;
            // This keypress speaks its own confirmation below — mark the exact edge it causes as already
            // spoken so the passive PauseAnnouncer doesn't double it (main-HUD audit #5).
            RTAccess.Accessibility.PauseAnnouncer.SuppressNext(!g.IsPaused);
            // The pause-mode change settles a frame later, so reading g.IsPaused right after the set is stale —
            // announce the state we just asked for, not the not-yet-updated getter.
            bool willPause = !g.IsPaused;
            g.IsPaused = willPause;
            Tts.Speak(willPause ? GameText.Or(() => UIStrings.Instance.CommonTexts.Paused, "pause.paused") : Loc.T("pause.unpaused"), interrupt: true);
        }

        private static void ToggleInspect()
        {
            var ui = Game.Instance?.Player?.UISettings;
            if (ui == null) return;
            bool on = !ui.ShowInspect;
            ui.ShowInspect = on;
            Tts.Speak(Loc.T(on ? "hud.inspect_on" : "hud.inspect_off"), interrupt: true);
        }

        // ---- Party stop ----

        // One roster row per member, keyed by the UNIT so focus follows the member through joins/leaves
        // (the reconciler's identity match — the old restore-to-member rebuild, now by construction). The
        // live label re-reads name + wounds + selected marker each frame; Enter selects the member.
        private static void BuildParty(GraphBuilder b)
        {
            b.BeginStop("party").SetRegion("hud:party");
            b.PushContext(Loc.T("hud.party"), Loc.T("role.list"));
            foreach (var u in Controllable())
            {
                var unit = u; // loop-local copy for the closures
                b.AddItem(ControlId.Referenced(unit, "hud:party:" + unit.UniqueId), new NodeVtable
                {
                    ControlType = ControlTypes.Text, // a readout row, not a button — no role word
                    Announcements = new List<NodeAnnouncement> { GraphNodes.LabelPart(() => PartyLabel(unit)) },
                    SearchText = () => unit.CharacterName,
                    OnActivate = () => Select(unit),
                    // Selecting flips the unit's IsSelected VM reactive, which the live PartyCharacterPCView
                    // already answers with Character.CharacterSelect — so no generic click of ours on top
                    // (the spoken feedback is SelectionAnnouncer.Announce(force: true) inside Select).
                    ActivateSound = null,
                });
            }
            b.PopContext();
        }

        /// <summary>The directly-controllable party members, in party order — the roster + selectable set
        /// (mirrors <see cref="RTAccess.Accessibility.PartyHotkeys"/>).</summary>
        private static List<BaseUnitEntity> Controllable()
        {
            var list = new List<BaseUnitEntity>();
            var party = Game.Instance?.Player?.Party;
            if (party == null) return list;
            foreach (var u in party)
                if (u != null && u.IsDirectlyControllable()) list.Add(u);
            return list;
        }

        private static string PartyLabel(BaseUnitEntity unit)
        {
            try
            {
                if (unit == null) return "";
                var sb = new StringBuilder(unit.CharacterName);
                AppendWounds(sb, unit);
                if (Game.Instance?.SelectionCharacter?.SelectedUnit?.Value == unit) sb.Append(", ").Append(Loc.T("unit.selected"));
                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("InGameScreen.PartyLabel: " + e); return ""; }
        }

        private static void Select(BaseUnitEntity unit)
        {
            if (unit == null) return;
            try
            {
                Game.Instance.SelectionCharacter.SetSelected(unit);
                // One selection-announce path (force → always speaks + marks the unit so the poll won't echo it).
                RTAccess.Accessibility.SelectionAnnouncer.Announce(unit, force: true);
            }
            catch (Exception e) { Main.Log?.Error("InGameScreen.Select failed: " + e); }
        }

        // ---- Status line ----

        // internal so the R hotkey (PartyHotkeys.CombatStatus) can speak the same line the HUD status element shows,
        // one-press, without a trip through HUD focus.
        internal static string StatusLine()
        {
            try
            {
                var game = Game.Instance;
                if (game == null) return "";
                var unit = SelectedUnit();
                if (unit == null) return Loc.T("status.no_selection");

                var sb = new StringBuilder(unit.CharacterName);
                AppendWounds(sb, unit);

                if (game.TurnController.TurnBasedModeActive)
                {
                    var cs = unit.GetCombatStateOptional();
                    if (cs != null)
                        sb.Append(", ").Append(Loc.T("combat.ap_mp",
                            new { ap = cs.ActionPointsYellow, mp = Mathf.RoundToInt(cs.ActionPointsBlue) }));
                    var turnUnit = game.TurnController.CurrentUnit;
                    if (turnUnit != null)
                        sb.Append(", ").Append(Loc.T(
                            game.TurnController.IsPlayerTurn ? "combat.turn" : "combat.turn_enemy",
                            new { name = NameOf(turnUnit) }));
                }
                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("InGameScreen.StatusLine: " + e); return ""; }
        }

        // ---- Combat stop (turn-based only) ----

        // Out of turn-based mode nothing is emitted — the stop doesn't exist, so Tab skips it entirely (the
        // old empty-container behavior). Initiative rows are keyed by the UNIT, so focus follows the unit
        // as the order shifts; the divider re-positions with RoundIndex by construction (per-frame render).
        private static void BuildCombat(GraphBuilder b)
        {
            var game = Game.Instance;
            if (game == null || !game.TurnController.TurnBasedModeActive) return;

            b.BeginStop("combat").SetRegion("hud:combat");
            b.PushContext(Loc.T("hud.combat"), Loc.T("role.list"));

            // The combat-aware status line, focusable on its own.
            b.AddItem(ControlId.Structural("hud:cstatus"), GraphNodes.Text(() => StatusLine()));

            // TryEndPlayerTurnManually plays Combat.EndTurn itself (TurnController), so no ActivateSound —
            // a generic ButtonClick would stack on top of the real end-turn sting.
            Func<string> endTurnLabel = () => GameText.Or(() => UIStrings.Instance.HUDTexts.EndTurn, "turn.end");
            b.AddItem(ControlId.Structural("hud:endturn"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(endTurnLabel),
                    GraphNodes.DisabledPart(() => game.TurnController.CanEndTurn),
                },
                SearchText = endTurnLabel,
                OnActivate = () => { if (game.TurnController.CanEndTurn) game.TurnController.TryEndPlayerTurnManually(); },
                ActivateSound = null,
            });

            var tracker = SurfaceHUD()?.InitiativeTrackerVM?.Value;
            if (tracker?.Units != null)
            {
                var units = tracker.Units;
                for (int i = 0; i < units.Count; i++)
                {
                    var vm = units[i];
                    if (vm == null) continue;
                    // #10 Next-round divider. InitiativeTrackerVM sets RoundIndex to the index of the LAST
                    // current-turn unit (InitiativeTrackerVM.UpdateUnits: RoundIndex = num after the current-turn
                    // loop, before next-turn units get ++num), so units[0..RoundIndex] act this round and
                    // units[RoundIndex+1..] act next round. The divider therefore belongs BEFORE the first
                    // next-turn unit (i == RoundIndex+1), not before the last current one. Labelled with the
                    // upcoming round number — or "surprise round" (round 0). The label re-fetches the tracker
                    // VM (it's swapped between rounds), so it never reads a stale capture under focus.
                    if (i == tracker.RoundIndex + 1)
                        b.AddItem(ControlId.Structural("hud:round"), GraphNodes.Text(
                            () => RoundDividerLabel(SurfaceHUD()?.InitiativeTrackerVM?.Value)));
                    // #17 squads render collapsed on the tracker: a member's card is hidden behind the leader's
                    // (which carries the alive-count badge) unless the player expands the squad — the NeedToShow
                    // toggle InitiativeTrackerSquadLeaderVM propagates leader→members. Mirror the exact
                    // InitiativeTrackerVerticalView filter so the review speaks one entry per squad, after the
                    // divider check above so a skipped member can't swallow the round boundary.
                    if (vm.IsInSquad.Value && !vm.IsSquadLeader.Value && !vm.NeedToShow.Value) continue;
                    var row = vm; // loop-local copy for the closure
                    b.AddItem(ControlId.Referenced(row, "hud:init:" + (row.Unit?.UniqueId ?? "slot" + i)),
                        GraphNodes.Text(() => InitiativeLabel(row)));
                }
            }
            b.PopContext();
        }

        private static string InitiativeLabel(InitiativeTrackerUnitVM vm)
        {
            try
            {
                if (vm?.Unit == null) return "";
                var sb = new StringBuilder(NameOf(vm.Unit));

                // #8 Faction frame the card shows (SurfaceCombatUnitVM.IsEnemy/IsPlayer, set from the unit's faction
                // in UpdateData). Parity-safe: the tracker lists only shown participants, so no visibility gate here.
                sb.Append(", ").Append(Loc.T(
                    vm.IsEnemy.Value  ? "combat.faction_enemy"
                  : vm.IsPlayer.Value ? "combat.faction_ally"
                  :                     "combat.faction_neutral"));

                // #17 the leader card's squad badge (SurfaceCombatUnitOrderView shows m_SquadNumber only when
                // IsSquadLeader && HasAliveUnitsInSquad; SquadCount = living members). Member cards are collapsed
                // out by the queue walk, so the squad reads as one entry carrying its strength.
                if (vm.IsSquadLeader.Value && vm.HasAliveUnitsInSquad.Value)
                    sb.Append(", ").Append(Loc.T("combat.squad_of", new { n = vm.SquadCount.Value }));

                // #9 Per-card HP (the card renders UIUtility.GetHpText). Only read a foe's HP the game itself would
                // show — party units are always visible, enemy/neutral only when IsVisibleForPlayer (belt-and-braces
                // over the tracker's own visibility filter) — and mask to the "???" token exactly when the card does
                // (HideRealHealthInUI), never leaking the raw number. Squads/placeholders with no BaseUnitEntity are
                // skipped (no Health part to read).
                var body = vm.UnitAsBaseUnitEntity;
                var h = body?.Health;
                if (h != null && (vm.Unit.IsPlayerFaction || vm.Unit.IsVisibleForPlayer))
                    sb.Append(", ").Append(vm.Unit.HasMechanicFeature(MechanicsFeatureType.HideRealHealthInUI)
                        ? Loc.T("combat.hp_unknown")
                        : Loc.T("combat.hp", new { cur = h.HitPointsLeft, max = h.MaxHitPoints }));

                // #19 Ordinal position in the queue (InitiativeTrackerUnitVM.OrderIndex — the card's hover UnitOrder
                // hint). The acting unit keeps the clearer "current" marker instead of "order 0".
                if (Game.Instance?.TurnController?.CurrentUnit == vm.Unit) sb.Append(", ").Append(Loc.T("combat.current"));
                else if (vm.OrderIndex != null && vm.OrderIndex.Value > 0) sb.Append(", ").Append(Loc.T("combat.unit_order", new { n = vm.OrderIndex.Value }));

                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("InGameScreen.InitiativeLabel: " + e); return ""; }
        }

        // The #10 round divider's label. The tracker's own end-of-round card VM (RoundVM.Round = RoundCounter+1)
        // carries the number; round 0 is the surprise round, which the game's card renders as literal "S"
        // (InitiativeTrackerEndOfRound.SetRound). Read live so it advances without a rebuild.
        private static string RoundDividerLabel(InitiativeTrackerVM tracker)
        {
            try
            {
                int round = tracker?.RoundVM?.Round?.Value ?? 0;
                return round == 0 ? Loc.T("combat.surprise_round") : Loc.T("combat.next_round", new { n = round });
            }
            catch (Exception e) { Main.Log?.Error("InGameScreen.RoundDividerLabel: " + e); return ""; }
        }

        // ---- shared reads (null-propagating) ----

        private static BaseUnitEntity SelectedUnit()
        {
            var sel = Game.Instance?.SelectionCharacter;
            if (sel == null) return null;
            return sel.SelectedUnit.Value ?? sel.FirstSelectedUnit;
        }

        private static void AppendWounds(StringBuilder sb, BaseUnitEntity unit)
        {
            var h = unit?.Health;
            if (h == null) return;
            sb.Append(", ").Append(Loc.T("unit.wounds", new { current = h.HitPointsLeft, max = h.MaxHitPoints }));
            if (h.TemporaryHitPoints > 0) sb.Append(", ").Append(Loc.T("unit.wounds_temp", new { temp = h.TemporaryHitPoints }));
        }

        private static string NameOf(MechanicEntity e)
            => (e as AbstractUnitEntity)?.CharacterName ?? e?.Name;

        private static SurfaceStaticPartVM StaticPart() => Game.Instance?.RootUiContext?.SurfaceVM?.StaticPartVM;
        private static SurfaceHUDVM SurfaceHUD() => StaticPart()?.SurfaceHUDVM;
        private static ServiceWindowsVM ServiceWindows() => StaticPart()?.ServiceWindowsVM;
        private static SurfaceActionBarVM ActionBar() => SurfaceHUD()?.ActionBarVM;
        private static IngameMenuVM IngameMenu() => SurfaceHUD()?.IngameMenuVM;
    }
}
