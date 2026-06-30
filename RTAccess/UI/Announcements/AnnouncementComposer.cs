using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace RTAccess.UI.Announcements
{
    /// <summary>
    /// Turns an element's yielded announcements into one focus Message: sorts by the
    /// element's [AnnouncementOrder] (undeclared appended in yield order), renders
    /// each, skips disabled (per ctx, defaults for now) and empty, and joins them with
    /// each part's Suffix between (the last suffix is dropped). Ported from SayTheSpire2.
    /// </summary>
    public static class AnnouncementComposer
    {
        public static Message Compose(UIElement element, IEnumerable<Announcement> announcements)
        {
            var ctx = new AnnouncementContext(element);
            var attr = element.AnnouncementOrderType.GetCustomAttribute<AnnouncementOrderAttribute>(true);
            var order = attr != null ? attr.Types : Array.Empty<Type>();

            var declared = new Dictionary<Type, Announcement>();
            var undeclared = new List<Announcement>();
            foreach (var a in announcements)
            {
                var t = a.GetType();
                if (Array.IndexOf(order, t) >= 0 && !declared.ContainsKey(t)) declared[t] = a;
                else undeclared.Add(a);
            }

            var sorted = new List<Announcement>(declared.Count + undeclared.Count);
            foreach (var t in order)
                if (declared.TryGetValue(t, out var a)) sorted.Add(a);
            sorted.AddRange(undeclared);

            var rendered = new List<KeyValuePair<string, string>>(); // (text, suffix)
            foreach (var a in sorted)
            {
                if (!ctx.ResolveBool(a.Key, "enabled", true)) continue;
                var text = a.Render(ctx)?.Resolve();
                if (string.IsNullOrEmpty(text)) continue;
                // include_suffix off → drop this part's trailing punctuation (parts stay space-joined).
                var suffix = ctx.ResolveBool(a.Key, "include_suffix", true) ? a.Suffix : "";
                rendered.Add(new KeyValuePair<string, string>(text, suffix));
            }

            if (rendered.Count == 0) return Message.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < rendered.Count; i++)
            {
                if (i > 0) { sb.Append(rendered[i - 1].Value); sb.Append(' '); }
                sb.Append(rendered[i].Key);
            }
            return Message.Raw(sb.ToString());
        }
    }
}
