using System;
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

        /// <summary>The game's own TMP rich-text answer label ((<c>&lt;link&gt;</c>)-wrapped DC tags; Tts
        /// strips them at speak time). System/continue answers carry no DisplayText — a plain "Continue",
        /// UNNUMBERED. Also voiced as confirmation by the number-key quick-select in DialogueScreen.</summary>
        public static string AnswerText(AnswerVM vm)
        {
            var bp = vm?.Answer?.Value;
            if (bp == null) return "";
            if (string.IsNullOrEmpty(bp.DisplayText))
                return Message.Localized("ui", "label.continue").Resolve();
            try { return UIConstsExtensions.GetAnswerFormattedString(bp, "DialogChoice" + vm.Index, vm.Index); }
            catch { return vm.Index + ". " + bp.DisplayText; }
        }
    }
}
