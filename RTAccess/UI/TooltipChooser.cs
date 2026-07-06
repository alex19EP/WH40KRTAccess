using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using RTAccess.Accessibility;     // TooltipReader, GlossaryLinks

namespace RTAccess.UI
{
    /// <summary>
    /// The shared Space-key tooltip CHOOSER — the single decision point both tooltip paths funnel through
    /// (the adapter path, <see cref="GraphNavigator"/>'s element-backed OpenTooltipOrLinks, and the
    /// graph-native factory OnTooltip slots in <see cref="GraphNodes"/>): a body → open it straight in the
    /// <see cref="RTAccess.Screens.TooltipScreen"/> reader, with any rendered SECTIONS (compare-vs-equipped
    /// cards) and inline glossary link terms following the body lines as drill-in entries — the first press
    /// always reads the selected thing's own tooltip, never an intermediate menu; a body-less control with
    /// only extras → a <see cref="RTAccess.Screens.DrillMenuScreen"/> list to pick from; nothing →
    /// "No tooltip". Keeping the branch in one place means a factory tooltip with glossary links no longer
    /// silently flattens to just its body text.
    /// </summary>
    internal static class TooltipChooser
    {
        /// <summary>Open the chooser over already-gathered parts. <paramref name="title"/> is the focused
        /// control's label (the reader speaks it as its ScreenName; a null title on the body-less drill
        /// path falls back to the nav.references word).</summary>
        internal static void Open(string title, string body,
            IReadOnlyList<(string label, string body)> sections, List<GlossaryLinks.Entry> links)
        {
            bool hasSections = sections != null && sections.Count > 0;
            bool hasLinks = links != null && links.Count > 0;

            if (!string.IsNullOrWhiteSpace(body))
            {
                // The control has its own tooltip → read it immediately; the extra sections and
                // glossary terms follow the body lines as References entries inside the reader.
                var entries = new List<(string, string)>();
                if (hasSections) entries.AddRange(sections);
                if (hasLinks) foreach (var e in links) entries.Add((e.Label, e.Body));
                RTAccess.Screens.TooltipScreen.Open(title, body, entries);
                return;
            }

            if (!hasLinks && !hasSections) { Tts.Speak(Loc.T("nav.no_tooltip"), interrupt: true); return; }

            // No body of its own, only extras → a drill chooser to pick from.
            var items = new List<(string, string)>();
            if (hasSections) items.AddRange(sections);
            if (hasLinks) foreach (var e in links) items.Add((e.Label, e.Body));
            // Title by the control (its own name) when it carries sections; glossary-only keeps "References".
            RTAccess.Screens.DrillMenuScreen.Open(
                hasSections ? (title ?? Loc.T("nav.references")) : Loc.T("nav.references"), items);
        }

        /// <summary>The factory (template) path: render <paramref name="tpl"/> for the body AND mine the
        /// same render (markup-intact) for inline glossary links, then open the chooser — so a factory
        /// tooltip with link terms drills exactly like the adapter path. A null template / empty render
        /// stays the "No tooltip" case.</summary>
        internal static void OpenTemplate(string title, TooltipBaseTemplate tpl)
        {
            var body = tpl != null ? TooltipReader.GetFull(tpl) : null;
            var links = GlossaryLinks.Gather(tpl);
            Open(title, body, sections: null, links: links);
        }
    }
}
