using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog; // AnswerVM
using Kingmaker.Code.Utility;                   // UIConstsExtensions

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// Builds a <see cref="ProxyActionButton"/> for one player answer in a conversation
    /// (<see cref="AnswerVM"/>). An answer is just label + enabled + choose, so it's a plain button; the
    /// twists are read here: the label uses the game's own answer formatter (numbered prefix plus
    /// skill-check / soul-mark / condition tags, gated by the same dialogue settings, so we surface only
    /// what's drawn), activation goes through <see cref="AnswerVM.OnChooseAnswer"/> (which advances the
    /// dialogue — the next cue announces itself), and the action verb reads "Choose".
    ///
    /// RT difference vs WOTR: <see cref="AnswerVM.OnChooseAnswer"/> plays NO sound (the game's view plays
    /// the click in its own Confirm()), so we do NOT suppress our button's click — it supplies the feedback.
    /// The DC / conditions / exchange tooltip is already built on the VM (<see cref="AnswerVM.AnswerTooltip"/>),
    /// so we surface it directly instead of reconstructing it from link keys.
    /// </summary>
    public static class DialogAnswerButton
    {
        public static ProxyActionButton For(AnswerVM vm)
            => new ProxyActionButton(
                label: () => AnswerText(vm),
                enabled: () => vm != null && vm.Enable.Value,
                activate: () => vm?.OnChooseAnswer(),
                actionVerb: "choose",
                tooltip: () => vm?.AnswerTooltip?.Value);

        // Returns the game's own TMP rich-text answer label ((<link>)-wrapped DC tags); Tts strips that at
        // speak time. System/continue answers carry no DisplayText — they read a plain "Continue", UNNUMBERED.
        private static string AnswerText(AnswerVM vm)
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
