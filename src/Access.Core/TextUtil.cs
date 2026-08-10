using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Access.Core
{
    /// <summary>
    /// Cleans game-sourced strings for speech. WotR UI text is TMP rich text —
    /// labels come pre-wrapped in tags (color/size/sprite/style, e.g. the main
    /// menu's "saber book" formatting), so we strip tags before speaking.
    /// </summary>
    public static class TextUtil
    {
        // Sub/superscripts are decorative (e.g. the per-level BAB shows iterative-attack indices as
        // "<sub><size=125%> 1 </size></sub>"); their content is noise in speech, so drop tag AND text.
        private static readonly Regex SubSup =
            new Regex("<(sub|sup)>.*?</(sub|sup)>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex RichTextTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        // A run of ADJACENT tags ("</color><size=110%>") is a single visual boundary — the glue-or-space
        // decision in StripRichTextSpaced must see it as one unit, not per tag.
        private static readonly Regex RichTextTagRun = new Regex("(?:<[^>]+>)+", RegexOptions.Compiled);
        // Explicit separator tags: a line/paragraph break is a real boundary no matter what characters
        // surround it.
        private static readonly Regex BreakTag =
            new Regex(@"<\s*/?\s*(br|p)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
        // Break-preserving collapse: horizontal runs fold to one space, then a run of newlines (with any
        // spaces around it) folds to a single '\n' — so blank lines vanish and one paragraph is one line.
        private static readonly Regex HorizontalWhitespace = new Regex(@"[^\S\n]+", RegexOptions.Compiled);
        private static readonly Regex NewlineRun = new Regex(@" ?\n[ \n]*", RegexOptions.Compiled);

        public static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = SubSup.Replace(s, "");   // remove sub/superscript blocks entirely (content included)
            // Strip remaining tags to nothing: real spaces in the text are preserved, and tags
            // are usually inline (e.g. a drop-cap "<size=200%>N</size>ew Game"), so a
            // space here would wrongly split words into "N ew Game".
            s = RichTextTag.Replace(s, "");
            s = Whitespace.Replace(s, " ");
            return s.Trim();
        }

        /// <summary>Like <see cref="StripRichText"/> but replaces each tag boundary with a SPACE rather than
        /// nothing, so segments joined only by a rich-text boundary don't weld into one word — e.g. a
        /// combat-log damage line and its emphasised "Critical hit!" suffix, which the game separates with a
        /// colour/size tag and no space. ONE exception: a styling-tag run with DIGITS on both sides glues —
        /// stat values are written per-character ("&lt;color&gt;3&lt;/color&gt;&lt;size=110%&gt;0&lt;/size&gt;",
        /// the char-sheet ability-score views) and TMP renders them as one number, so "30" must not read as
        /// "3 0"; an explicit break tag (&lt;br&gt;/&lt;p&gt;) between digits still separates. Use for
        /// combat-log, bark and scraped-tooltip text; prefer <see cref="StripRichText"/> for UI labels, where
        /// tight stripping keeps "N&lt;size&gt;ew Game" whole. Extra spaces around punctuation are audibly
        /// harmless — screen readers normalise them.</summary>
        public static string StripRichTextSpaced(string s) => StripSpaced(s, keepBreaks: false);

        /// <summary>Like <see cref="StripRichTextSpaced"/> but LINE BREAKS SURVIVE: a <c>&lt;br&gt;</c>/
        /// <c>&lt;p&gt;</c> tag run and any literal newline become a single <c>'\n'</c>, while every other
        /// whitespace run still collapses to one space. This is what carries a description's real PARAGRAPH
        /// structure into the tooltip reader, which splits its body on <c>'\n'</c> alone — sentence-splitting
        /// a paragraph is what tore the noble homeworld's "You. Serve me." into two separate lines. Use for
        /// any text that will be read as a document (tooltip bodies, the licence); the spaced form remains
        /// right for text that must end up on ONE spoken line (a combat-log entry, a browse label).</summary>
        public static string StripRichTextLines(string s) => StripSpaced(s, keepBreaks: true);

        private static string StripSpaced(string s, bool keepBreaks)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = SubSup.Replace(s, "");
            var src = s; // the evaluator indexes the string the Replace is running over
            s = RichTextTagRun.Replace(src, m =>
            {
                bool isBreak = BreakTag.IsMatch(m.Value);
                if (isBreak && keepBreaks) return "\n";
                int i = m.Index - 1, j = m.Index + m.Length;
                bool glue = i >= 0 && j < src.Length
                    && char.IsDigit(src[i]) && char.IsDigit(src[j])
                    && !isBreak;
                return glue ? "" : " ";
            });
            if (!keepBreaks) return Whitespace.Replace(s, " ").Trim();
            s = HorizontalWhitespace.Replace(s, " ");
            s = NewlineRun.Replace(s, "\n");
            return s.Trim();
        }

        // A single explicit break tag (not a run: an adjacent styling/link tag must stay with its own
        // segment — "<br><link=x>term</link>" severs the link if the whole run is treated as the boundary).
        private static readonly Regex BreakTagFull =
            new Regex(@"<\s*/?\s*(br|p)\b[^>]*>|\n", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Split RAW (markup-intact) text into segments at exactly the boundaries
        /// <see cref="StripRichTextLines"/> turns into <c>'\n'</c> — break tags and literal newlines — so a
        /// segment's strip is one reader line and its <c>&lt;link&gt;</c> anchors stay attached to the line
        /// they appear on. Splits at each break TAG individually (never a whole adjacent-tag run): a styling
        /// or link tag adjacent to the break belongs to a segment, not to the boundary. Whitespace-only
        /// segments are dropped (they are the blank lines the strip collapses); a segment that strips to
        /// noise is the CALLER's call, because a dropped segment may still carry a followable link.</summary>
        public static List<string> SplitRichLines(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;
            int start = 0;
            foreach (Match m in BreakTagFull.Matches(s))
            {
                Add(list, s.Substring(start, m.Index - start));
                start = m.Index + m.Length;
            }
            Add(list, s.Substring(start));
            return list;

            static void Add(List<string> into, string seg)
            {
                if (!string.IsNullOrWhiteSpace(seg)) into.Add(seg);
            }
        }

        // One navigable line per PARAGRAPH. Paragraph is the unit a document is read in, and stripped game
        // text already carries the breaks (brick boundaries, <br>/<p>, literal newlines) as '\n' as long as
        // it went through StripRichTextLines. Splitting a paragraph at sentence punctuation is what tore the
        // noble homeworld's "You. Serve me." into two lines, so it is NOT what we do to structured text. It
        // survives only for a body that arrived with NO breaks at all — a hand-built string (a message of
        // the day, a DLC blurb) that would otherwise be one unnavigable node — where there is no paragraph
        // structure to damage. (Moved here from TooltipScreen so the raw-line world shares one splitter.)
        private static readonly Regex SentenceSplit = new Regex(@"(?<=[\.!?]) +", RegexOptions.Compiled);

        /// <summary>Split an already-STRIPPED body into spoken reader lines: one per paragraph
        /// (<c>'\n'</c>), falling back to sentence-splitting only when the whole body carries no break at
        /// all. The tooltip/document readers' navigation model.</summary>
        public static IEnumerable<string> SplitSpokenLines(string body)
        {
            if (string.IsNullOrEmpty(body)) yield break;
            bool structured = body.IndexOf('\n') >= 0;
            foreach (var para in body.Split('\n'))
            {
                var p = para.Trim();
                if (p.Length == 0) continue;
                if (structured) { yield return p; continue; }
                foreach (var s in SentenceSplit.Split(p))
                {
                    var t = s.Trim();
                    if (t.Length > 0) yield return t;
                }
            }
        }

        /// <summary>True when the text carries at least one letter or digit — the "is this a real value or
        /// a prefab placeholder / bare separator" test shared by the tooltip scrape pipeline.</summary>
        public static bool HasLetterOrDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if (char.IsLetterOrDigit(c)) return true;
            return false;
        }

        /// <summary>Fold accents away for matching ("Séance" matches "seance"); ligatures œ/æ expand.
        /// Ported from OniAccess (VisionNotIncluded) with permission.</summary>
        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            for (int i = 0; i < decomposed.Length; i++)
            {
                char c = decomposed[i];
                switch (c)
                {
                    case 'œ': case 'Œ': sb.Append("oe"); break;
                    case 'æ': case 'Æ': sb.Append("ae"); break;
                    default:
                        if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
