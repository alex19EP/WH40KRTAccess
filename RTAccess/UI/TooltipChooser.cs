using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using RTAccess.Accessibility;     // TooltipReader, TooltipRef, GlossaryLinks, NestedTooltips

namespace RTAccess.UI
{
    /// <summary>
    /// The shared Space-key tooltip CHOOSER — the single decision point every tooltip path funnels through
    /// (the graph-native factory OnTooltip slots in <see cref="GraphNodes"/>, and the reader's own
    /// references when you drill deeper): a body → open it straight in the
    /// <see cref="RTAccess.Screens.TooltipScreen"/> reader, with any caller-supplied SECTIONS
    /// (compare-vs-equipped cards), the rows' nested tooltips and the inline link terms following the body
    /// lines as drill-in entries — the first press always reads the selected thing's own tooltip, never an
    /// intermediate menu; a body-less control with only extras → a
    /// <see cref="RTAccess.Screens.DrillMenuScreen"/> list to pick from; nothing → "No tooltip".
    ///
    /// Every extra is a <see cref="TooltipRef"/> — a label plus a TEMPLATE factory — so the page it opens
    /// comes back through <see cref="OpenTemplate"/> and gathers references of its own. That is what makes
    /// drilling recursive rather than one level deep: following a link lands on a real page, and that page
    /// links onward exactly like the one you came from.
    /// </summary>
    internal static class TooltipChooser
    {
        /// <summary>Open the chooser over already-gathered parts. <paramref name="title"/> is the focused
        /// control's label (the reader speaks it as its ScreenName; a null title on the body-less drill
        /// path falls back to the nav.references word).</summary>
        internal static void Open(string title, string body,
            IReadOnlyList<TooltipRef> sections = null, IReadOnlyList<TooltipRef> links = null)
        {
            bool hasSections = sections != null && sections.Count > 0;
            bool hasLinks = links != null && links.Count > 0;

            if (!string.IsNullOrWhiteSpace(body))
            {
                // The control has its own tooltip → read it immediately; the extra sections and
                // link terms follow the body lines as References entries inside the reader.
                var entries = new List<TooltipRef>();
                if (hasSections) entries.AddRange(sections);
                if (hasLinks) entries.AddRange(links);
                RTAccess.Screens.TooltipScreen.Open(title, body, entries);
                return;
            }

            if (!hasLinks && !hasSections) { Tts.Speak(Loc.T("nav.no_tooltip"), interrupt: true); return; }

            // No body of its own, only extras → a drill chooser to pick from.
            var items = new List<TooltipRef>();
            if (hasSections) items.AddRange(sections);
            if (hasLinks) items.AddRange(links);
            // Title by the control (its own name) when it carries sections; links-only keeps "References".
            RTAccess.Screens.DrillMenuScreen.Open(
                hasSections ? (title ?? Loc.T("nav.references")) : Loc.T("nav.references"), items);
        }

        /// <summary>The factory (template) path, and the landing point for every drill-in: render
        /// <paramref name="tpl"/> for the body, mine the same render (markup-intact) for its inline link
        /// terms, and harvest the nested tooltips its rows hang off themselves — a homeworld lists its
        /// granted talents as hover-for-detail icons, which flatten to bare names in text. A null template /
        /// empty render stays the "No tooltip" case.</summary>
        internal static void OpenTemplate(string title, TooltipBaseTemplate tpl)
            => OpenTemplate(title, tpl, null);

        /// <summary>As above, with a <paramref name="lead"/> paragraph placed before the rendered body — for
        /// the handful of controls whose hover detail the game keeps OUTSIDE the tooltip template, in a
        /// separate hint (the chargen attribute stepper's "+N per rank"). It is a paragraph of the page, not
        /// a drill-in entry: a one-line fact should not cost a second keypress. Null/blank leaves the page
        /// exactly as the plain overload builds it.</summary>
        internal static void OpenTemplate(string title, TooltipBaseTemplate tpl, string lead)
        {
            var body = tpl != null ? TooltipReader.GetFull(tpl) : null;
            if (!string.IsNullOrWhiteSpace(lead))
                body = string.IsNullOrWhiteSpace(body) ? lead.Trim() : lead.Trim() + "\n" + body;
            Open(title, body, sections: NestedTooltips.Gather(tpl), links: GlossaryLinks.Gather(tpl));
        }
    }
}
