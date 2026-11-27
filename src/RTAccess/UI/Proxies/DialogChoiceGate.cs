using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog; // AnswerVM
using HarmonyLib;
using Kingmaker.Code.UI.MVVM.View.Dialog.BookEvent;
using Kingmaker.Code.UI.MVVM.View.Dialog.Dialog;
using Kingmaker.Networking;
using Kingmaker.UI.Common;
using Owlcat.Runtime.UI.MVVM;
using RTAccess.Screens;
using UnityEngine;

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
            if (vm == null || !vm.Enable.Value) return;
            MineNow = true;
            try
            {
                if (vm.IsSystem || !UINetUtility.InLobbyAndPlaying || UINetUtility.IsControlMainCharacter())
                    vm.OnChooseAnswer();
                else if (ScreenManager.Current is BookEventScreen)
                    FindAnswer<BookEventAnswerPCView>(vm)?.Confirm();
                else
                    FindAnswer<DialogAnswerPCView>(vm)?.Confirm();
            }
            finally { MineNow = false; }
        }

        private static T FindAnswer<T>(AnswerVM vm) where T : ViewBase<AnswerVM>
            => UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None)
                .FirstOrDefault(view => view.isActiveAndEnabled && ReferenceEquals(view.GetViewModel(), vm));
    }

    // A guest's native Confirm sends the ping immediately, before the OnChooseAnswer guard runs.
    [HarmonyPatch(typeof(PingNetManager), nameof(PingNetManager.PingDialogAnswerVote))]
    internal static class DialogVoteGuard
    {
        private static bool Prefix()
        {
            if (!Main.Enabled || !FocusMode.Active || DialogChoiceGate.MineNow) return true;
            var ctx = UiContexts.Dialog();
            return ctx?.DialogVM.Value == null && ctx?.BookEventVM.Value == null && ctx?.EpilogVM.Value == null;
        }
    }
}
