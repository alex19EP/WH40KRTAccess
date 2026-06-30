namespace RTAccess.UI.Announcements
{
    /// <summary>
    /// One addressable piece of an element's spoken focus message (label, role,
    /// status, position…). One class per semantic concept, reused everywhere it
    /// appears. Ported from SayTheSpire2.
    /// </summary>
    public abstract class Announcement
    {
        /// <summary>Stable identity (e.g. "label", "role", "position") — used for settings/ordering.</summary>
        public abstract string Key { get; }

        /// <summary>Rendered text. The context exposes per-element resolved settings (defaults for now).</summary>
        public abstract Message Render(AnnouncementContext ctx);

        /// <summary>Punctuation placed between this part and the next (last one's is dropped). Default comma.</summary>
        public virtual string Suffix => ",";
    }
}
