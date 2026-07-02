using System;
using Kingmaker.Blueprints.Root.Strings;   // UINotificationTexts
using Kingmaker.PubSubSystem;               // ISoulMarkShiftHandler
using Kingmaker.UI.Common;                  // UIUtility
using Kingmaker.UnitLogic.Alignments;       // ISoulMarkShiftProvider, SoulMarkShift
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Voices conviction (soul-mark) shifts — "Faith +2", "Corruption +1", and so on. This is the ONE dialogue
/// notification the game does NOT write to its log (no thread, no GameLogEvent), so unlike the rest it
/// cannot come from <see cref="LogTap"/>; it rides its own <c>EventBus</c> subscription instead. The wording
/// reuses the game's own localized <see cref="UINotificationTexts.SoulMarksShiftFormat"/> plus
/// <see cref="UIUtility.GetSoulMarkDirectionText"/>, so it matches the on-screen popup and the player's
/// language. A persistent session subscriber (subscribed at load, unsubscribed at unload — see
/// <see cref="Main"/>); speech is queued (interrupt:false) like the other passive event feedback
/// (see [[rt-interrupt-speech-rule]]).
/// </summary>
internal sealed class ConvictionEvents : ISoulMarkShiftHandler
{
    internal static readonly ConvictionEvents Instance = new ConvictionEvents();

    public void HandleSoulMarkShift(ISoulMarkShiftProvider provider)
    {
        try
        {
            var shift = provider?.SoulMarkShift;
            if (shift == null || shift.Empty) return;

            var fmt = UINotificationTexts.Instance?.SoulMarksShiftFormat;
            if (fmt == null) return;
            var dir = UIUtility.GetSoulMarkDirectionText(shift.Direction)?.Text ?? shift.Direction.ToString();

            var line = TextUtil.StripRichText(string.Format((string)fmt, dir, shift.Value));
            if (!string.IsNullOrWhiteSpace(line)) Speaker.Speak(line, interrupt: false);
        }
        catch (Exception e) { Main.Log?.Log("conviction announce failed: " + e.Message); }
    }
}
