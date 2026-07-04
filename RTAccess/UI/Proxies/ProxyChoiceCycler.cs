using System;
using System.Collections.Generic;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// A combo-box control over a fixed option list, driven by delegates — reads "Label, combo box, Value"
    /// and on Enter opens a <see cref="RTAccess.Screens.ChoiceSubmenuScreen"/> to pick. Options / current
    /// index / select callback are supplied live so it can front any game enum (inventory filter, sort mode,
    /// …). Deliberately Activate-only (no Left/Right cycle): as a <see cref="BarRegion"/> cell it sits in a
    /// multi-cell row where the grid navigator uses Left/Right to move BETWEEN cells, so binding them to
    /// cycle would fight inter-cell navigation. The value is re-read on each focus, so it reflects the change
    /// after the owning screen rebuilds.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement))]
    public sealed class ProxyChoiceCycler : UIElement
    {
        private readonly Func<string> _label;
        private readonly Func<IReadOnlyList<string>> _options;
        private readonly Func<int> _current;
        private readonly Action<int> _select;

        public ProxyChoiceCycler(Func<string> label, Func<IReadOnlyList<string>> options,
            Func<int> current, Action<int> select)
        {
            _label = label;
            _options = options;
            _current = current;
            _select = select;
        }

        public override bool CanFocus => true;

        private string Value()
        {
            var opts = _options?.Invoke();
            int i = _current?.Invoke() ?? -1;
            return (opts != null && i >= 0 && i < opts.Count) ? opts[i] : "";
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label?.Invoke() ?? ""));
            yield return new RoleAnnouncement("combo box");
            yield return new ValueAnnouncement(Message.Raw(Value()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.open"), _ => Open());
        }

        private void Open()
        {
            var opts = _options?.Invoke();
            if (opts == null || opts.Count == 0) return;
            RTAccess.Screens.ChoiceSubmenuScreen.Open(_label?.Invoke(), opts, _current?.Invoke() ?? -1,
                i => _select?.Invoke(i));
        }
    }
}
