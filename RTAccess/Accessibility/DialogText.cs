using System.Text.RegularExpressions;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog;

namespace RTAccess.Accessibility;

/// <summary>
/// Single source of the spoken dialogue cue line: speaker name + cue text, rich-text stripped. Used by the
/// injected cue <see cref="VirtualNavItem"/> (and the OFF-by-default <see cref="DialogCuePatch"/> fallback).
/// The speaker name is prepended only when <c>includeSpeaker</c> is set — the caller decides, so a run of
/// cues from the same NPC isn't re-named on every line (see <see cref="DialogNavAugmentor"/>).
/// </summary>
internal static class DialogText
{
    private static readonly Regex RichText = new Regex("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Build the spoken cue line from a <see cref="CueVM"/>, falling back to the controller's current cue when
    /// the VM is null. Prepends "Speaker. " only when <paramref name="includeSpeaker"/> is true. Returns null
    /// when there is no readable text.
    /// </summary>
    public static string BuildCueLine(CueVM cue, bool includeSpeaker = true)
    {
        var dc = Game.Instance?.DialogController;

        var raw = cue?.RawText;
        if (string.IsNullOrEmpty(raw)) raw = dc?.CurrentCue?.DisplayText;
        var line = Clean(raw);
        if (string.IsNullOrEmpty(line)) return null;

        if (!includeSpeaker) return line;

        var speaker = dc?.CurrentSpeakerName;
        return string.IsNullOrWhiteSpace(speaker) ? line : speaker + ". " + line;
    }

    private static string Clean(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return Whitespace.Replace(RichText.Replace(raw, " "), " ").Trim();
    }
}
