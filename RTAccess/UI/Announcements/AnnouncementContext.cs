namespace RTAccess.UI.Announcements
{
    /// <summary>
    /// Supplied to <see cref="Announcement.Render"/> and the composer to resolve per-announcement settings
    /// with the SayTheSpire2 cascade: inner element key → outer proxy key → global default (most specific
    /// wins). The inner key comes from <see cref="UIElement.AnnouncementOrderType"/> (which also governs
    /// ordering); the outer key is the focused element's actual type, used only for composite proxies whose
    /// order type differs. A per-element override counts only when it's explicitly set (not inheriting).
    /// </summary>
    public sealed class AnnouncementContext
    {
        public UIElement Element { get; }
        public string ElementKey { get; } // from AnnouncementOrderType
        public string OuterKey { get; }   // from the actual type, when it differs (composite proxy)

        public AnnouncementContext(UIElement element)
        {
            Element = element;
            if (element == null) return;
            var innerType = element.AnnouncementOrderType;
            ElementKey = AnnouncementRegistry.DeriveElementKey(innerType);
            var outerType = element.GetType();
            if (outerType != innerType)
            {
                var outer = AnnouncementRegistry.DeriveElementKey(outerType);
                if (outer != ElementKey) OuterKey = outer;
            }
        }

        public bool ResolveBool(string announcementKey, string settingKey, bool defaultValue)
        {
            var ov = ResolveOverride(announcementKey, settingKey);
            if (ov != null && ov.IsOverridden) return ov.LocalValue.Value;

            var global = RTAccess.Settings.ModSettings.GetSetting<RTAccess.Settings.BoolSetting>(
                "announcements." + announcementKey + "." + settingKey);
            return global != null ? global.Get() : defaultValue;
        }

        public int ResolveInt(string announcementKey, string settingKey, int defaultValue) => defaultValue;
        public string ResolveString(string announcementKey, string settingKey, string defaultValue) => defaultValue;

        // The most-specific explicitly-set override (inner before outer), or null.
        private RTAccess.Settings.NullableBoolSetting ResolveOverride(string announcementKey, string settingKey)
        {
            if (ElementKey == null) return null;
            var inner = RTAccess.Settings.ModSettings.GetSetting<RTAccess.Settings.NullableBoolSetting>(
                "ui." + ElementKey + ".announcements." + announcementKey + "." + settingKey);
            if (inner != null && inner.IsOverridden) return inner;
            if (OuterKey != null)
            {
                var outer = RTAccess.Settings.ModSettings.GetSetting<RTAccess.Settings.NullableBoolSetting>(
                    "ui." + OuterKey + ".announcements." + announcementKey + "." + settingKey);
                if (outer != null && outer.IsOverridden) return outer;
            }
            return null;
        }
    }
}
