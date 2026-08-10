using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using RTAccess.Accessibility;     // TooltipReader, TooltipRef, TooltipPage, GlossaryLinks, NestedTooltips

namespace RTAccess.UI
{
    /// <summary>
    /// The shared Space-key tooltip CHOOSER — the single decision point every tooltip path funnels through
    /// (the graph-native factory OnTooltip slots in <see cref="GraphNodes"/>, and the reader's own lines
    /// when you drill deeper): a body → open it straight in the <see cref="RTAccess.Screens.TooltipScreen"/>
    /// reader as a PAGE — spoken lines each carrying the inline link terms that occur ON that line, so a
    /// term drills from the line you just heard it on — with caller-supplied SECTIONS (compare-vs-equipped
    /// cards), the rows' nested tooltips and any UNATTACHABLE links following the body lines as References
    /// entries; a body-less control with only extras → a <see cref="RTAccess.Screens.DrillMenuScreen"/>
    /// list to pick from; nothing → "No tooltip".
    ///
    /// Every extra is a <see cref="TooltipRef"/> — a label plus a TEMPLATE factory — so the page it opens
    /// comes back through <see cref="OpenTemplate"/> and gathers references of its own. That is what makes
    /// drilling recursive rather than one level deep: following a link lands on a real page, and that page
    /// links onward exactly like the one you came from.
    /// </summary>
    internal static class TooltipChooser
    {
        /// <summary>Open the chooser over already-gathered parts — the PLAIN-body path (no raw form, so no
        /// line links; a DLC blurb, a saved-game detail). <paramref name="title"/> is the focused control's
        /// label (the reader speaks it as its ScreenName; a null title on the body-less drill path falls
        /// back to the nav.references word).</summary>
        internal static void Open(string title, string body,
            IReadOnlyList<TooltipRef> sections = null, IReadOnlyList<TooltipRef> links = null)
        {
            var refs = new List<TooltipRef>();
            if (sections != null) refs.AddRange(sections);
            if (links != null) refs.AddRange(links);
            OpenPage(title, TooltipPage.FromPlain(body).Lines, refs,
                menuTitle: sections != null && sections.Count > 0 ? title : null);
        }

        /// <summary>The template path, and the landing point for every drill-in: render
        /// <paramref name="tpl"/> ONCE into the page model — lines with their own link terms attached, the
        /// blind-player equivalent of the highlighted words a sighted player sees inline — and harvest the
        /// nested tooltips its rows hang off themselves (a homeworld lists its granted talents as
        /// hover-for-detail icons, which flatten to bare names in text). A null template / empty render
        /// stays the "No tooltip" case.
        ///
        /// <paramref name="lead"/> (optional) is a paragraph placed before the rendered body — for the
        /// handful of controls whose hover detail the game keeps OUTSIDE the tooltip template, in a separate
        /// hint (the chargen attribute stepper's "+N per rank"). It is a paragraph of the page, not a
        /// drill-in entry: a one-line fact should not cost a second keypress.
        /// <paramref name="sections"/> (optional) are caller-supplied drill-in pages (compare-vs-equipped
        /// cards, a rank feature's category write-up) — listed BEFORE the template's own nested rows.
        /// <paramref name="linksFrom"/> (optional) is a second RAW link source mined on top of the body (a
        /// log line's own text over its tooltip template) — only terms the page does not already carry are
        /// added, so a term never appears both on its line and in References.
        /// <paramref name="resolve"/> (optional) is the caller-context link resolver
        /// (<see cref="RTAccess.Accessibility.SkillCheckLinks"/>) applied to body lines and
        /// <paramref name="linksFrom"/> alike.</summary>
        internal static void OpenTemplate(string title, TooltipBaseTemplate tpl, string lead = null,
            IReadOnlyList<TooltipRef> sections = null, string linksFrom = null,
            Func<string, string[], TooltipBaseTemplate> resolve = null)
        {
            var page = tpl != null ? TooltipReader.GetPage(tpl, resolve) : null;
            var lines = page != null ? page.Lines : new List<TooltipLine>();
            if (!string.IsNullOrWhiteSpace(lead))
                lines.Insert(0, new TooltipLine(lead.Trim(), null));

            var refs = new List<TooltipRef>();
            if (sections != null) refs.AddRange(sections);
            // Page-level sweep of the rows' nested tooltips: only the ones NOT already attached to their
            // own body line (label-derived ids match) — same rule as every other reference: the bottom
            // list keeps what has no line to live on.
            foreach (var r in NestedTooltips.Gather(tpl))
                if (page == null || page.Claim(r.Id))
                    refs.Add(r);
            if (page != null) refs.AddRange(page.Orphans);
            if (linksFrom != null)
                foreach (var r in GlossaryLinks.Gather(linksFrom, resolve))
                    if (page == null || page.Claim(r.Id))
                        refs.Add(r);

            OpenPage(title, lines, refs, menuTitle: title);
        }

        /// <summary>The RAW-string path: a node whose game text IS the body (a character-story write-up, an
        /// encyclopedia definition). The raw splits at its own paragraph breaks and each line keeps the
        /// link terms that occur on it.</summary>
        internal static void OpenRaw(string title, string raw,
            Func<string, string[], TooltipBaseTemplate> resolve = null)
        {
            var page = TooltipPage.FromRaw(raw, resolve);
            OpenPage(title, page.Lines, new List<TooltipRef>(page.Orphans), menuTitle: null);
        }

        /// <summary>Space on a row whose text is ALREADY the spoken label (a scrollback line, a book-event
        /// paragraph, a reader body line): follow the row's own drill targets — none → "No tooltip", exactly
        /// one → its page opens directly, several → a small chooser. The row's text is not re-read as a
        /// body; the target is what you asked for.</summary>
        internal static void FollowRefs(IReadOnlyList<TooltipRef> refs)
        {
            if (refs == null || refs.Count == 0)
            {
                Tts.Speak(Loc.T("nav.no_tooltip"), interrupt: true);
                return;
            }
            if (refs.Count == 1)
            {
                var only = refs[0];
                OpenTemplate(only.Label, only.Open?.Invoke());
                return;
            }
            RTAccess.Screens.DrillMenuScreen.Open(Loc.T("nav.references"), new List<TooltipRef>(refs));
        }

        /// <summary>As <see cref="FollowRefs"/>, over a RAW game string's inline links.</summary>
        internal static void FollowLinks(string raw,
            Func<string, string[], TooltipBaseTemplate> resolve = null)
            => FollowRefs(GlossaryLinks.Gather(raw, resolve));

        // The shared landing: lines → the reader (references nested inside); no lines but references → a
        // drill chooser; nothing → "No tooltip". menuTitle names the body-less chooser after the control
        // when the caller had sections of its own; links-only keeps the generic "References".
        private static void OpenPage(string title, List<TooltipLine> lines, List<TooltipRef> refs,
            string menuTitle)
        {
            if (lines.Count > 0)
            {
                RTAccess.Screens.TooltipScreen.Open(title, lines, refs);
                return;
            }
            if (refs.Count == 0)
            {
                Tts.Speak(Loc.T("nav.no_tooltip"), interrupt: true);
                return;
            }
            RTAccess.Screens.DrillMenuScreen.Open(menuTitle ?? Loc.T("nav.references"), refs);
        }
    }
}
