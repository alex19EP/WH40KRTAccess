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
            _actions = new ListContainer("Actions");
            Add(_actions);
            _party = new ListContainer("Party");
            Add(_party);
            _combat = new ListContainer("Combat");
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
        };

        private void BuildWindows()
        {
            var windows = new ListContainer("Windows");
            foreach (var t in WindowButtons)
            {
                var type = t; // capture for the live closure
                windows.Add(new ProxyActionButton(WindowLabel(type), () => true,
                    () => ServiceWindows()?.HandleOpenWindowOfType(type), actionVerb: "open"));
            }
            Add(windows);
        }

        private static string WindowLabel(ServiceWindowsType type)
        {
            switch (type)
            {
                case ServiceWindowsType.Inventory: return "Inventory";
                case ServiceWindowsType.CharacterInfo: return "Character";
                case ServiceWindowsType.Journal: return "Journal";
                case ServiceWindowsType.LocalMap: return "Map";
                case ServiceWindowsType.Encyclopedia: return "Encyclopedia";
                default: return type.ToString();
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
            foreach (var s in BarSlots()) if (Usable(s)) _actions.Add(new ProxyActionBarSlot(s));

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
            var menu = new ListContainer("Menu");
            menu.Add(new ProxyActionButton(() => "Pause, " + OnOff(Game.Instance != null && Game.Instance.IsPaused),
                () => true, TogglePause, actionVerb: "toggle"));
            menu.Add(new ProxyActionButton("Hold position", () => true, () => SelectionManagerBase.Instance?.Hold()));
            menu.Add(new ProxyActionButton("Stop", () => true, () => SelectionManagerBase.Instance?.Stop()));
            menu.Add(new ProxyActionButton("Select whole party", () => true, () => SelectionManagerBase.Instance?.SelectAll()));
            menu.Add(new ProxyActionButton(() => "Formation" + (IngameMenu()?.IsFormationActive?.Value == true ? ", open" : ""),
                () => true, () => IngameMenu()?.OpenFormation(), actionVerb: "open"));
            menu.Add(new ProxyActionButton(() => "Inspect mode, " + OnOff(Game.Instance?.Player?.UISettings?.ShowInspect ?? false),
                () => true, ToggleInspect, actionVerb: "toggle"));
            menu.Add(new ProxyActionButton("Reset camera", () => true,
                () => Kingmaker.View.CameraRig.Instance?.ResetCameraRotate()));
            Add(menu);
        }

        private static string OnOff(bool on) => on ? "on" : "off";

        private static void TogglePause()
        {
            var g = Game.Instance;
            if (g == null) return;
            // The pause-mode change settles a frame later, so reading g.IsPaused right after the set is stale —
            // announce the state we just asked for, not the not-yet-updated getter.
            bool willPause = !g.IsPaused;
            g.IsPaused = willPause;
            Tts.Speak(willPause ? "Paused" : "Unpaused", interrupt: true);
        }

        private static void ToggleInspect()
        {
            var ui = Game.Instance?.Player?.UISettings;
            if (ui == null) return;
            bool on = !ui.ShowInspect;
            ui.ShowInspect = on;
            Tts.Speak(on ? "Inspect mode on" : "Inspect mode off", interrupt: true);
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
                if (Game.Instance?.SelectionCharacter?.SelectedUnit?.Value == unit) sb.Append(", selected");
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
                // Key-driven selection — interrupt so stepping the roster stays responsive.
                Tts.Speak(unit.CharacterName, interrupt: true);
            }
            catch (Exception e) { Main.Log?.Error("InGameScreen.Select failed: " + e); }
        }

        // ---- Status region ----

        private static string StatusLine()
        {
            try
            {
                var game = Game.Instance;
                if (game == null) return "";
                var unit = SelectedUnit();
                if (unit == null) return "No character selected.";

                var sb = new StringBuilder(unit.CharacterName);
                AppendWounds(sb, unit);

                if (game.TurnController.TurnBasedModeActive)
                {
                    var cs = unit.GetCombatStateOptional();
                    if (cs != null)
                        sb.Append(", ").Append(cs.ActionPointsYellow).Append(" AP, ")
                          .Append(Mathf.RoundToInt(cs.ActionPointsBlue)).Append(" MP");
                    var turnUnit = game.TurnController.CurrentUnit;
                    if (turnUnit != null)
                    {
                        sb.Append(", ").Append(NameOf(turnUnit)).Append("'s turn");
                        if (!game.TurnController.IsPlayerTurn) sb.Append(" (enemy)");
                    }
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
                _combat.Add(new ProxyActionButton("End turn",
                    () => game.TurnController.CanEndTurn,
                    () => game.TurnController.TryEndPlayerTurnManually(), actionVerb: "activate"));

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
                if (Game.Instance?.TurnController?.CurrentUnit == vm.Unit) sb.Append(", current");
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
            sb.Append(", ").Append(h.HitPointsLeft).Append(" of ").Append(h.MaxHitPoints).Append(" wounds");
            if (h.TemporaryHitPoints > 0) sb.Append(", ").Append(h.TemporaryHitPoints).Append(" temporary");
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
