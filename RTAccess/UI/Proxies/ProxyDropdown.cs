using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// A dropdown setting → combo box. Value = the current option (from LocalizedValues). Left/Right step
    /// through the options and activate advances; each change re-announces the new value. (WOTR opens a
    /// submenu screen for this — deferred until the child-screen pattern lands; inline cycling is usable.)
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(TooltipAnnouncement), typeof(PositionAnnouncement))]
    [ElementSettingsKey("combo_box")]
    public sealed class ProxyDropdown : UIElement
    {
        private readonly SettingsEntityDropdownVM _vm;

        public ProxyDropdown(SettingsEntityDropdownVM vm) { _vm = vm; }

        public override bool ReannounceOnActivate => true; // cycling flips the value in place

        private bool Enabled => _vm != null && _vm.ModificationAllowed.Value;

        private string CurrentOption()
        {
            if (_vm == null) return "";
            var vals = _vm.LocalizedValues;
            int i = _vm.GetTempValue();
            return (vals != null && i >= 0 && i < vals.Count) ? vals[i] : "";
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title?.Text ?? ""));
            yield return new RoleAnnouncement("combo box");
            yield return new ValueAnnouncement(Message.Raw(CurrentOption()));
            yield return new EnabledAnnouncement(Enabled);
            yield return new TooltipAnnouncement(Message.MaybeRaw(_vm?.Description));
        }

        public override string GetTooltipText() => _vm?.Description;

        public override IEnumerable<ElementAction> GetActions()
        {
            if (!Enabled) yield break;
            // Activate opens the option list (the deliberate "open a menu and pick" flow); Left/Right also
            // step inline as a quick alternative. Both re-announce the new value (ReannounceOnActivate).
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.open"), _ => OpenSubmenu());
            yield return new ElementAction(ActionIds.Decrease, Message.Localized("ui", "action.previous"), _ => _vm.SetPrevValue());
            yield return new ElementAction(ActionIds.Increase, Message.Localized("ui", "action.next"), _ => _vm.SetNextValue());
        }

        private void OpenSubmenu()
        {
            var vals = _vm?.LocalizedValues;
            if (vals == null) return;
            RTAccess.Screens.ChoiceSubmenuScreen.Open(_vm.Title?.Text, vals, _vm.GetTempValue(), i => _vm.SetTempValue(i));
        }
    }
}
