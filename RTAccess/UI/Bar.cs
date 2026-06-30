using System.Collections.Generic;
using RTAccess.UI.Announcements;

namespace RTAccess.UI
{
    /// <summary>
    /// A horizontal row of controls — like a List but Left/Right navigated — labeled as a
    /// whole (e.g. a key-binding row: "Move Forward" holding its two binding buttons).
    /// A single Tab-stop; children are labeled individually, so it suppresses position.
    /// </summary>
    public sealed class Bar : Container
    {
        public Bar(string label) : base(ContainerShape.HorizontalList, label) { }

        public override bool AnnouncePosition => false; // children carry their own labels

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (!string.IsNullOrEmpty(Label)) yield return new LabelAnnouncement(Message.Raw(Label));
            // No "list" role — it reads as the control's name, then you Left/Right its buttons.
        }
    }
}
