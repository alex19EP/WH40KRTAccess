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
    }
}
