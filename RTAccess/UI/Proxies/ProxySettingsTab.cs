using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.Settings.Menu;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>A settings tab (Game / Controls / Graphics / Sound / Accessibility / …). Activate switches to it.</summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(SelectedAnnouncement),
        typeof(PositionAnnouncement))]
    [ElementSettingsKey("tab")] // shared settings identity with ControlTypes.Tab
    public sealed class ProxySettingsTab : UIElement
    {
        private readonly SettingsMenuEntityVM _tab;

        public ProxySettingsTab(SettingsMenuEntityVM tab) { _tab = tab; }

        public override bool ReannounceOnActivate => true; // selecting flips it to "selected" in place

        private bool IsSelected => _tab != null && _tab.IsSelected.Value;

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_tab?.Title?.Value ?? ""));
            yield return new RoleAnnouncement("tab");
            yield return new SelectedAnnouncement(IsSelected); // speaks "selected" only on the current tab
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // SetSelectedFromView(true) is what the tab button calls on click: the selection group updates
            // SelectedMenuEntity then runs DoSelectMe → SetSettingsList. We replicate the game's own flow.
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.select"),
                _ => _tab?.SetSelectedFromView(true));
        }
    }
}
