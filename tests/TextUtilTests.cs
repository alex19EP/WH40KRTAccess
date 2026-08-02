using RTAccess;
using Xunit;

namespace RTAccess.Tests
{
    /// <summary>
    /// Pins the rich-text strip rules for speech — above all the StripRichTextSpaced boundary decision:
    /// tags become spaces so tag-welded segments don't glue, EXCEPT a styling-tag run with digits on both
    /// sides, which glues because the game writes stat values per-character
    /// ("&lt;color&gt;3&lt;/color&gt;&lt;size=110%&gt;0&lt;/size&gt;" renders as "30" —
    /// CharInfoAbilityScorePCView, scraped into e.g. the familiar/pet tooltip card).
    /// </summary>
    public class TextUtilTests
    {
        // --- StripRichTextSpaced: the digit-glue exception ---

        [Fact]
        public void Spaced_GluesPerCharacterStyledStatValue()
        {
            // The char-sheet ability-score composition: accented first digit + smaller rest.
            Assert.Equal("30",
                TextUtil.StripRichTextSpaced("<color=#AABBCC>3</color><size=110%>0</size>"));
        }

        [Fact]
        public void Spaced_GluesAcrossAdjacentTagRun()
        {
            // "</color><size=..>" between two digits is ONE boundary — glue, keeping the suffix intact.
            Assert.Equal("45%",
                TextUtil.StripRichTextSpaced("<color=red>4</color><size=50%>5%</size>"));
        }

        [Fact]
        public void Spaced_KeepsSpaceBetweenLetterSegments()
        {
            // The combat-log weld case the spaced variant exists for.
            Assert.Equal("damage Critical hit!",
                TextUtil.StripRichTextSpaced("damage<color=red>Critical hit!</color>"));
        }

        [Fact]
        public void Spaced_BreakTagBetweenDigitsStillSeparates()
        {
            Assert.Equal("5 3", TextUtil.StripRichTextSpaced("5<br>3"));
            Assert.Equal("5 3", TextUtil.StripRichTextSpaced("5</p><p>3"));
        }

        [Fact]
        public void Spaced_RealWhitespaceBetweenDigitsSurvives()
        {
            // A genuine space next to the tags means the digits were separate values.
            Assert.Equal("5 3", TextUtil.StripRichTextSpaced("<color=red>5</color> <color=red>3</color>"));
        }

        [Fact]
        public void Spaced_LetterDigitBoundaryKeepsSpace()
        {
            Assert.Equal("Level 5", TextUtil.StripRichTextSpaced("Level<color=red>5</color>"));
        }

        [Fact]
        public void Spaced_LeadingAndTrailingTagsTrim()
        {
            Assert.Equal("30 damage",
                TextUtil.StripRichTextSpaced("<b><color=#FFF>3</color><size=80%>0</size> damage</b>"));
        }

        // --- StripRichText: tight strip keeps drop-caps whole ---

        [Fact]
        public void Tight_KeepsDropCapWordsWhole()
        {
            Assert.Equal("New Game", TextUtil.StripRichText("<size=200%>N</size>ew Game"));
        }

        // --- shared: sub/superscript content is dropped entirely ---

        [Fact]
        public void SubSuperscriptContentDropsInBothModes()
        {
            Assert.Equal("Attack", TextUtil.StripRichText("Attack<sub><size=125%> 1 </size></sub>"));
            Assert.Equal("Attack", TextUtil.StripRichTextSpaced("Attack<sub><size=125%> 1 </size></sub>"));
        }

        // --- StripRichTextLines: paragraph structure survives, everything else still collapses ---
        // The tooltip reader splits its body on '\n' alone, so what this preserves IS the navigation model:
        // one line per paragraph. The whole point is that prose is NOT re-split at sentence punctuation —
        // that is what tore the noble homeworld's "You. Serve me." into two separate lines.

        [Fact]
        public void Lines_KeepsSentencesOfOneParagraphTogether()
        {
            Assert.Equal("You. Serve me.", TextUtil.StripRichTextLines("You. Serve me."));
        }

        [Fact]
        public void Lines_BreakTagBecomesNewline()
        {
            Assert.Equal("first\nsecond", TextUtil.StripRichTextLines("first<br>second"));
            Assert.Equal("first\nsecond", TextUtil.StripRichTextLines("first</p><p>second"));
        }

        [Fact]
        public void Lines_LiteralNewlineSurvives()
        {
            Assert.Equal("first\nsecond", TextUtil.StripRichTextLines("first\nsecond"));
        }

        [Fact]
        public void Lines_BlankLinesAndSurroundingSpaceCollapseToOneBreak()
        {
            // A paragraph gap is one boundary, not an empty navigable line.
            Assert.Equal("first\nsecond", TextUtil.StripRichTextLines("first  \n \n\n  second"));
            Assert.Equal("first\nsecond", TextUtil.StripRichTextLines("first<br><br>second"));
        }

        [Fact]
        public void Lines_HorizontalWhitespaceStillCollapses()
        {
            Assert.Equal("a b", TextUtil.StripRichTextLines("a \t  b"));
        }

        [Fact]
        public void Lines_LeadingAndTrailingBreaksTrim()
        {
            Assert.Equal("body", TextUtil.StripRichTextLines("<br>\n body \n<br>"));
        }

        [Fact]
        public void Lines_KeepsTheDigitGlueRule()
        {
            // The per-character stat-value composition must still read as one number...
            Assert.Equal("30", TextUtil.StripRichTextLines("<color=#AABBCC>3</color><size=110%>0</size>"));
            // ...but an explicit break between digits separates them onto their own lines.
            Assert.Equal("5\n3", TextUtil.StripRichTextLines("5<br>3"));
        }

        [Fact]
        public void Lines_KeepsSpaceBetweenTagWeldedLetterSegments()
        {
            Assert.Equal("damage Critical hit!",
                TextUtil.StripRichTextLines("damage<color=red>Critical hit!</color>"));
        }

        [Fact]
        public void Lines_DropsSubSuperscriptContentToo()
        {
            Assert.Equal("Attack", TextUtil.StripRichTextLines("Attack<sub><size=125%> 1 </size></sub>"));
        }

        [Fact]
        public void Spaced_StillFlattensBreaksToSpaces()
        {
            // The one-spoken-line variant is unchanged — a combat-log entry must not gain line breaks.
            Assert.Equal("first second", TextUtil.StripRichTextSpaced("first<br>second"));
            Assert.Equal("first second", TextUtil.StripRichTextSpaced("first\nsecond"));
        }

        // --- SplitRichLines: RAW segments at exactly the boundaries StripRichTextLines makes '\n' ---
        // This is what lets a tooltip line keep the <link> anchors that occur ON it: the raw splits first,
        // each segment strips to one reader line, and the segment's tags are still intact for mining.

        [Fact]
        public void RawSplit_BreakTagsAndNewlinesBound()
        {
            Assert.Equal(new[] { "first", "second", "third" },
                TextUtil.SplitRichLines("first<br>second\nthird"));
            Assert.Equal(new[] { "first", "second" },
                TextUtil.SplitRichLines("first</p><p>second"));
        }

        [Fact]
        public void RawSplit_KeepsLinkTagsIntactWithinTheirSegment()
        {
            var segs = TextUtil.SplitRichLines("para one.<br><link=\"g:Int\">Intelligence</link> matters.");
            Assert.Equal(2, segs.Count);
            Assert.Equal("para one.", segs[0]);
            // The link tag adjacent to the break belongs to the SECOND segment — treating the whole
            // adjacent-tag run as the boundary would sever the anchor and lose the link.
            Assert.Equal("<link=\"g:Int\">Intelligence</link> matters.", segs[1]);
        }

        [Fact]
        public void RawSplit_BlankSegmentsDrop()
        {
            Assert.Equal(new[] { "a", "b" }, TextUtil.SplitRichLines("a<br> <br>\n\nb"));
        }

        [Fact]
        public void RawSplit_AlignsWithTheLineStrip()
        {
            // The contract the pairing relies on: strip-of-segments == segments-of-strip.
            var raw = "one <b>bold</b>.<br>two<link=x>term</link>\nthree";
            var viaWhole = TextUtil.StripRichTextLines(raw).Split('\n');
            var viaSegments = TextUtil.SplitRichLines(raw)
                .ConvertAll(s => TextUtil.StripRichTextLines(s));
            Assert.Equal(viaWhole, viaSegments);
        }

        // --- SplitSpokenLines: the reader's navigation model (moved from TooltipScreen) ---

        [Fact]
        public void SpokenSplit_StructuredBodySplitsByParagraphOnly()
        {
            // A body WITH breaks never re-splits at sentence punctuation ("You. Serve me." stays whole).
            Assert.Equal(new[] { "You. Serve me.", "Second para." },
                TextUtil.SplitSpokenLines("You. Serve me.\nSecond para."));
        }

        [Fact]
        public void SpokenSplit_BreaklessBodyFallsBackToSentences()
        {
            Assert.Equal(new[] { "One.", "Two!", "Three?" },
                TextUtil.SplitSpokenLines("One. Two! Three?"));
        }

        [Fact]
        public void SpokenSplit_EmptyAndWhitespaceYieldNothing()
        {
            Assert.Empty(TextUtil.SplitSpokenLines(null));
            Assert.Empty(TextUtil.SplitSpokenLines("  \n \n"));
        }
    }
}
