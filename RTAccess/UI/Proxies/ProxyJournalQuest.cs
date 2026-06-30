using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Journal; // JournalQuestVM
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One quest in the journal's quest list (<see cref="JournalQuestVM"/>). The quests form a selection
    /// group (the selected one drives the detail panel), so each is a <b>radio button</b>: it announces
    /// "selected" when it's the shown quest, plus the quest's state (active / completed / failed, and
    /// "updated" when it needs attention). Enter selects it (the game's <see cref="JournalQuestVM.SelectQuest"/>),
    /// which updates <c>JournalVM.SelectedQuest</c> and so the detail region.
    ///
    /// Ported from WrathAccess; retargeted to RT's <see cref="JournalQuestVM"/> (which exposes its state as
    /// bools rather than the WOTR localization keys). Cannot reuse <see cref="ProxySelectionItem"/> — that
    /// wraps a <c>SelectionGroupEntityVM</c>, which a quest VM is not. English is hardcoded via
    /// <see cref="Message.Raw"/> (no new locale keys); the role word resolves via the existing "ui" table key.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(ValueAnnouncement))]
    public sealed class ProxyJournalQuest : UIElement
    {
        private readonly JournalQuestVM _vm;

        public ProxyJournalQuest(JournalQuestVM vm) { _vm = vm; }

        public override bool CanFocus => _vm != null;

        private string State()
        {
            var s = _vm.IsCompleted ? "completed" : _vm.IsFailed ? "failed" : "active";
            if (_vm.IsAttention) s += ", updated";
            return s;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm.Title));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(_vm.IsSelected.Value);
            yield return new ValueAnnouncement(Message.Raw(State()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Raw("Select"), _ => _vm.SelectQuest());
        }
    }
}
