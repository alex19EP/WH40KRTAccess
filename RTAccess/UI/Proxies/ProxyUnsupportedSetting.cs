using System.Collections.Generic;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// Placeholder for control types we haven't built proxies for yet (key bindings, difficulty
    /// dropdowns, accessibility images…). Keeps the tab navigable and tells the user the setting exists
    /// but isn't accessible yet. Replaced as we add those proxies.
    /// </summary>
    public sealed class ProxyUnsupportedSetting : UIElement
    {
        private readonly string _label;
        public ProxyUnsupportedSetting(string label) { _label = label; }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label));
            yield return new RoleAnnouncement("setting");
            yield return new ValueAnnouncement(Message.Localized("ui", "value.not_accessible"));
        }
    }
}
