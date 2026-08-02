using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate

namespace RTAccess.Accessibility
{
    /// <summary>One spoken line of a tooltip page, with the inline link terms that appear ON that line —
    /// so the reader can follow a term from the line being read instead of sending the user to a
    /// consolidated list at the bottom of the page. Null <see cref="Links"/> = a plain line.</summary>
    internal sealed class TooltipLine
    {
        public readonly string Text;
        public readonly List<TooltipRef> Links;

        public TooltipLine(string text, List<TooltipRef> links)
        {
            Text = text;
            Links = links != null && links.Count > 0 ? links : null;
        }
    }

    /// <summary>
    /// Assembles a tooltip's text into the reader's page model: spoken LINES each carrying the links that
    /// occur on it, plus the ORPHAN links that could not be attached to any line. Line-attached links are
    /// the fix for the "all links are consolidated at the very bottom" feedback: reviewing a page no longer
    /// means walking to the References list and back, losing your place — the term drills from the line
    /// you just heard it on.
    ///
    /// Both sources funnel here: a scraped template render (per-line clean+raw pairs from
    /// <see cref="TooltipViewScraper"/>) and a raw game string (split at the same break boundaries via
    /// <see cref="TextUtil.SplitRichLines"/>). After the lines are built, ONE whole-raw gather acts as the
    /// safety net: any link the per-line pass missed — an icon-only anchor on a line that stripped to
    /// noise, an anchor severed by a paragraph break inside its own text — lands in <see cref="Orphans"/>
    /// (the References list), so nothing reachable before line-attachment became unreachable.
    ///
    /// A page that turns out to be ONE breakless paragraph still sentence-splits for navigation (the same
    /// hand-built-string fallback the reader always had); its links re-attach to the first sentence whose
    /// text contains the link's label, and the rest fall through to <see cref="Orphans"/>.
    /// </summary>
    internal sealed class TooltipPage
    {
        public readonly List<TooltipLine> Lines = new List<TooltipLine>();
        public readonly List<TooltipRef> Orphans = new List<TooltipRef>();

        private readonly HashSet<string> _claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Claim a link id against this page: true when it is NOT already carried by a line or an
        /// orphan entry (and records it). For callers merging an extra link source (a log line's raw text
        /// over a template body) without duplicating what the page already offers.</summary>
        public bool Claim(string id) => id == null || _claimed.Add(id);

        /// <summary>Page from a scraped template render: the scraped lines (clean + raw + the row's own
        /// nested tooltip) + the whole raw text.</summary>
        public static TooltipPage FromScraped(List<TooltipViewScraper.ScrapedLine> lines, string wholeRaw,
            Func<string, string[], TooltipBaseTemplate> resolve = null)
        {
            var p = new TooltipPage();
            if (lines != null)
                foreach (var l in lines)
                    p.AddLine(l.Clean, l.Raw, resolve, l.Nested);
            p.Finish(wholeRaw, resolve);
            return p;
        }

        /// <summary>Page from a RAW (markup-intact) game string — a dialogue cue, an encyclopedia entry, a
        /// character-story write-up. Splits at the same boundaries the strip turns into line breaks, so the
        /// links stay on their lines by construction.</summary>
        public static TooltipPage FromRaw(string raw, Func<string, string[], TooltipBaseTemplate> resolve = null)
        {
            var p = new TooltipPage();
            foreach (var seg in TextUtil.SplitRichLines(raw))
            {
                var clean = TextUtil.StripRichTextLines(seg);
                if (!TextUtil.HasLetterOrDigit(clean)) continue; // noise line; Finish still mines its links
                p.AddLine(clean, seg, resolve);
            }
            p.Finish(raw, resolve);
            return p;
        }

        /// <summary>Page from already-stripped plain text (no raw form, so no links) — the brick-walk
        /// fallback and legacy string bodies.</summary>
        public static TooltipPage FromPlain(string body)
        {
            var p = new TooltipPage();
            foreach (var line in TextUtil.SplitSpokenLines(body))
                p.Lines.Add(new TooltipLine(line, null));
            return p;
        }

        private void AddLine(string clean, string raw, Func<string, string[], TooltipBaseTemplate> resolve,
            TooltipRef? nested = null)
        {
            if (string.IsNullOrWhiteSpace(clean)) return;
            var links = GlossaryLinks.Gather(raw, resolve);
            // The row's own card leads: Space on a talent row opens THE TALENT, and the row's inline
            // terms (if any) follow behind it in the chooser.
            if (nested != null) links.Insert(0, nested.Value);
            foreach (var r in links)
                if (r.Id != null) _claimed.Add(r.Id);
            Lines.Add(new TooltipLine(clean, links));
        }

        /// <summary>Mine <paramref name="raw"/> for links the page does not already carry and add them to
        /// <see cref="Orphans"/> — the safety net every builder runs over its whole raw text, and the merge
        /// point for callers with a SECOND link source (a log line's raw over a template body, a scraped
        /// raw whose every line stripped to noise).</summary>
        public void MineOrphans(string raw, Func<string, string[], TooltipBaseTemplate> resolve = null)
        {
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var r in GlossaryLinks.Gather(raw, resolve))
                if (Claim(r.Id)) Orphans.Add(r);
        }

        private void Finish(string wholeRaw, Func<string, string[], TooltipBaseTemplate> resolve)
        {
            if (Lines.Count == 1) SentenceSplit();
            MineOrphans(wholeRaw, resolve);
        }

        // The one-breakless-paragraph fallback: sentence-split the only line so it stays navigable, and
        // re-attach each link to the FIRST sentence containing its label (the anchor's own words appear in
        // the sentence they were spoken in). A label no sentence contains falls back to References.
        private void SentenceSplit()
        {
            var only = Lines[0];
            var parts = new List<string>(TextUtil.SplitSpokenLines(only.Text));
            if (parts.Count <= 1) return;

            Lines.Clear();
            var pending = only.Links != null ? new List<TooltipRef>(only.Links) : null;
            foreach (var part in parts)
            {
                List<TooltipRef> here = null;
                if (pending != null)
                    for (int i = pending.Count - 1; i >= 0; i--)
                    {
                        var label = pending[i].Label;
                        if (string.IsNullOrEmpty(label)) continue;
                        if (part.IndexOf(label, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        (here ??= new List<TooltipRef>()).Insert(0, pending[i]);
                        pending.RemoveAt(i);
                    }
                Lines.Add(new TooltipLine(part, here));
            }
            if (pending != null) Orphans.AddRange(pending);
        }
    }
}
