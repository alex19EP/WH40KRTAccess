using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateGlossary
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils;     // TooltipHelper
using Owlcat.Runtime.UI.Tooltips;                   // TooltipBaseTemplate

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Extracts the inline glossary/encyclopedia <c>&lt;link&gt;</c> terms embedded in a game string
    /// and resolves each to its definition, so the tooltip key (Space) can offer them as drill-in entries —
    /// the blind-player equivalent of hovering a highlighted term in the sighted UI.
    ///
    /// The source is raw, markup-intact game text — a node's own string (a log line, a dialogue cue), or,
    /// for a factory tooltip that carries only a template, the template's own markup-intact view render
    /// (see the <see cref="TooltipBaseTemplate"/> overload). Dialogue cue/answer text
    /// arrives here already <c>&lt;link&gt;</c>-expanded because <c>LocalizedString</c> runs the text-tool
    /// engine on read (glossary text-tools → real TMP link tags). Resolution reuses the game's own
    /// <c>TooltipHelper.GetLinkTooltipTemplate</c>, kept ONLY when it yields a <see cref="TooltipTemplateGlossary"/>
    /// (a real definition) so skill-check / condition / exchange links — surfaced via the control's own tooltip —
    /// don't leak in. Bodies render via <see cref="TooltipReader"/>.
    /// </summary>
    internal static class GlossaryLinks
    {
        // <link="KEY">label</link> — KEY is the raw link id, label may itself carry nested color/bold markup.
        private static readonly Regex LinkTag =
            new Regex("<link=\"([^\"]+)\"[^>]*>(.*?)</link>", RegexOptions.Compiled | RegexOptions.Singleline);

        internal readonly struct Entry
        {
            public readonly string Label;
            public readonly string Body;
            public Entry(string label, string body) { Label = label; Body = body; }
        }

        /// <summary>The resolvable glossary terms in a tooltip TEMPLATE's rendered text — the source for
        /// factory tooltips, which carry a template but no backing text of their own. The raw text is
        /// the game's own view render scraped MARKUP-INTACT (<see cref="TooltipViewScraper.ReadRaw"/> —
        /// the clean read strips the very tags we match).</summary>
        public static List<Entry> Gather(TooltipBaseTemplate tpl)
            => Gather(tpl == null ? null : TooltipViewScraper.ReadRaw(tpl, TooltipTemplateType.Info));

        /// <summary>The resolvable glossary terms in a RAW (markup-intact) game string as (label,
        /// definition) pairs, in first-appearance order, deduped by link id — the source for a node
        /// that carries its game text directly (a log line, a dialogue cue).</summary>
        public static List<Entry> Gather(string raw)
        {
            var outList = new List<Entry>();
            if (string.IsNullOrEmpty(raw) || raw.IndexOf("<link=", System.StringComparison.Ordinal) < 0)
                return outList;

            HashSet<string> seen = null;
            foreach (Match m in LinkTag.Matches(raw))
            {
                var id = m.Groups[1].Value;
                if (string.IsNullOrEmpty(id)) continue;
                seen ??= new HashSet<string>();
                if (!seen.Add(id)) continue; // a term repeated in the line drills once

                var tpl = ResolveGlossary(id);
                if (tpl == null) continue; // not a definitional link (skill-check / condition / etc.)
                var body = TooltipReader.GetFull(tpl);
                if (string.IsNullOrWhiteSpace(body)) continue;

                var label = CleanLabel(m.Groups[2].Value);
                outList.Add(new Entry(string.IsNullOrEmpty(label) ? id : label, body));
            }
            return outList;
        }

        // The game's standard glossary/encyclopedia resolution — kept only when it is a glossary definition.
        private static TooltipBaseTemplate ResolveGlossary(string id)
        {
            try
            {
                var tpl = TooltipHelper.GetLinkTooltipTemplate(id);
                return tpl is TooltipTemplateGlossary ? tpl : null;
            }
            catch { return null; }
        }

        private static string CleanLabel(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var clean = TextUtil.StripRichTextSpaced(s);
            return string.IsNullOrEmpty(clean) ? null : clean;
        }
    }
}
