using HarmonyLib;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog;
using Kingmaker.DialogSystem.Blueprints;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Auto-announce of the dialogue cue on <c>DialogVM.HandleOnCueShow</c>.
///
/// Cue reading now happens through the console focus path: <see cref="DialogNavAugmentor"/> injects the cue as
/// a focus stop (<see cref="VirtualNavItem"/>) that the focus reader speaks, and which can be re-read by
/// arrowing back up to it. This event-driven auto-read is therefore kept OFF by default — enabling it
/// alongside the injected cue item would read the cue twice. It remains as a fallback for contexts without a
/// focus ring (e.g. mouse mode), reusing <see cref="DialogText"/> so cue wording stays in one place.
/// </summary>
[HarmonyPatch(typeof(DialogVM), nameof(DialogVM.HandleOnCueShow))]
internal static class DialogCuePatch
{
    /// <summary>Off by default — the injected cue item (focus path) is the normal cue reader.</summary>
    internal static bool AutoReadEnabled = false;

    // HandleOnCueShow can re-fire for the same cue; dedupe on the blueprint.
    private static BlueprintCue _lastCue;

    private static void Postfix()
    {
        if (!AutoReadEnabled) return;
        try
        {
            var cue = Game.Instance?.DialogController?.CurrentCue;
            if (cue == null || ReferenceEquals(cue, _lastCue)) return;
            _lastCue = cue;

            var line = DialogText.BuildCueLine(null); // reads the controller's current cue + speaker
            if (string.IsNullOrEmpty(line)) return;
            Speaker.Speak(line, interrupt: false);
        }
        catch (Exception e)
        {
            Main.Log?.Log("dialogue cue read failed: " + e.Message);
        }
    }
}
