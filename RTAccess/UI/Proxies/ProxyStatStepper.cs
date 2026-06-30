using System.Collections.Generic;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Stats;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One attribute row in the chargen point-buy: the stat name + its current value (with any invested
    /// ranks and a recommendation flag), with Left/Right to lower/raise it by refunding or spending a
    /// point. Raise/lower route through the game's advance-stat command; they're only offered while the
    /// stat can still move (CanAdvance / CanRetreat).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyStatStepper : UIElement
    {
        private readonly CharGenAttributesItemVM _vm;

        public ProxyStatStepper(CharGenAttributesItemVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true; // raising/lowering changes the value in place

        private string ValueText()
        {
            if (_vm == null) return "";
            var s = _vm.StatValue.Value.ToString();
            int ranks = _vm.StatRanks.Value;
            if (ranks > 0) s += ", " + Loc.T("chargen.stat_ranks", new { ranks });
            if (_vm.IsRecommended.Value) s += ", " + Loc.T("chargen.recommended");
            return s;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.DisplayName ?? ""));
            yield return new RoleAnnouncement("slider");
            yield return new ValueAnnouncement(Message.Raw(ValueText()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (_vm == null) yield break;
            if (_vm.CanRetreat.Value)
                yield return new ElementAction(ActionIds.Decrease, Message.Localized("ui", "action.lower"), _ => _vm.RetreatStat());
            if (_vm.CanAdvance.Value)
                yield return new ElementAction(ActionIds.Increase, Message.Localized("ui", "action.raise"), _ => _vm.AdvanceStat());
        }
    }
}
