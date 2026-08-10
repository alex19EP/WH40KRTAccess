using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates; // TooltipTemplateSkillCheckResult / …DC
using Kingmaker.Controllers.Dialog;                // SkillCheckResult, SkillCheckDC
using Kingmaker.UI.Common;                          // EntityLink
using Owlcat.Runtime.UI.Tooltips;                   // TooltipBaseTemplate

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Link resolvers for the two <c>&lt;link&gt;</c> kinds that CANNOT be resolved from their id alone.
    ///
    /// Every other link type is self-describing — <c>TooltipHelper.GetLinkTooltipTemplate</c> looks the
    /// blueprint up and builds the page. A skill check is not: the id names only which check it is, while the
    /// numbers a player actually wants (the acting character, the stat total, the DC, the roll and the odds)
    /// live on the CUE's or ANSWER's own list. That is why the game's dispatcher takes those lists as extra
    /// parameters and its views hand them over at bind time —
    /// <c>DialogCuePCView</c> / <c>BookEventCueView</c> pass <c>ViewModel.SkillChecks</c>, and
    /// <c>DialogAnswerBaseView</c> / <c>BookEventAnswerView</c> pass <c>Answer.Value.SkillChecksDC</c>, each
    /// into <c>SetLinkTooltip</c>. Without them the dispatcher returns an empty glossary and the link is
    /// dropped, which is why "what's this difficulty / how was it calculated" used to be unanswerable.
    ///
    /// These are the <paramref name="resolve"/> hook <see cref="GlossaryLinks.Gather(string,
    /// System.Func{string, string[], TooltipBaseTemplate})"/> takes: each mirrors exactly one branch of the
    /// game's own switch and declines (null) for every other link type, so everything else falls through to
    /// the standard dispatcher. A null return from the factory itself means "no checks here" — pass it
    /// straight to Gather, which treats it as the plain path.
    /// </summary>
    internal static class SkillCheckLinks
    {
        /// <summary>Resolver for a CUE's already-rolled checks (the <c>SkillcheckResult</c> branch). The
        /// keys are handed through as the template's keywords, which is what adds the glossary write-up for
        /// that check type beneath the roll breakdown.</summary>
        public static Func<string, string[], TooltipBaseTemplate> Results(List<SkillCheckResult> checks)
        {
            if (checks == null || checks.Count == 0) return null;
            return (id, keys) => keys != null && keys.Length > 0
                && EntityLink.GetEntityType(keys[0]) == EntityLink.Type.SkillcheckResult
                    ? new TooltipTemplateSkillCheckResult(checks, keys)
                    : null;
        }

        /// <summary>Resolver for an ANSWER's previewed difficulties (the <c>SkillcheckDC</c> branch).</summary>
        public static Func<string, string[], TooltipBaseTemplate> Dcs(List<SkillCheckDC> dcs)
        {
            if (dcs == null || dcs.Count == 0) return null;
            return (id, keys) => keys != null && keys.Length > 0
                && EntityLink.GetEntityType(keys[0]) == EntityLink.Type.SkillcheckDC
                    ? new TooltipTemplateSkillCheckDC(dcs)
                    : null;
        }
    }
}
