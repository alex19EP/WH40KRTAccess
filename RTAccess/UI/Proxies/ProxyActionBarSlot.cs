using System.Collections.Generic;
using System.Text;
using Kingmaker;                                     // Game (VirtualPositionController)
using Kingmaker.Code.UI.MVVM.VM.ActionBar;           // ActionBarSlotVM
using Kingmaker.UnitLogic.Abilities;                 // AbilityData
using Kingmaker.UnitLogic.Abilities.Blueprints;      // AbilityTargetAnchor
using RTAccess.UI.Announcements;
using UnityEngine;                                   // Vector3

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One action-bar slot — an ability / weapon attack / consumable / heroic act the selected character can
    /// use — as a focusable, activatable button, read live off the game's <see cref="ActionBarSlotVM"/>:
    ///   • label   = the mechanic slot's title (<c>MechanicActionBarSlot.GetTitle()</c>),
    ///   • value   = AP cost, ammo, cooldown, and targeting/active state (the reactive mirrors on the VM),
    ///   • enabled = whether it's usable right now (<c>IsPossibleActive</c> — AP/cooldown/turn gates),
    ///   • activate = the VM's own click (<see cref="ActionBarSlotVM.OnMainClick"/>, which fires the ability
    ///     AND plays the game's slot-click sound — so we suppress our own ActivateSound to avoid doubling it).
    /// Space reads the full ability/item description via the slot's rich brick tooltip
    /// (<c>slot.Tooltip.Value</c>, surfaced through <see cref="GetTooltipTemplate"/>).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyActionBarSlot : UIElement
    {
        private readonly ActionBarSlotVM _slot;

        public ProxyActionBarSlot(ActionBarSlotVM slot) { _slot = slot; }

        // OnMainClick plays the action-bar click sound itself; don't double it.
        public override Kingmaker.UI.Sound.BlueprintUISound.UISound ActivateSound => null;

        public override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate GetTooltipTemplate()
            => _slot?.Tooltip?.Value;

        private bool Enabled => _slot?.IsPossibleActive?.Value ?? false;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Title()));
            yield return new RoleAnnouncement("action");
            var state = State();
            if (!string.IsNullOrEmpty(state)) yield return new ValueAnnouncement(Message.Raw(state));
            yield return new EnabledAnnouncement(Enabled);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Even a not-currently-usable slot is worth activating (it surfaces the game's own "not enough
            // action points" warning via WarningReader) — but match the other proxies and only offer it when
            // usable, so a greyed slot reads "disabled" and Enter does nothing surprising.
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.activate"),
                    _ => _slot?.OnMainClick());
        }

        private string Title()
        {
            try { return _slot?.MechanicActionBarSlot?.GetTitle() ?? ""; }
            catch { return ""; }
        }

        // AP cost + range/target-kind/uses + ammo + cooldown + targeting/active + why-disabled. The reactive
        // mirrors are the VM's; the range/target-kind/uses/reason come off the AbilityData — the same decision
        // info a sighted player reads off the slot icon and its greyed-out tooltip. Read on focus (user-driven),
        // so the rule-triggering getters (RangeCells, GetUnavailableReason) are fine to call here.
        private string State()
        {
            if (_slot == null) return null;
            var sb = new StringBuilder();
            try
            {
                int ap = _slot.ActionPointCost.Value;
                if (ap > 0) Append(sb, ap + (ap == 1 ? " action point" : " action points"));

                var ab = _slot.AbilityData;
                if (ab != null) AppendAbilityDetails(sb, ab);

                if (_slot.IsReload.Value)
                    Append(sb, _slot.CurrentAmmo.Value + " of " + _slot.MaxAmmo.Value + " ammo");
                if (_slot.IsOnCooldown.Value)
                {
                    var cd = _slot.CooldownText.Value;
                    Append(sb, string.IsNullOrEmpty(cd) ? "on cooldown" : "cooldown " + cd);
                }
                if (_slot.IsSelected.Value) Append(sb, "targeting");
                else if (_slot.MechanicActionBarSlot != null && _slot.MechanicActionBarSlot.IsActive())
                    Append(sb, "active");

                // Why it's greyed out — the game's own reason (not enough AP, on cooldown, out of range, …), so a
                // disabled slot says the cause instead of a bare "disabled".
                if (ab != null && !Enabled)
                {
                    var why = UnavailableReason(ab);
                    if (why != null) Append(sb, "unavailable, " + why);
                }
            }
            catch { }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        // Range (melee vs cells, + a minimum if the weapon has one), what it targets, and limited uses.
        private void AppendAbilityDetails(StringBuilder sb, AbilityData ab)
        {
            try
            {
                var anchor = ab.TargetAnchor;

                // Range is meaningless for a self/owner ability; melee reads better as "melee" than "range 1 cell".
                if (anchor != AbilityTargetAnchor.Owner)
                {
                    if (ab.IsMelee) Append(sb, "melee");
                    else
                    {
                        int r = 0; try { r = ab.RangeCells; } catch { }
                        if (r > 1) Append(sb, "range " + r + " cells");
                        int min = 0; try { min = ab.MinRangeCells; } catch { }
                        if (min > 0) Append(sb, "minimum range " + min);
                    }
                }

                // What activating it will ask for.
                switch (anchor)
                {
                    case AbilityTargetAnchor.Owner: Append(sb, "self"); break;
                    case AbilityTargetAnchor.Unit: Append(sb, "targets a unit"); break;
                    case AbilityTargetAnchor.Point: Append(sb, ab.IsAOE ? "area effect" : "targets a point"); break;
                }

                // Limited uses (charges / per-day resource); -1 == at-will. Ammo weapons already read their ammo
                // above, so don't also say "N uses left" for them.
                if (!_slot.IsReload.Value)
                {
                    int uses = -1; try { uses = ab.GetAvailableForCastCount(); } catch { }
                    if (uses >= 0) Append(sb, uses + (uses == 1 ? " use left" : " uses left"));
                }
            }
            catch { }
        }

        // The game's localized "why greyed out" text, evaluated from where the caster will act (its desired
        // position, matching the on-screen tooltip). Null when there's no reason or no caster.
        private string UnavailableReason(AbilityData ab)
        {
            try
            {
                var caster = ab.Caster;
                if (caster == null) return null;
                Vector3 pos = caster.Position;
                try { var vpc = Game.Instance?.VirtualPositionController; if (vpc != null) pos = vpc.GetDesiredPosition(caster); }
                catch { }
                var reason = ab.GetUnavailableReason(pos);
                return string.IsNullOrWhiteSpace(reason) ? null : TextUtil.StripRichText(reason);
            }
            catch { return null; }
        }

        private static void Append(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(s);
        }
    }
}
