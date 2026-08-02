using System.Text.RegularExpressions;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateGlossary
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils;     // TooltipHelper
using Kingmaker.UI.Common;                          // UIUtility.GetKeysFromLink
using Owlcat.Runtime.UI.Tooltips;                   // TooltipBaseTemplate

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Extracts the inline <c>&lt;link&gt;</c> terms embedded in a game string and resolves each to the page
    /// it points at, so the tooltip key (Space) can offer them as drill-in entries — the blind-player
    /// equivalent of hovering a highlighted term, or right-clicking it for a full page, in the sighted UI.
    ///
    /// The source is raw, markup-intact game text — a node's own string (a log line, a dialogue cue), or,
    /// for a factory tooltip that carries only a template, the template's own markup-intact view render
    /// (see the <see cref="TooltipBaseTemplate"/> overload). Dialogue cue/answer text arrives here already
    /// <c>&lt;link&gt;</c>-expanded because <c>LocalizedString</c> runs the text-tool engine on read
    /// (glossary text-tools → real TMP link tags).
    ///
    /// Resolution is the game's OWN dispatcher, <c>TooltipHelper.GetLinkTooltipTemplate</c>, and we keep
    /// WHATEVER IT RETURNS. That dispatcher picks the right template per link type — ability, activatable
    /// ability, feature, buff, item, item/cargo blueprint, unit inspect, stat, soul-mark shift, colony
    /// resource, dialogue exchange/conditions, UI property — and only falls back to a bare glossary entry
    /// for true UI/encyclopedia links. This used to filter to <c>TooltipTemplateGlossary</c> alone, which
    /// meant every one of those richer kinds was silently dropped: a homeworld whose write-up links the
    /// talents it grants offered no way to read what those talents DO, because a talent link is a
    /// <c>TooltipTemplateFeature</c>, not a glossary entry. The one thing still dropped is a link that
    /// resolves to nothing at all (see <see cref="HasContent"/>).
    ///
    /// Links whose template needs context the raw string cannot supply — a dialogue skill-check result or
    /// DC, which the game resolves from the cue's / answer's own roll lists — resolve through the caller's
    /// <c>resolve</c> hook instead, exactly as the game's views pass those lists to <c>SetLinkTooltip</c>.
    /// </summary>
    internal static class GlossaryLinks
    {
        // TMP link tag: <link="KEY">label</link> or the unquoted <link=KEY>label</link> the text-tool engine
        // also emits. KEY is the raw link id, label may itself carry nested colour/bold markup.
        private static readonly Regex LinkTag =
            new Regex("<link=\"?(?<id>[^\">]+)\"?[^>]*>(?<txt>.*?)</link>",
                RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        /// <summary>The followable terms in a tooltip TEMPLATE's rendered text — the source for factory
        /// tooltips, which carry a template but no backing text of their own. The raw text is the game's own
        /// view render scraped MARKUP-INTACT (<see cref="TooltipViewScraper.ReadRaw"/> — the clean read
        /// strips the very tags we match).</summary>
        public static List<TooltipRef> Gather(TooltipBaseTemplate tpl)
            => Gather(tpl == null ? null : TooltipViewScraper.ReadRaw(tpl, TooltipTemplateType.Info));

        /// <summary>The followable terms in a RAW (markup-intact) game string, in first-appearance order,
        /// deduped by link id — the source for a node that carries its game text directly (a log line, a
        /// dialogue cue). <paramref name="resolve"/> (optional) gets first refusal on each link, receiving
        /// the raw id and the game's own key split; return null to fall through to the standard
        /// dispatcher.</summary>
        public static List<TooltipRef> Gather(string raw, Func<string, string[], TooltipBaseTemplate> resolve = null)
        {
            var outList = new List<TooltipRef>();
            if (string.IsNullOrEmpty(raw) || raw.IndexOf("<link", StringComparison.OrdinalIgnoreCase) < 0)
                return outList;

            HashSet<string> seen = null;
            foreach (Match m in LinkTag.Matches(raw))
            {
                var id = m.Groups["id"].Value;
                if (string.IsNullOrEmpty(id)) continue;
                seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!seen.Add(id)) continue; // a term repeated in the line drills once

                // Probe once to decide whether the link is worth offering, then discard the probe: the
                // entry re-resolves on follow so the page reads live state, never this instant's snapshot.
                Func<TooltipBaseTemplate> open = null;
                TooltipBaseTemplate probe = null;
                if (resolve != null)
                {
                    var keys = Keys(id);
                    probe = Safe(() => resolve(id, keys));
                    if (probe != null) open = () => Safe(() => resolve(id, keys));
                }
                if (open == null)
                {
                    probe = Resolve(id);
                    if (!HasContent(probe)) continue;
                    open = () => Resolve(id);
                }

                // Label = the anchor's own words. Some anchors are an ICON, not a word — a dialogue answer's
                // conditions and exchange links wrap a bare <sprite>, which strips to nothing — so fall back
                // to the target page's own title (the game's header string for that template) before the last
                // resort of speaking the raw link id, which reads as machine noise.
                var label = CleanLabel(m.Groups["txt"].Value)
                            ?? CleanLabel(Safe(() => TooltipReader.GetTitle(probe)))
                            ?? id;
                outList.Add(new TooltipRef(label, open, id));
            }
            return outList;
        }

        // The game's standard link → template dispatch, whatever kind it yields.
        private static TooltipBaseTemplate Resolve(string id) => Safe(() => TooltipHelper.GetLinkTooltipTemplate(id));

        /// <summary>A link is followable when it resolves to real content. Any non-glossary template
        /// (feature, ability, item, stat, unit, …) is content by construction. A glossary template counts
        /// only when it actually found an entry — the dispatcher returns an EMPTY glossary as its fallback
        /// for an unknown link type and for a skill-check link with no roll list, and offering those would
        /// put dead "no tooltip information" entries in the list.</summary>
        private static bool HasContent(TooltipBaseTemplate t)
        {
            if (t == null) return false;
            if (t is TooltipTemplateGlossary g) return g.GlossaryEntry != null;
            return true;
        }

        // The game's own id → keys split (the "ui:"-style prefix and multi-key packing), for the caller's
        // resolver; falls back to the raw id.
        private static string[] Keys(string id)
        {
            var k = Safe(() => UIUtility.GetKeysFromLink(id));
            return k != null && k.Length > 0 ? k : new[] { id };
        }

        private static T Safe<T>(Func<T> f) where T : class
        {
            try { return f(); } catch { return null; }
        }

        private static string CleanLabel(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var clean = TextUtil.StripRichTextSpaced(s);
            return string.IsNullOrEmpty(clean) ? null : clean;
        }
    }
}
