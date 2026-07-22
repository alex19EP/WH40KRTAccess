using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RTAccess
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
        public static string StripRichTextSpaced(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = SubSup.Replace(s, "");
            var src = s; // the evaluator indexes the string the Replace is running over
            s = RichTextTagRun.Replace(src, m =>
            {
                int i = m.Index - 1, j = m.Index + m.Length;
                bool glue = i >= 0 && j < src.Length
                    && char.IsDigit(src[i]) && char.IsDigit(src[j])
                    && !BreakTag.IsMatch(m.Value);
                return glue ? "" : " ";
            });
            s = Whitespace.Replace(s, " ");
            return s.Trim();
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
