using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;                                     // Game (VirtualPositionController, TurnController.VeilThicknessCounter)
using Kingmaker.Blueprints;                          // BlueprintExtenstions.GetComponent<T> (blueprint components)
using Kingmaker.Blueprints.Root;                     // BlueprintRoot (WarhammerRoot.PsychicPhenomenaRoot)
using Kingmaker.Blueprints.Root.Strings;             // UIStrings (Tooltips.EndsTurn / SpendAllMovementPoints)
using Kingmaker.Code.UI.MVVM.VM.ActionBar;           // ActionBarSlotVM
using Kingmaker.UI.Common;                           // UIUtility.GetCurrentSelectedUnit
using Kingmaker.UI.Models.UnitSettings;              // MechanicActionBarSlotItem (quick-slot stack count)
using Kingmaker.UnitLogic.Abilities;                 // AbilityData
using Kingmaker.UnitLogic.Abilities.Blueprints;      // AbilityTargetAnchor
using Kingmaker.UnitLogic.Abilities.Components;      // WarhammerEndTurn, CheckBuffForMPSpendTooltip
using Kingmaker.UnitLogic.FactLogic;                 // WarhammerCooldown (UntilEndOfCombat — the "once per battle" cue)
using Kingmaker.Controllers.Enums;                   // PsychicPower (Minor/Major veil degradation)
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
    ///   • enabled = the "unavailable" status word, spoken FIRST (the ActionSlot type leads with the
    ///     Enabled kind), LIVE — with the game's own why-not reason ordered right after the name (Reason
    ///     kind): "unavailable, {name}, out of range, …", so a player scanning a bar of mostly-greyed
    ///     abilities hears whether a slot is usable, and why not, before its detail;
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
        /// matching the game. <paramref name="bindName"/> is the game's own direct-activation keybinding
        /// name for this slot's bar position (ActionBarAbility/Consumable/WeaponButtonNN — null for the
        /// momentum/overdrive slots, which have none), spoken so the fast bare-key channel is discoverable.</summary>
        public static NodeVtable Slot(ActionBarSlotVM vm, bool isOverdrive = false, string bindName = null)
        {
            Func<bool> enabled = () => vm?.IsPossibleActive?.Value ?? false;
            Func<string> title = () => Title(vm);
            return new NodeVtable
            {
                // ActionSlot (not plain Button): its speak order is "unavailable, <name>, <reason>, …",
                // so an unusable ability announces its status first and the reason right after the name —
                // skippable without waiting out its full detail.
                ControlType = ControlTypes.ActionSlot,
                Announcements = new List<NodeAnnouncement>
                {
                    // The status word LEADS (Enabled kind, first). Silent when usable, "unavailable" when
                    // not. LIVE, but reads only the cheap IsPossibleActive — so an ability going un/available
                    // under focus (AP spent, cooldown ticked) re-speaks it, without the per-frame cost of the
                    // rule-triggering reason lookup (that stays on the non-live Reason part below).
                    new NodeAnnouncement(() => UnavailableMarker(vm), live: true, kind: AnnouncementKinds.Enabled),
                    new NodeAnnouncement(title, live: true, kind: AnnouncementKinds.Label),
                    // The why-not reason, ordered right after the name (ActionSlot's Reason kind). Silent
                    // when usable; the game's own greyed-tooltip reason otherwise. Read on focus (not live)
                    // — the reason getter triggers rules, so it must not run every frame.
                    new NodeAnnouncement(() => UnavailableReasonText(vm), kind: AnnouncementKinds.Reason),
                    new NodeAnnouncement(() => Detail(vm), kind: AnnouncementKinds.Value),
                    // The game's own hotkey for this slot (main-HUD audit #10) — resolved exactly as the
                    // sighted label is (ActionBarSlotPCView.SetKeyBindLabel), so rebinds read correctly.
                    new NodeAnnouncement(() => HotkeyPart(bindName), kind: AnnouncementKinds.Value),
                    // Targeting / active state — LIVE, so arming an ability (or the game's async settle)
                    // announces itself while the slot is focused. Silent when neither.
                    new NodeAnnouncement(() => ToggleState(vm), live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = title,
                OnActivate = () =>
                {
                    if (enabled())
                    {
                        // #3 (main-HUD audit): OnMainClick on a HasVariants ability TOGGLES a convert flyout
                        // instead of casting (OnShowConvertRequest / HandleConvertRequest's else-branch) —
                        // silent on screen-reader without this. Announce the open (with the choice count; the
                        // rows render right after this slot) and the close, so Enter never reads as a no-op.
                        bool wasOpen = vm?.ConvertedVm?.Value != null;
                        vm?.OnMainClick();
                        var conv = vm?.ConvertedVm?.Value;
                        if (conv != null && !wasOpen)
                            Tts.Speak(Loc.T("slot.variants_open", new { count = conv.Slots.Count }), interrupt: true);
                        else if (conv == null && wasOpen)
                            Tts.Speak(Loc.T("slot.variants_closed"), interrupt: true);
                        return;
                    }
                    // The game only raises a warning for some refusals; give the greyed slot's own
                    // reason (or a plain "disabled") so Enter always says something.
                    var why = UnavailableReason(AbilityOf(vm));
                    Tts.Speak(why != null
                        ? Loc.T("slot.unavailable", new { reason = why })
                        : Loc.T("state.disabled"), interrupt: true);
                },
                // Backspace — the sighted convert-ARROW's keyboard twin (same OnShowConvertRequest, same
                // HasConvert && IsCanConvert visibility): toggles the variant list on ANY slot carrying
                // converts. This is the only keyboard route for an ITEM's converts ("use on another
                // character"), whose Enter uses the item directly instead of opening the list. Declared
                // fresh per render, so the hook exists exactly while the arrow would be visible.
                OnSecondary = (vm?.HasConvert?.Value ?? false) && (vm?.IsCanConvert?.Value ?? false)
                    ? (Action)(() => ToggleConvert(vm))
                    : null,
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

        // Toggle the slot's convert/variant flyout through the game's own request (the exact call the
        // sighted arrow's click makes) and announce the result — the rows render right after the parent.
        private static void ToggleConvert(ActionBarSlotVM vm)
        {
            bool wasOpen = vm.ConvertedVm?.Value != null;
            vm.OnShowConvertRequest();
            var conv = vm.ConvertedVm?.Value;
            if (conv != null && !wasOpen)
                Tts.Speak(Loc.T("slot.variants_open", new { count = conv.Slots.Count }), interrupt: true);
            else if (conv == null && wasOpen)
                Tts.Speak(Loc.T("slot.variants_closed"), interrupt: true);
        }

        // The slot's ability for the detail readout. The VM's own AbilityData getter covers ability and
        // item slots but NOT the convert-flyout rows (MechanicActionBarSlotSpontaneusConvertedSpell is not
        // a MechanicActionBarSlotAbility — its ability lives in the public Spell field), which left variant
        // rows with no range/AP/why-disabled detail at all (review finding).
        private static AbilityData AbilityOf(ActionBarSlotVM vm)
        {
            if (vm == null) return null;
            return vm.AbilityData
                ?? (vm.MechanicActionBarSlot as MechanicActionBarSlotSpontaneusConvertedSpell)?.Spell;
        }

        // The spoken form of the slot's game hotkey — the exact resolution the sighted corner label uses
        // (UIKeyboardTexts.GetStringByBinding over the live binding), so a player rebind reads correctly.
        // Null (silent) when the slot has no binding name or the binding resolves to no key.
        private static string HotkeyPart(string bindName)
        {
            if (string.IsNullOrEmpty(bindName)) return null;
            try
            {
                var label = UIKeyboardTexts.Instance.GetStringByBinding(Game.Instance.Keyboard.GetBindingByName(bindName));
                return string.IsNullOrWhiteSpace(label) ? null : Loc.T("slot.hotkey", new { key = label });
            }
            catch { return null; }
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

                var ab = AbilityOf(vm);
                if (ab != null) AppendAbilityDetails(sb, vm, ab);
                // An item slot with no AbilityData never reaches AppendAbilityDetails — its counter still shows.
                else if (vm.MechanicActionBarSlot is MechanicActionBarSlotItem && !vm.IsReload.Value)
                    AppendItemCount(sb, vm);

                if (vm.IsReload.Value)
                    Append(sb, Loc.T("slot.ammo", new { current = vm.CurrentAmmo.Value, max = vm.MaxAmmo.Value }));
                // Ammo spent per activation (VM's AmmoCost — a separate badge from the current/max ammo,
                // shown when the ability consumes more than one round per use, e.g. a burst).
                if (vm.AmmoCost.Value > 0)
                    Append(sb, Loc.T("slot.ammo_cost", new { cost = vm.AmmoCost.Value }));

                if (ab != null)
                {
                    AppendCooldown(sb, ab);  // base cooldown LENGTH (tooltip-faithful; live on-cooldown state rides the availability reason)
                    AppendVeil(sb, ab);      // veil points added + minor/major degradation (tooltip-faithful)
                    AppendEndTurn(sb, ab);   // "ends turn" / "spends all movement" cue
                }

                // #3 the convert/variant cue — the sighted arrow's visibility (HasConvert && IsCanConvert).
                // Every such slot is now reachable: Enter auto-opens the list on an ability slot, Backspace
                // (the secondary action) opens it on any slot — including an ITEM's "use on another
                // character" converts, whose Enter uses the item directly.
                if ((vm.HasConvert?.Value ?? false) && (vm.IsCanConvert?.Value ?? false))
                    Append(sb, Loc.T("slot.has_variants"));

                // Why it's greyed out no longer trails here — it now LEADS the readout as the availability
                // part (Availability(), Enabled kind). Kept out of Detail so the reason is spoken once.
            }
            catch { }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        // The leading "unavailable" status word (Enabled kind, spoken FIRST). Silent when the slot is
        // usable. Reads only the cheap IsPossibleActive so it is safe to watch live (the reason lookup,
        // which triggers rules, lives on the separate non-live part below).
        private static string UnavailableMarker(ActionBarSlotVM vm)
            => (vm?.IsPossibleActive?.Value ?? false) ? null : Loc.T("slot.unavailable_marker");

        // Why the slot is greyed out, ordered right after the name (Reason kind). Silent when usable or
        // when the game gives no reason; otherwise the game's own localized reason (not enough AP, on
        // cooldown, out of range, …), evaluated from where the caster will act — the text the sighted
        // greyed tooltip shows.
        private static string UnavailableReasonText(ActionBarSlotVM vm)
            => (vm?.IsPossibleActive?.Value ?? false) ? null : UnavailableReason(AbilityOf(vm));

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

                // Limited uses. Ammo weapons already read their ammo above, so don't also say "N uses left".
                if (!vm.IsReload.Value)
                {
                    if (vm.MechanicActionBarSlot is MechanicActionBarSlotItem) AppendItemCount(sb, vm);
                    else
                    {
                        // Ability charges / per-day resource; -1 == at-will.
                        int uses = -1; try { uses = ab.GetAvailableForCastCount(); } catch { }
                        if (uses >= 0) Append(sb, Loc.T(uses == 1 ? "slot.use_left" : "slot.uses_left", new { uses }));
                    }
                }
            }
            catch { }
        }

        // A quick-slot ITEM's sighted counter is GetResource() — the STACK count when stacked, else the
        // item's charges — mirrored live in the VM's ResourceCount (main-HUD audit #7). The old
        // GetAvailableForCastCount() read only Charges and misreported a stack of single-charge medikits as
        // "1 use left". Sentinels: -1 = uncounted item, 0 = the item left the quick slots; the sighted badge
        // hides when ≤ 0, so speak only a positive count.
        private static void AppendItemCount(StringBuilder sb, ActionBarSlotVM vm)
        {
            int count = vm.ResourceCount.Value;
            if (count > 0) Append(sb, Loc.T(count == 1 ? "slot.use_left" : "slot.uses_left", new { uses = count }));
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

        // The cooldown the ability tooltip prints (TooltipTemplateAbility.AddCooldown): the base cooldown
        // LENGTH — BlueprintAbility.CooldownRounds — or "once per battle" for an until-end-of-combat ability
        // (WarhammerCooldown.UntilEndOfCombat), the exact split the tooltip's Cooldown brick makes. This is
        // the static duration, NOT the live remaining countdown: whether it's on cooldown RIGHT NOW comes
        // through the leading availability reason (the game's greyed why-not), matching how a sighted player
        // reads it off the greyed slot rather than this brick.
        private static void AppendCooldown(StringBuilder sb, AbilityData ab)
        {
            try
            {
                var bp = ab?.Blueprint;
                if (bp == null) return;
                if (bp.GetComponent<WarhammerCooldown>()?.UntilEndOfCombat ?? false)
                { Append(sb, Loc.T("slot.cooldown_once")); return; }
                int rounds = bp.CooldownRounds;
                if (rounds > 0) Append(sb, Loc.T(rounds == 1 ? "slot.cooldown_round" : "slot.cooldown_rounds", new { rounds }));
            }
            catch { }
        }

        // The veil cost the ability tooltip shows for a psyker power: the points THIS cast adds (the number
        // in the tooltip's psychic cost line, BlueprintAbility.GetVeilThicknessPointsToAdd) plus the game's
        // own Minor/Major degradation label (TooltipTemplateAbility.GetVeil). We do NOT speak a predicted
        // after-cast total — the game never prints one (it's the veil meter's moving slider, not text). The
        // one thing we still flag is whether the cast would break the veil (perils escalate): faithful to
        // the sighted player watching that predicted slider cross the critical marker on hover — computed
        // from the global veil, never spoken as a raw number.
        private static void AppendVeil(StringBuilder sb, AbilityData ab)
        {
            try
            {
                var bp = ab?.Blueprint;
                if (bp == null || !bp.IsPsykerAbility) return;
                int points = bp.GetVeilThicknessPointsToAdd();
                if (points > 0) Append(sb, Loc.T("slot.veil_add", new { points }));
                Append(sb, (string)(bp.PsychicPower != PsychicPower.Major
                    ? UIStrings.Instance.Tooltips.MinorVeilDegradation
                    : UIStrings.Instance.Tooltips.MajorVeilDegradation));
                int current = Game.Instance?.TurnController?.VeilThicknessCounter?.Value ?? 0;
                int predicted = current + ab.GetVeilThicknessPointsToAdd(isPrediction: true);
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
