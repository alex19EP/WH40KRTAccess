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
using Kingmaker.UI.Common;                           // IsDirectlyControllable() extension
using Kingmaker.UI.Selection;                        // SelectionManagerBase (Hold/Stop/SelectAll)
using Kingmaker.Blueprints.Root.Strings;             // UIStrings (reuse the game's localized HUD labels)
using Kingmaker.UI.Models.SettingsUI;                // UISettingsRoot (keybind Description labels)
using Kingmaker.Stores;                              // StoreManager (Augmentations DLC gate)
using Kingmaker.Stores.DlcInterfaces;                // DlcNameEnum
using RTAccess.UI;
using RTAccess.UI.Proxies;
using UnityEngine;                                   // Mathf

namespace RTAccess.Screens
{
    /// <summary>
    /// The on-foot surface world (<c>RootUiContext.IsSurface</c>) as a navigable screen — the Layer-0 base
    /// context the player lands in after character creation. It is UNFOCUSED by default: the arrows belong to
    /// the exploration helpers (cursor/scan/landmarks) and <b>Tab enters the HUD</b>, then cycles its regions
    /// (Tab off the end returns to exploration). Regions, in order: <b>Status</b> (selected character + wounds,
    /// and in combat their AP/MP + whose turn), the <b>Party</b> roster (each member's wounds; Enter selects
    /// them), the <b>Combat</b> panel (turn-based only: status + End turn + initiative order; empty/skipped out
    /// of combat), and the <b>Windows</b> list (character sheet / inventory / journal / map / encyclopedia).
    ///
    /// Membership-bearing regions (Party, Combat) are rebuilt only when their signature changes (a member
    /// joining/leaving, combat toggling, initiative order shifting); every label is a live <c>Func</c>, so
    /// wounds/AP/MP and the active-turn marker read current without a rebuild. <see cref="ExplorationActive"/>
    /// is the single gate the console-era exploration helpers now ride (mouse mode, while this screen is up and
    /// the world is interactive) in place of their old gamepad-mode check.
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
        // same, but silently, since ScreenName is null). Only acts while something is focused: on the bare HUD
        // nothing is focused, and ui.back is a context-split (YieldsWhenUnfocused) that hands Escape to the game
        // there, so it opens the game's pause menu instead. The no-op guard means that yield path is untouched
        // even though ui.back is still dispatched to us this frame.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Raw("Back"), _ =>
            {
                if (Navigation.Current == null) return; // unfocused → let the yield reach the game (pause menu)
                Navigation.Blur();
                Tts.Speak(Loc.T("nav.exploration"), interrupt: true);
            });
        }

        /// <summary>The shared gate the exploration helpers ride: this screen is the live top screen (no window
        /// or dialogue layered over it), the game exists, and we're on-foot (Default = exploration AND surface
        /// tactical combat). Replaces the helpers' old <c>ControllerMode == Gamepad</c> check so they work in
        /// mouse mode.</summary>
        public static bool ExplorationActive =>
            ScreenManager.Current?.Key == "ctx.ingame"
            && Game.Instance != null
            && Game.Instance.CurrentMode == GameModeType.Default;

        private TextElement _status;     // selected char + wounds + (in combat) AP/MP + whose turn
        private ListContainer _actions;  // the action bar (abilities/weapons/consumables); rebuilt on change
        private ListContainer _party;    // roster; Enter selects
        private ListContainer _combat;   // turn-based only; empty/skipped out of combat
        private string _actionsSig;      // last action-bar content signature
        private string _partySig;        // last party-membership signature
        private string _combatSig;       // last combat/initiative signature

        public override void OnPush()
        {
            Clear();
            _status = new TextElement(StatusLine);
            Add(_status);
            _actions = new ListContainer(Loc.T("hud.actions"));
            Add(_actions);
            _party = new ListContainer(Loc.T("hud.party"));
            Add(_party);
            _combat = new ListContainer(Loc.T("hud.combat"));
            Add(_combat);
            BuildWindows();
            BuildMenu();

            RebuildActions();
            RebuildParty();
            RebuildCombat();
            _actionsSig = ActionsSig();
            _partySig = PartySig();
            _combatSig = CombatSig();
        }

        public override void OnPop()
        {
            Clear();
            _status = null; _actions = null; _party = null; _combat = null;
            _actionsSig = null; _partySig = null; _combatSig = null;
        }

        public override void OnUpdate()
        {
            if (_party == null) { OnPush(); return; } // defensive: ensure the shell exists

            var asig = ActionsSig();
            if (asig != _actionsSig) { _actionsSig = asig; RebuildActions(); }

            var ps = PartySig();
            if (ps != _partySig) { _partySig = ps; RebuildParty(); }

            var cs = CombatSig();
            if (cs != _combatSig) { _combatSig = cs; RebuildCombat(); }
        }

        // ---- Windows region (static set; built once) ----

        private static readonly ServiceWindowsType[] WindowButtons =
        {
            ServiceWindowsType.Inventory, ServiceWindowsType.CharacterInfo, ServiceWindowsType.Journal,
            ServiceWindowsType.LocalMap, ServiceWindowsType.Encyclopedia,
            // The Rogue-Trader-specific service windows the game gates on ship access / colonization / DLC.
            ServiceWindowsType.ShipCustomization, ServiceWindowsType.ColonyManagement,
            ServiceWindowsType.CargoManagement, ServiceWindowsType.Augmentations,
        };

        private void BuildWindows()
        {
            var windows = new ListContainer(Loc.T("hud.windows"));
            foreach (var t in WindowButtons)
            {
                var type = t; // capture for the live closure
                // The HUD service-window openers are Plastick in the game (IngameMenuNewPCView.SetClickAndHoverSound).
                windows.Add(new ProxyActionButton(WindowLabel(type), () => WindowEnabled(type),
                    () => ServiceWindows()?.HandleOpenWindowOfType(type), actionVerb: "open",
                    hoverSoundType: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound,
                    clickSoundType: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound));
            }
            // The message-log review — our own overlay (not a game ServiceWindowsType), listed beside the game
            // windows for discoverability. Also on bare L (see InputBindings). See LogReviewScreen.
            windows.Add(new ProxyActionButton(() => Loc.T("hud.log"), () => true,
                LogReviewScreen.Open, actionVerb: "open"));
            Add(windows);
        }

        private static string WindowLabel(ServiceWindowsType type)
        {
            switch (type)
            {
                case ServiceWindowsType.Inventory: return GameText.Or(() => UIStrings.Instance.MainMenu.Inventory, "screen.inventory");
                case ServiceWindowsType.CharacterInfo: return GameText.Or(() => UIStrings.Instance.MainMenu.CharacterInfo, "screen.character");
                case ServiceWindowsType.Journal: return GameText.Or(() => UIStrings.Instance.MainMenu.Journal, "screen.journal");
                case ServiceWindowsType.LocalMap: return GameText.Or(() => UIStrings.Instance.MainMenu.LocalMap, "screen.map");
                case ServiceWindowsType.Encyclopedia: return GameText.Or(() => UIStrings.Instance.MainMenu.Encyclopedia, "screen.encyclopedia");
                case ServiceWindowsType.ShipCustomization: return GameText.Or(() => UIStrings.Instance.MainMenu.ShipCustomization, "screen.ship");
                case ServiceWindowsType.ColonyManagement: return GameText.Or(() => UIStrings.Instance.MainMenu.ColonyManagement, "screen.colony");
                case ServiceWindowsType.CargoManagement: return GameText.Or(() => UIStrings.Instance.MainMenu.CargoManagement, "screen.cargo");
                case ServiceWindowsType.Augmentations: return GameText.Or(() => UIStrings.Instance.MainMenu.Augmentations, "screen.augmentations");
                default: return type.ToString();
            }
        }

        // Availability gate mirroring the game's own HUD button visibility (IngameMenuNewPCView.CheckEnabled* /
        // CheckServiceWindowsBlocked). The original five stay always-offered (unchanged behavior); the four RT
        // windows read as disabled exactly when the game would hide their buttons, so Enter never drops the player
        // into a window the game is refusing. HandleOpenWindow is itself a no-op when blocked, so this is belt-and-
        // braces, not the only guard.
        private static bool WindowEnabled(ServiceWindowsType type)
        {
            var player = Game.Instance?.Player;
            if (player == null) return false;
            switch (type)
            {
                case ServiceWindowsType.ShipCustomization:
                {
                    bool canShip = player.CanAccessStarshipInventory;
                    bool blocked = player.ServiceWindowsBlocked;
                    return canShip && !blocked;
                }
                case ServiceWindowsType.ColonyManagement:
                {
                    bool canShip = player.CanAccessStarshipInventory;
                    bool forbid = player.ColoniesState.ForbidColonization;
                    return canShip && !forbid;
                }
                case ServiceWindowsType.CargoManagement:
                {
                    bool blocked = player.ServiceWindowsBlocked;
                    return !blocked;
                }
                case ServiceWindowsType.Augmentations:
                {
                    bool augBlocked = player.AugmentationsWindowBlocked;
                    return StoreManager.CheckIfDlcPurchasedAndInstalled(DlcNameEnum.DLC3TheInfiniteMuseion) && !augBlocked;
                }
                default:
                    return true;
            }
        }

        // ---- Action bar region (abilities / weapon attacks / consumables the selected unit can use) ----

        // All action-bar slots in a stable order — current weapon set, abilities, consumables, heroic acts,
        // desperate measures, overdrive. RT has no unified slot list (the bar is split into part VMs); this
        // mirrors the set the game itself refreshes in SurfaceActionBarVM.UpdateSlotsCommandHandler.
        private static IEnumerable<ActionBarSlotVM> BarSlots()
        {
            var bar = ActionBar();
            if (bar == null) yield break;
            var set = bar.Weapons?.CurrentSet?.Value;
            if (set?.AllSlots != null) foreach (var s in set.AllSlots) yield return s;
            if (bar.Abilities?.Slots != null) foreach (var s in bar.Abilities.Slots) yield return s;
            if (bar.Consumables?.Slots != null) foreach (var s in bar.Consumables.Slots) yield return s;
            var momentum = bar.SurfaceMomentumVM;
            if (momentum?.HeroicActSlots != null) foreach (var s in momentum.HeroicActSlots) yield return s;
            if (momentum?.DesperateMeasureSlots != null) foreach (var s in momentum.DesperateMeasureSlots) yield return s;
            var overdrive = bar.Abilities?.OverdriveSlotVM;
            if (overdrive != null) yield return overdrive;
        }

        // A real, usable slot: not a non-current-weapon-set skeleton (IsFake), not empty, backed by a
        // supported mechanic (not IsBad).
        private static bool Usable(ActionBarSlotVM s)
            => s != null && !s.IsFake.Value && !s.IsEmpty.Value
               && s.MechanicActionBarSlot != null && !s.MechanicActionBarSlot.IsBad();

        // Rebuild the action list in place, restoring focus to the same row index if focus was inside (so a
        // character swap / item use under you doesn't strand focus). Focus elsewhere is untouched.
        private void RebuildActions()
        {
            if (_actions == null) return;
            var cur = Navigation.Active?.Current;
            int focusedIndex = (cur != null && cur.Parent == _actions) ? _actions.IndexOf(cur) : -1;

            _actions.Clear();
            // The augmentation-overdrive slot carries themed hover/click sounds in the game; tag it so the
            // proxy replays them (every other slot uses the generic hover + its own click).
            var overdrive = ActionBar()?.Abilities?.OverdriveSlotVM;
            foreach (var s in BarSlots()) if (Usable(s)) _actions.Add(new ProxyActionBarSlot(s, isOverdrive: ReferenceEquals(s, overdrive)));

            if (focusedIndex < 0 || _actions.Children.Count == 0) return;
            var target = _actions.Children[Math.Min(focusedIndex, _actions.Children.Count - 1)];
            Navigation.Focus(target, announce: true);
        }

        // The bar's content fingerprint: the selected unit + the usable slots' titles. Changes when the unit is
        // swapped, abilities/items appear, or an item is consumed — exactly when we must rebuild. Live per-slot
        // state (AP/cooldown/charges) is read by the proxy, so it isn't in here.
        private static string ActionsSig()
        {
            var sb = new StringBuilder();
            sb.Append(SelectedUnit()?.UniqueId).Append('|');
            foreach (var s in BarSlots())
                if (Usable(s)) { try { sb.Append(s.MechanicActionBarSlot.GetTitle()); } catch { } sb.Append(','); }
            return sb.ToString();
        }

        // ---- Menu region (the compass-corner control cluster) ----

        // RT keeps no menu VM for these controls (in RT the IngameMenuVM is only window-openers) — each is a
        // direct game API. One Tab-stop list, built once; toggles speak their new state on press and read their
        // live state into the label on the next focus. (No five-foot step / delay-turn / surface Rest in RT; the
        // turn-based and speed-up toggles are omitted — no clean stateful API.)
        private void BuildMenu()
        {
            var menu = new ListContainer(GameText.Or(() => UIStrings.Instance.CommonTexts.Menu, "hud.menu"));
            menu.Add(new ProxyActionButton(() => GameText.Or(() => UIStrings.Instance.CommonTexts.Pause, "hudmenu.pause") + ", " + OnOff(Game.Instance != null && Game.Instance.IsPaused),
                () => true, TogglePause, actionVerb: "toggle"));
            menu.Add(new ProxyActionButton(() => GameText.Or(() => UISettingsRoot.Instance.UIKeybindGeneralSettings.Hold.Description, "hudmenu.hold"),
                () => true, () => SelectionManagerBase.Instance?.Hold()));
            menu.Add(new ProxyActionButton(() => GameText.Or(() => UIStrings.Instance.ActionTexts.Stop, "hudmenu.stop"),
                () => true, () => SelectionManagerBase.Instance?.Stop()));
            menu.Add(new ProxyActionButton(() => GameText.Or(() => UISettingsRoot.Instance.UIKeybindSelectCharacterSettings.SelectAll.Description, "hudmenu.select_all"),
                () => true, () => SelectionManagerBase.Instance?.SelectAll()));
            menu.Add(new ProxyActionButton(() => GameText.Or(() => UIStrings.Instance.FormationTexts.FormationLabel, "hudmenu.formation") + (IngameMenu()?.IsFormationActive?.Value == true ? ", " + Loc.T("combat.active_marker") : ""),
                () => true, () => IngameMenu()?.OpenFormation(), actionVerb: "open",
                hoverSoundType: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound,     // Plastick in IngameMenuNewPCView
                clickSoundType: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.PlastickSound));
            menu.Add(new ProxyActionButton(() => GameText.Or(() => UIStrings.Instance.MainMenu.Inspect, "hudmenu.inspect") + ", " + OnOff(Game.Instance?.Player?.UISettings?.ShowInspect ?? false),
                () => true, ToggleInspect, actionVerb: "toggle"));
            menu.Add(new ProxyActionButton(() => GameText.Or(() => UISettingsRoot.Instance.UIKeybindGeneralSettings.CameraRotateToPointNorth.Description, "hudmenu.reset_camera"),
                () => true, () => Kingmaker.View.CameraRig.Instance?.ResetCameraRotate()));
            Add(menu);
        }

        private static string OnOff(bool on) => Loc.T(on ? "value.on" : "value.off");

        private static void TogglePause()
        {
            var g = Game.Instance;
            if (g == null) return;
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

        // ---- Party region ----

        // Rebuild the roster in place, restoring focus to the same member's row if focus was inside (so a
        // member joining/leaving under you doesn't strand focus). Focus elsewhere is untouched.
        private void RebuildParty()
        {
            if (_party == null) return;
            var cur = Navigation.Active?.Current;
            var focusedUnit = (cur != null && cur.Parent == _party) ? (cur as PartyEntry)?.Unit : null;

            _party.Clear();
            foreach (var u in Controllable()) _party.Add(new PartyEntry(u));

            if (focusedUnit == null) return;
            UIElement target = null;
            foreach (var c in _party.Children)
                if (c is PartyEntry e && e.Unit == focusedUnit) { target = c; break; }
            if (target != null) Navigation.Focus(target, announce: false); // same member: its live label already reads
            else { var f = _party.FirstFocusable(); if (f != null) Navigation.Focus(f, announce: true); }
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

        private static string PartySig()
        {
            var sb = new StringBuilder();
            foreach (var u in Controllable()) sb.Append(u.UniqueId).Append('|');
            return sb.ToString();
        }

        // One roster row — live label (name + wounds + selected marker); Enter selects the member.
        private sealed class PartyEntry : TextElement
        {
            public readonly BaseUnitEntity Unit;
            public PartyEntry(BaseUnitEntity unit) : base(() => PartyLabel(unit)) { Unit = unit; }

            // Selecting flips the unit's IsSelected VM reactive, which the live PartyCharacterPCView already
            // answers with Character.CharacterSelect — so suppress our generic click to avoid doubling it.
            public override Kingmaker.UI.Sound.BlueprintUISound.UISound ActivateSound => null;

            public override IEnumerable<ElementAction> GetActions()
            {
                yield return new ElementAction(ActionIds.Activate,
                    Message.Localized("ui", "action.select"), _ => Select(Unit));
            }
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

        // ---- Status region ----

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

        // ---- Combat region ----

        // Rebuild the turn panel only when membership changes (combat toggling, units joining/leaving/dying).
        // Status, button state and the active marker are all live, so the turn advancing needs no rebuild.
        private void RebuildCombat()
        {
            if (_combat == null) return;
            var cur = Navigation.Active?.Current;
            int focusedIndex = (cur != null && cur.Parent == _combat) ? _combat.IndexOf(cur) : -1;

            _combat.Clear();
            var game = Game.Instance;
            if (game != null && game.TurnController.TurnBasedModeActive)
            {
                _combat.Add(new TextElement(StatusLine)); // the combat-aware status line, focusable on its own
                // TryEndPlayerTurnManually plays Combat.EndTurn itself (TurnController), so suppress our
                // generic click to avoid stacking a ButtonClick on top of the real end-turn sting.
                _combat.Add(new ProxyActionButton(() => GameText.Or(() => UIStrings.Instance.HUDTexts.EndTurn, "turn.end"),
                    () => game.TurnController.CanEndTurn,
                    () => game.TurnController.TryEndPlayerTurnManually(), actionVerb: "activate",
                    suppressActivateSound: true));

                var tracker = SurfaceHUD()?.InitiativeTrackerVM?.Value;
                if (tracker?.Units != null)
                    foreach (var u in tracker.Units)
                        if (u != null) { var vm = u; _combat.Add(new TextElement(() => InitiativeLabel(vm))); }
            }

            if (focusedIndex < 0 || _combat.Children.Count == 0) return;
            var target = _combat.Children[Math.Min(focusedIndex, _combat.Children.Count - 1)];
            Navigation.Focus(target, announce: true);
        }

        private static string CombatSig()
        {
            var game = Game.Instance;
            if (game == null || !game.TurnController.TurnBasedModeActive) return "off";
            var sb = new StringBuilder("tb|");
            var tracker = SurfaceHUD()?.InitiativeTrackerVM?.Value;
            if (tracker?.Units != null)
                foreach (var u in tracker.Units)
                    if (u != null) sb.Append(u.Unit?.UniqueId).Append('|');
            return sb.ToString();
        }

        private static string InitiativeLabel(InitiativeTrackerUnitVM vm)
        {
            try
            {
                if (vm?.Unit == null) return "";
                var sb = new StringBuilder(NameOf(vm.Unit));
                if (Game.Instance?.TurnController?.CurrentUnit == vm.Unit) sb.Append(", ").Append(Loc.T("combat.current"));
                return sb.ToString();
            }
            catch (Exception e) { Main.Log?.Error("InGameScreen.InitiativeLabel: " + e); return ""; }
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
