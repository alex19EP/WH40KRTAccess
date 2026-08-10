using System;

namespace RTAccess.UI.Announcements
{
    /// <summary>
    /// Opts an <see cref="Announcement"/> subclass into the global Announcements settings UI. Without it
    /// the global category is still created (per-control-type overrides need it as a fallback) but hidden,
    /// so the user only configures that announcement through per-type overrides. Apply to announcements
    /// whose global toggle reads naturally (label, role, value, tooltip, position); skip context-specific ones.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ShowInGlobalSettingsAttribute : Attribute { }
}
