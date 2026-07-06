using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;                                     // Game (VirtualPositionController, TurnController.VeilThicknessCounter)
using Kingmaker.Blueprints;                          // BlueprintExtenstions.GetComponent<T> (blueprint components)
using Kingmaker.Blueprints.Root;                     // BlueprintRoot (WarhammerRoot.PsychicPhenomenaRoot)
using Kingmaker.Blueprints.Root.Strings;             // UIStrings (Tooltips.EndsTurn / SpendAllMovementPoints)
using Kingmaker.Code.UI.MVVM.VM.ActionBar;           // ActionBarSlotVM
using Kingmaker.UI.Common;                           // UIUtility.GetCurrentSelectedUnit
using Kingmaker.UnitLogic.Abilities;                 // AbilityData
using Kingmaker.UnitLogic.Abilities.Blueprints;      // AbilityTargetAnchor
using Kingmaker.UnitLogic.Abilities.Components;      // WarhammerEndTurn, CheckBuffForMPSpendTooltip
using RTAccess.UI.Graph;
using UnityEngine;                                   // Vector3, Mathf

namespace RTAccess.UI
{
    /// <summary>
    /// Graph factory for action-bar slots — an ability / weapon attack / consumable / heroic act the
    /// selected character can use — read live off the game's <see cref="ActionBarSlotVM"/>:
    ///   • label   = the mechanic slot's title (<c>MechanicActionBarSlot.GetTitle()</c>) — LIVE, so the
    ///     bar repopulating under focus (a character swap) re-reads the new occupant of the row;
    ///   • value   = AP cost, range/target-kind/uses, ammo, cooldown, veil, "ends turn" — read on focus —
    ///     plus a LIVE targeting/active part, so arming or the game's async settle announces under focus;
    ///   • enabled = whether it's usable right now (<c>IsPossibleActive</c> — AP/cooldown/turn gates), LIVE;
    ///   • activate = the VM's own click (<see cref="ActionBarSlotVM.OnMainClick"/>, which fires the ability
    ///     AND plays the game's slot-click sound — so no ActivateSound of ours on top). Enter on a greyed
    ///     slot speaks the game's own why-not instead of silence (the blocked-click fallback).
    /// Space reads the full ability/item description via the slot's rich brick tooltip.
    /// Carries the whole ProxyActionBarSlot contract (the retired adapter-era widget).
    /// </summary>
    internal static class ActionBarNodes
    {
        /// <summary>One action-bar slot. <paramref name="isOverdrive"/> tags the augmentation-overdrive
        /// slot, the only one with a themed hover/click in the game (SurfaceActionBarPartAbilitiesPCView
        /// SetHoverSound/SetClickSound); its themed click LAYERS on OnMainClick's own mechanic sound,
        /// matching the game.</summary>
        public static NodeVtable Slot(ActionBarSlotVM vm, bool isOverdrive = false)
        {
            Func<bool> enabled = () => vm?.IsPossibleActive?.Value ?? false;
            Func<string> title = () => Title(vm);
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement>
                {
                    new NodeAnnouncement(title, live: true, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Detail(vm), kind: AnnouncementKinds.Value),
                    // Targeting / active state — LIVE, so arming an ability (or the game's async settle)
                    // announces itself while the slot is focused. Silent when neither.
                    new NodeAnnouncement(() => ToggleState(vm), live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(enabled),
                },
                SearchText = title,
                OnActivate = () =>
                {
                    if (enabled()) { vm?.OnMainClick(); return; }
                    // The game only raises a warning for some refusals; give the greyed slot's own
                    // reason (or a plain "disabled") so Enter always says something.
                    var why = UnavailableReason(vm?.AbilityData);
                    Tts.Speak(why != null
                        ? Loc.T("slot.unavailable", new { reason = why })
                        : Loc.T("state.disabled"), interrupt: true);
                },
                OnTooltip = () => TooltipChooser.OpenTemplate(Title(vm), vm?.Tooltip?.Value),
                HoverSound = isOverdrive ? Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.AugmentationsOverdriveHover
                    : (Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum?)null,
                ClickSound = isOverdrive ? Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.AugmentationsOverdriveClick
                    : (Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum?)null,
                ActivateSound = null, // OnMainClick plays the slot's own click; the refusal path speaks
            };
        }

        private static string Title(ActionBarSlotVM vm)
        {
            try { return vm?.MechanicActionBarSlot?.GetTitle() ?? ""; }
            catch { return ""; }
        }

        // The live targeting/active marker (the old State()'s only volatile piece, split out as the live
        // part): "targeting" while the slot is armed awaiting a target, the active marker for a running
        // toggle, null otherwise.
        private static string ToggleState(ActionBarSlotVM vm)
        {
            try
            {
                if (vm == null) return null;
                if (vm.IsSelected.Value) return Loc.T("value.targeting");
                var m = vm.MechanicActionBarSlot;
                return m != null && m.IsActive() ? Loc.T("combat.active_marker") : null;
            }
            catch { return null; }
        }

        // AP cost + range/target-kind/uses + ammo + cooldown + veil + "ends turn" + why-disabled. The
        // reactive mirrors are the VM's; the range/target-kind/uses/reason come off the AbilityData — the
        // same decision info a sighted player reads off the slot icon and its greyed-out tooltip. Read on
        // focus (user-driven), so the rule-triggering getters (RangeCells, GetUnavailableReason) are fine
        // to call here. (Targeting/active state lives in the LIVE part above, not here.)
        private static string Detail(ActionBarSlotVM vm)
        {
            if (vm == null) return null;
            var sb = new StringBuilder();
            try
            {
                int ap = vm.ActionPointCost.Value;
                if (ap > 0) Append(sb, Loc.T(ap == 1 ? "slot.action_point" : "slot.action_points", new { ap }));

                var ab = vm.AbilityData;
                if (ab != null) AppendAbilityDetails(sb, vm, ab);

                if (vm.IsReload.Value)
                    Append(sb, Loc.T("slot.ammo", new { current = vm.CurrentAmmo.Value, max = vm.MaxAmmo.Value }));
                // Ammo spent per activation (VM's AmmoCost — a separate badge from the current/max ammo,
                // shown when the ability consumes more than one round per use, e.g. a burst).
                if (vm.AmmoCost.Value > 0)
                    Append(sb, Loc.T("slot.ammo_cost", new { cost = vm.AmmoCost.Value }));
                if (vm.IsOnCooldown.Value)
                {
                    var cd = vm.CooldownText.Value;
                    Append(sb, string.IsNullOrEmpty(cd) ? Loc.T("slot.on_cooldown") : Loc.T("slot.cooldown", new { turns = cd }));
                }

                if (ab != null)
                {
                    AppendVeil(sb, ab);      // predicted after-cast veil (psyker powers)
                    AppendEndTurn(sb, ab);   // "ends turn" / "spends all movement" cue
                }

                // Why it's greyed out — the game's own reason (not enough AP, on cooldown, out of range, …),
                // so a disabled slot says the cause instead of a bare "disabled".
                if (ab != null && !(vm.IsPossibleActive?.Value ?? false))
                {
                    var why = UnavailableReason(ab);
                    if (why != null) Append(sb, Loc.T("slot.unavailable", new { reason = why }));
                }
            }
            catch { }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        // Range (melee vs cells, + a minimum if the weapon has one), what it targets, and limited uses.
        private static void AppendAbilityDetails(StringBuilder sb, ActionBarSlotVM vm, AbilityData ab)
        {
            try
            {
                var anchor = ab.TargetAnchor;

                // Range is meaningless for a self/owner ability; melee reads better as "melee" than "range 1 cell".
                if (anchor != AbilityTargetAnchor.Owner)
                {
                    if (ab.IsMelee) Append(sb, Loc.T("slot.melee"));
                    else
                    {
                        int r = 0; try { r = ab.RangeCells; } catch { }
                        if (r > 1) Append(sb, Loc.T("slot.range", new { cells = r }));
                        // Effective (half) range ring — a weapon ability's sweet-spot ends at half its max range
                        // (AbilitySingleTargetRange: weapon != null ? FloorToInt(MaxRangeCells / 2) : 0); the sighted
                        // view draws it as an inner ring. Only for weapon abilities, only when it's a distinct band.
                        if (ab.Weapon != null && r > 0)
                        {
                            int eff = Mathf.FloorToInt(r / 2f);
                            if (eff > 0 && eff < r) Append(sb, Loc.T("slot.effective_range", new { cells = eff }));
                        }
                        int min = 0; try { min = ab.MinRangeCells; } catch { }
                        if (min > 0) Append(sb, Loc.T("slot.min_range", new { cells = min }));
                    }
                }

                // What activating it will ask for.
                switch (anchor)
                {
                    case AbilityTargetAnchor.Owner: Append(sb, Loc.T("slot.self")); break;
                    case AbilityTargetAnchor.Unit: Append(sb, Loc.T("slot.targets_unit")); break;
                    case AbilityTargetAnchor.Point: Append(sb, ab.IsAOE ? Loc.T("slot.area_effect") : Loc.T("slot.targets_point")); break;
                }

                // Limited uses (charges / per-day resource); -1 == at-will. Ammo weapons already read their
                // ammo above, so don't also say "N uses left" for them.
                if (!vm.IsReload.Value)
                {
                    int uses = -1; try { uses = ab.GetAvailableForCastCount(); } catch { }
                    if (uses >= 0) Append(sb, Loc.T(uses == 1 ? "slot.use_left" : "slot.uses_left", new { uses }));
                }
            }
            catch { }
        }

        // The game's localized "why greyed out" text, evaluated from where the caster will act (its desired
        // position, matching the on-screen tooltip). Null when there's no reason or no caster.
        private static string UnavailableReason(AbilityData ab)
        {
            try
            {
                var caster = ab?.Caster;
                if (caster == null) return null;
                Vector3 pos = caster.Position;
                try { var vpc = Game.Instance?.VirtualPositionController; if (vpc != null) pos = vpc.GetDesiredPosition(caster); }
                catch { }
                var reason = ab.GetUnavailableReason(pos);
                return string.IsNullOrWhiteSpace(reason) ? null : TextUtil.StripRichText(reason);
            }
            catch { return null; }
        }

        // "Ends turn" / "spends all movement" — the same cue the sighted tooltip shows for an ability whose
        // blueprint carries WarhammerEndTurn (SurfaceActionBarVM.GetEndTurn / TooltipTemplateAbility). The strings
        // are game-localized content (UIStrings.Tooltips.*), passed straight through. Honors CheckBuffForMPSpendTooltip:
        // that component overrides the plain text, showing the MP cue only when the selected caster holds its buff
        // (and suppressing it otherwise) — matching TooltipTemplateAbility.GetEndTurn exactly.
        private static void AppendEndTurn(StringBuilder sb, AbilityData ab)
        {
            try
            {
                var bp = ab.Blueprint;
                if (bp == null) return;
                var check = bp.GetComponent<CheckBuffForMPSpendTooltip>();
                if (check != null)
                {
                    var unit = UIUtility.GetCurrentSelectedUnit();
                    if (unit != null)
                    {
                        // The buff-check component wins when a caster is known: MP cue iff the buff is present.
                        if (check.CheckContainsBuff(unit))
                            Append(sb, (string)UIStrings.Instance.Tooltips.SpendAllMovementPoints);
                        return;
                    }
                }
                var endTurn = bp.GetComponent<WarhammerEndTurn>();
                if (endTurn != null)
                    Append(sb, (string)(endTurn.clearMPInsteadOfEndingTurn
                        ? UIStrings.Instance.Tooltips.SpendAllMovementPoints
                        : UIStrings.Instance.Tooltips.EndsTurn));
            }
            catch { }
        }

        // Predicted veil thickness after casting a psyker power — the sighted VeilThicknessVM.PredictedValue
        // (current global veil + AbilityData.GetVeilThicknessPointsToAdd(isPrediction:true)). Veil is a global
        // location value (AreaVailPart.Vail), not per-unit, so there's nothing to fog-gate. Flags when the cast
        // would reach the critical threshold the game uses to escalate perils (CriticalVeilOnAllLocation).
        private static void AppendVeil(StringBuilder sb, AbilityData ab)
        {
            try
            {
                if (ab.Blueprint == null || !ab.Blueprint.IsPsykerAbility) return;
                int current = Game.Instance?.TurnController?.VeilThicknessCounter?.Value ?? 0;
                int predicted = current + ab.GetVeilThicknessPointsToAdd(isPrediction: true);
                Append(sb, Loc.T("slot.veil_after", new { value = predicted }));
                int critical = BlueprintRoot.Instance.WarhammerRoot.PsychicPhenomenaRoot.CriticalVeilOnAllLocation;
                if (predicted >= critical) Append(sb, Loc.T("slot.veil_would_go_critical"));
            }
            catch { }
        }

        private static void Append(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(s);
        }
    }
}
