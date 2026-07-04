using System;
using System.Collections.Generic;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>One option row in a choice submenu (e.g. a dropdown's options). Activate selects it.</summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(PositionAnnouncement))]
    [ElementSettingsKey("radio_button")] // shared settings identity with ProxySelectionItem + ControlTypes.RadioButton
    public sealed class ProxyChoiceOption : UIElement
    {
        private readonly string _label;
        // Snapshot is safe: a ChoiceSubmenuScreen is ephemeral (rebuilt on every open, closed by the
        // selection itself), so the selected state can't change while this proxy lives.
        private readonly bool _selected;
        private readonly Action _select;

        public ProxyChoiceOption(string label, bool selected, Action select)
        {
            _label = label;
            _selected = selected;
            _select = select;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label));
            yield return new RoleAnnouncement("radio button");
            yield return new SelectedAnnouncement(_selected);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"), _ => _select?.Invoke());
        }
    }
}
