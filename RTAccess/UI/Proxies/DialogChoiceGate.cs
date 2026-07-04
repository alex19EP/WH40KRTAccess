using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog; // AnswerVM

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// The single sanctioned gateway for choosing a dialogue answer from our parallel tree. The game's own
    /// dialogue view stays live underneath and reacts to Unity's EventSystem "Submit" (Enter) on its selected
    /// button — a path parallel to BOTH our navigator and KeyboardAccess — so the SAME Enter that e.g. dismissed
    /// a tutorial popped over the cue would fire <see cref="AnswerVM.OnChooseAnswer"/> and pick an answer the
    /// player never navigated to. <see cref="Choose"/> wraps our own call in a flag the arbitration prefix
    /// (<c>DialogChoiceGuard</c>, in <see cref="RTAccess.Screens.DialogueScreen"/>) reads to let OUR selection
    /// through and block every other source while a dialogue is live under focus mode. Main-thread only
    /// (Unity UI), so a plain static flag suffices.
    ///
    /// Lives in its own file (kept in the <c>RTAccess.UI.Proxies</c> namespace) so every fully-qualified
    /// <c>RTAccess.UI.Proxies.DialogChoiceGate</c> reference keeps compiling after the DialogAnswerButton
    /// proxy it used to co-reside with was retired. Consumed by <see cref="RTAccess.Screens.DialogueScreen"/>
    /// (the number-key quick-select, the cue Continue shortcut, and the DialogChoiceGuard prefix) and by the
    /// answer node factory (<see cref="RTAccess.UI.DialogNodes"/>); named in the input-path doc comment of
    /// <c>RTAccess.Input.GameInputLayerGate</c>.
    /// </summary>
    internal static class DialogChoiceGate
    {
        /// <summary>True only for the duration of an OUR-initiated OnChooseAnswer call.</summary>
        public static bool MineNow { get; private set; }

        public static void Choose(AnswerVM vm)
        {
            if (vm == null) return;
            MineNow = true;
            try { vm.OnChooseAnswer(); }
            finally { MineNow = false; }
        }
    }
}
