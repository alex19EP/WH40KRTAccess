using System;
using System.Text.RegularExpressions;
using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog; // AnswerVM
using Kingmaker.Code.Utility;                   // UIConstsExtensions
using RTAccess.UI.Graph;                        // NodeVtable
using RTAccess.UI.Proxies;                      // DialogChoiceGate

namespace RTAccess.UI
{
    /// <summary>
    /// Node factory for CONVERSATION answers — one player <see cref="AnswerVM"/> in a dialogue or a book
    /// event. Grown from the retired <c>DialogAnswerButton</c> proxy: an answer is just label + enabled +
    /// choose, so it reuses <see cref="GraphNodes.Button"/> ("label, button[, disabled]"). The twists:
    /// <list type="bullet">
    /// <item>The label is the game's OWN answer formatter (<c>UIConstsExtensions.GetAnswerFormattedString</c> —
    /// numbered prefix plus skill-check / soul-mark / condition tags, gated by the same dialogue settings, so
    /// we surface only what's drawn), with a plain unnumbered "Continue" for the system answer and a
    /// try/catch fallback.</item>
    /// <item>Activation routes through the sanctioned <see cref="RTAccess.UI.Proxies.DialogChoiceGate"/> —
    /// NEVER <see cref="AnswerVM.OnChooseAnswer"/> directly: the DialogChoiceGuard Harmony prefix blocks any
    /// selection that didn't come through the gate (the game's own EventSystem-Submit leak). RT keeps the
    /// node's default click sound: RT's OnChooseAnswer plays none (the game view plays it in its own
    /// Confirm), so the navigator's generic click supplies the feedback.</item>
    /// <item>Space = the VM's prebuilt DC/conditions/exchange tooltip (<see cref="AnswerVM.AnswerTooltip"/>),
    /// surfaced directly rather than reconstructed from link keys.</item>
    /// </list>
    /// </summary>
    public static class DialogNodes
    {
        /// <summary>One player answer as a button node. <paramref name="deliverable"/> (optional) folds the
        /// window-is-showing gate into enabled — the dialogue screen passes <c>IsVisible &amp;&amp; !faded</c>
        /// so a queued Enter can't fire into a cutscene-hidden cue; the book-event screen leaves it null
        /// (Enable alone, its current behavior).</summary>
        public static NodeVtable AnswerNode(AnswerVM vm, Func<bool> deliverable = null)
        {
            Func<bool> enabled = () => vm != null && vm.Enable.Value && (deliverable == null || deliverable());
            return GraphNodes.Button(
                () => AnswerText(vm),
                () => DialogChoiceGate.Choose(vm),
                enabled,
                tooltip: () => vm?.AnswerTooltip?.Value);
        }

        // Book events draw their answer number through UIDialog.AnswerDialogueBeFormat — a decorative
        // "-// 1 ---" prefix that TTS reads as literal punctuation ("dash slash slash…"). Normalize it to
        // the plain "1. " numbering regular dialogue answers speak; the capture keeps whatever the game
        // put in the number slot (digit or keybinding text).
        private static readonly Regex BookNumberDecoration =
            new Regex(@"^\s*-//\s*(.*?)\s*---\s*", RegexOptions.Compiled);

        /// <summary>The game's own TMP rich-text answer label ((<c>&lt;link&gt;</c>)-wrapped DC tags; Tts
        /// strips them at speak time). System/continue answers carry no DisplayText — a plain "Continue",
        /// UNNUMBERED. An answer picked on an earlier visit gets a "previously chosen" tail (see
        /// <see cref="PreviouslyChosen"/>). Also voiced as confirmation by the number-key quick-select in
        /// DialogueScreen.</summary>
        public static string AnswerText(AnswerVM vm)
        {
            var bp = vm?.Answer?.Value;
            if (bp == null) return "";
            string text;
            if (string.IsNullOrEmpty(bp.DisplayText))
                text = Message.Localized("ui", "label.continue").Resolve();
            else
            {
                try
                {
                    var s = UIConstsExtensions.GetAnswerFormattedString(bp, "DialogChoice" + vm.Index, vm.Index);
                    text = BookNumberDecoration.Replace(s, "$1. ");
                }
                catch { text = vm.Index + ". " + bp.DisplayText; }
            }
            if (PreviouslyChosen(vm)) text += ", " + Loc.T("dialog.previously_chosen");
            return text;
        }

        // The game's own "picked on an earlier visit" recolor condition, verbatim (DialogAnswerBaseView colors
        // the answer m_DialogColors.SelectedAnswer when CanSelect && IsAlreadySelected && !IsSystem &&
        // !IsCurrentUnselectedWithNewAnswers — the last so a branch that still leads to UNSEEN content doesn't
        // read as exhausted). Backed by the save-persisted DialogState.SelectedAnswers. On screen this is a
        // color change ONLY, so it must be voiced. Disabled answers skip it, matching the game's color
        // precedence (DisabledAnswer wins) — the node already appends its own "disabled".
        private static bool PreviouslyChosen(AnswerVM vm)
        {
            try
            {
                return vm.Enable.Value && !vm.IsSystem && vm.IsAlreadySelected() && !vm.IsCurrentUnselectedWithNewAnswers;
            }
            catch { return false; }
        }
    }
}
