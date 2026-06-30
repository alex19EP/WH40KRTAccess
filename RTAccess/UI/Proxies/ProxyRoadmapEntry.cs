using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One step in the character-generation roadmap: the phase name, whether it's the current step and
    /// whether it's completed, plus an optional live summary of what's chosen there (e.g. "Homeworld:
    /// Fortress World"). Activating jumps to the phase when it's reachable (available). Selecting a phase
    /// changes the wizard's current phase, which rebuilds + announces the new page — so this doesn't
    /// re-announce itself on activate.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyRoadmapEntry : UIElement
    {
        private readonly CharGenPhaseBaseVM _phase;
        private readonly Func<string> _summary; // optional live summary of the phase's choice

        public ProxyRoadmapEntry(CharGenPhaseBaseVM phase, Func<string> summary = null)
        {
            _phase = phase;
            _summary = summary;
        }

        private bool Available => _phase != null && _phase.IsAvailable.Value;
        private bool IsCurrent => _phase != null && _phase.IsSelected.Value;
        private bool IsCompleted => _phase != null && _phase.IsCompleted.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            var name = _phase?.PhaseName?.Value ?? "";
            var summary = _summary != null ? _summary() : null;
            yield return new LabelAnnouncement(Message.Raw(string.IsNullOrEmpty(summary) ? name : name + ", " + summary));
            yield return new RoleAnnouncement("tab");
            yield return new SelectedAnnouncement(IsCurrent);
            if (IsCompleted)
                yield return new ValueAnnouncement(Message.Localized("ui", "value.completed"));
            yield return new EnabledAnnouncement(Available);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Available)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"),
                    _ => _phase?.SetSelectedFromView(true));
        }
    }
}
