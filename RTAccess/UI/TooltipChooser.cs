using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using RTAccess.Accessibility;     // TooltipReader, GlossaryLinks

namespace RTAccess.UI
{
    /// <summary>
    /// The shared Space-key tooltip CHOOSER — the single decision point both tooltip paths funnel through
    /// (the adapter path, <see cref="GraphNavigator"/>'s element-backed OpenTooltipOrLinks, and the
    /// graph-native factory OnTooltip slots in <see cref="GraphNodes"/>): one readable thing → open it
    /// straight in the <see cref="RTAccess.Screens.TooltipScreen"/> reader; several (a body plus rendered
    /// SECTIONS such as compare-vs-equipped cards, and/or inline glossary link terms) → a
    /// <see cref="RTAccess.Screens.DrillMenuScreen"/> list to pick from; nothing → "No tooltip".
    /// Keeping the branch in one place means a factory tooltip with glossary links no longer silently
    /// flattens to just its body text.
    /// </summary>
    internal static class TooltipChooser
    {
        /// <summary>Open the chooser over already-gathered parts. <paramref name="title"/> is the focused
        /// control's label (null falls back to the nav.details / nav.references words).</summary>
        internal static void Open(string title, string body,
            IReadOnlyList<(string label, string body)> sections, List<GlossaryLinks.Entry> links)
        {
            bool hasSections = sections != null && sections.Count > 0;
            bool hasLinks = links != null && links.Count > 0;

            if (!hasLinks && !hasSections)
            {
                // Nothing extra → the single-tooltip case: open the body directly, or say there's none.
                if (string.IsNullOrWhiteSpace(body)) { Tts.Speak(Loc.T("nav.no_tooltip"), interrupt: true); return; }
                RTAccess.Screens.TooltipScreen.Open(title, body);
                return;
            }

            // A drill chooser: the control's own tooltip first (if any), then its extra sections, then terms.
            var items = new List<(string, string)>();
            if (!string.IsNullOrWhiteSpace(body))
                items.Add((title ?? Loc.T("nav.details"), body));
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
