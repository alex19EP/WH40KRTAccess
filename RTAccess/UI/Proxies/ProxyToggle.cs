using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// A boolean setting → toggle (the game's term for a checkbox). Value is "on"/"off" (read live);
    /// activate flips it. Announced "Label, toggle, on/off, [disabled], [description]".
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(TooltipAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyToggle : UIElement
    {
        private readonly SettingsEntityBoolVM _vm;

        public ProxyToggle(SettingsEntityBoolVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true; // toggling flips the value in place

        private bool Enabled => _vm != null && _vm.ModificationAllowed.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title?.Text ?? ""));
            yield return new RoleAnnouncement("toggle");
            yield return new ValueAnnouncement(_vm != null && _vm.GetTempValue()
                ? Message.Localized("ui", "value.on") : Message.Localized("ui", "value.off"));
            yield return new EnabledAnnouncement(Enabled);
            yield return new TooltipAnnouncement(Message.MaybeRaw(_vm?.Description));
        }

        public override string GetTooltipText() => _vm?.Description;

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.toggle"), _ => _vm.ChangeValue());
        }
    }
}
