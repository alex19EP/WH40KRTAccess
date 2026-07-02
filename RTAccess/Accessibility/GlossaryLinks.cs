using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateGlossary
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils;     // TooltipHelper
using Kingmaker.UI.Common;                          // UIUtility.GetKeysFromLink
using Owlcat.Runtime.UI.Tooltips;                   // TooltipBaseTemplate
using RTAccess.UI;

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Extracts the inline glossary/encyclopedia <c>&lt;link&gt;</c> terms embedded in an element's game text
    /// and resolves each to its definition, so the tooltip key (Space) can offer them as drill-in entries —
    /// the blind-player equivalent of hovering a highlighted term in the sighted UI.
    ///
    /// The source is <see cref="UIElement.GetLinkSourceText"/> (raw, markup intact). Dialogue cue/answer text
    /// arrives here already <c>&lt;link&gt;</c>-expanded because <c>LocalizedString</c> runs the text-tool
    /// engine on read (glossary text-tools → real TMP link tags). Resolution reuses the game's own path:
    /// <c>UIUtility.GetKeysFromLink</c> → the element's own <see cref="UIElement.ResolveLink"/> (tried first)
    /// → <c>TooltipHelper.GetLinkTooltipTemplate</c>, kept ONLY when it yields a <see cref="TooltipTemplateGlossary"/>
    /// (a real definition) so skill-check / condition / exchange links — surfaced via the element's own tooltip —
    /// don't leak in. Bodies render via <see cref="TooltipReader"/>.
    /// </summary>
    internal static class GlossaryLinks
    {
        // <link="KEY">label</link> — KEY is the raw link id, label may itself carry nested color/bold markup.
        private static readonly Regex LinkTag =
            new Regex("<link=\"([^\"]+)\"[^>]*>(.*?)</link>", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex RichText = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        internal readonly struct Entry
        {
            public readonly string Label;
            public readonly string Body;
            public Entry(string label, string body) { Label = label; Body = body; }
        }

        /// <summary>The resolvable glossary terms in an element's text as (label, definition) pairs, in
        /// first-appearance order, deduped by link id. Empty when the element exposes none.</summary>
        public static List<Entry> Gather(UIElement el)
        {
            var outList = new List<Entry>();
            var raw = el?.GetLinkSourceText();
            if (string.IsNullOrEmpty(raw) || raw.IndexOf("<link=", System.StringComparison.Ordinal) < 0)
                return outList;

            HashSet<string> seen = null;
            foreach (Match m in LinkTag.Matches(raw))
            {
                var id = m.Groups[1].Value;
                if (string.IsNullOrEmpty(id)) continue;
                seen ??= new HashSet<string>();
                if (!seen.Add(id)) continue; // a term repeated in the line drills once

                var tpl = ResolveGlossary(el, id);
                if (tpl == null) continue; // not a definitional link (skill-check / condition / etc.)
                var body = TooltipReader.GetFull(tpl);
                if (string.IsNullOrWhiteSpace(body)) continue;

                var label = CleanLabel(m.Groups[2].Value);
                outList.Add(new Entry(string.IsNullOrEmpty(label) ? id : label, body));
            }
            return outList;
        }

        // Element-specific link first (e.g. a dialogue skill-check link built from the cue's own data), else
        // the game's standard glossary/encyclopedia resolution — kept only when it is a glossary definition.
        private static TooltipBaseTemplate ResolveGlossary(UIElement el, string id)
        {
            try
            {
                var keys = UIUtility.GetKeysFromLink(id);
                var tpl = el.ResolveLink(id, keys) ?? TooltipHelper.GetLinkTooltipTemplate(id);
                return tpl is TooltipTemplateGlossary ? tpl : null;
            }
            catch { return null; }
        }

        private static string CleanLabel(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var clean = Whitespace.Replace(RichText.Replace(s, " "), " ").Trim();
            return clean.Length > 0 ? clean : null;
        }
    }
}
