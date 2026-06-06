using HarmonyLib;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog;
using Kingmaker.Code.UI.MVVM.View.Dialog.Dialog.Console;
using Kingmaker.Code.UI.MVVM.View.Dialog.SurfaceDialog;
using Kingmaker.DialogSystem.Blueprints;

namespace RTAccess.Accessibility;

/// <summary>
/// Injects the NPC cue as the FIRST focus stop in the dialogue answer navigation, so a blind player hears the
/// dialogue text before the options and can arrow back up to re-read it — all inside the game's own arrow-key
/// ring. The cue is a <see cref="VirtualNavItem"/>; the real answer rows are untouched and commit as before.
///
/// Hook: postfix <c>SurfaceDialogBaseView&lt;DialogAnswerConsoleView&gt;.CreateNavigation()</c> — the closed
/// generic used in forced-console mode (see <see cref="ConsoleMode"/>). The base has just run
/// <c>SetEntitiesVertical(Answers)</c>; we insert the cue entity at index 0
/// (<c>GridConsoleNavigationBehaviour</c> navigates by list order, so no on-screen geometry is needed) and
/// focus it, making the cue the announced, re-readable first item. Reading happens through the existing
/// <see cref="SetFocusedPatch"/> path, which dedupes on the focused entity, so the cue is spoken exactly once.
/// </summary>
[HarmonyPatch(typeof(SurfaceDialogBaseView<DialogAnswerConsoleView>), "CreateNavigation")]
internal static class DialogNavAugmentor
{
    // Speaker-change tracking so a run of cues from the same NPC names the speaker only once.
    private static DialogVM _lastVm;
    private static BlueprintCue _lastCue;
    private static string _lastSpeaker;

    private static void Postfix(SurfaceDialogBaseView<DialogAnswerConsoleView> __instance)
    {
        try
        {
            var nav = __instance.NavigationBehaviour;
            var vm = __instance.ViewModel;
            if (nav == null || vm == null) return;

            // Decide once per cue whether to name the speaker, then bake it into the item so re-reads match.
            bool includeSpeaker = ShouldAnnounceSpeaker(vm);

            // Lazy text so a re-read (arrow back up to the cue) reflects the current cue line.
            var cueItem = new VirtualNavItem(
                text: () => DialogText.BuildCueLine(vm.Cue?.Value, includeSpeaker),
                anchor: __instance.m_CueView); // game scrolls the visible cue into view when focused

            nav.InsertVertical(0, cueItem);

            // Make the cue the initial focus so a new cue is announced cue-first via the normal focus read.
            // SetFocusedPatch dedupes on the focused entity, so re-asserting focus here cannot double-read.
            nav.FocusOnEntityManual(cueItem);
        }
        catch (Exception e)
        {
            Main.Log?.Log("dialog cue injection failed: " + e.Message);
        }
    }

    /// <summary>
    /// True when the current cue's speaker should be named: the first cue of a conversation, and whenever the
    /// speaker differs from the previous cue. A run of cues from the same NPC is named only once. The decision
    /// is made once per cue (and baked into the cue item) so re-reads of the same cue stay consistent.
    /// </summary>
    private static bool ShouldAnnounceSpeaker(DialogVM vm)
    {
        var dc = Game.Instance?.DialogController;

        // New conversation (each gets a fresh DialogVM): forget the previous speaker so its first line names it.
        if (!ReferenceEquals(vm, _lastVm)) { _lastVm = vm; _lastSpeaker = null; _lastCue = null; }

        // CreateNavigation can re-fire for the same cue; only advance tracking on a genuinely new cue.
        var cue = dc?.CurrentCue;
        if (ReferenceEquals(cue, _lastCue)) return false;
        _lastCue = cue;

        var speaker = dc?.CurrentSpeakerName;
        bool changed = !string.Equals(speaker, _lastSpeaker, StringComparison.Ordinal);
        _lastSpeaker = speaker;
        return changed;
    }
}
