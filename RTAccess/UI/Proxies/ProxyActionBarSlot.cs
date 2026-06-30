using System.Collections.Generic;
using System.Text;
using Kingmaker.Code.UI.MVVM.VM.ActionBar; // ActionBarSlotVM
using RTAccess.UI.Announcements;

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

        // AP cost + ammo + cooldown + targeting/active, read live off the VM's reactive mirrors.
        private string State()
        {
            if (_slot == null) return null;
            var sb = new StringBuilder();
            try
            {
                int ap = _slot.ActionPointCost.Value;
                if (ap > 0) Append(sb, ap + (ap == 1 ? " action point" : " action points"));
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
            }
            catch { }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static void Append(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(s);
        }
    }
}
