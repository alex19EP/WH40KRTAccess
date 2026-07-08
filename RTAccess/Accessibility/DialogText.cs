using Kingmaker;
using Kingmaker.Blueprints.Root;               // UIConfig.Instance.DialogColors
using Kingmaker.Code.UI.MVVM.VM.Dialog.Dialog;
using Kingmaker.UI.Common;                      // UIUtility.SkillCheckText

namespace RTAccess.Accessibility;

/// <summary>
/// Single source of the spoken dialogue cue line: speaker name + skill-check result + cue text, rich-text
/// stripped. Used by the live <see cref="Screens.DialogueScreen"/>. The speaker name is prepended only when
/// <c>includeSpeaker</c> is set — the caller decides, so a run of cues from the same NPC isn't re-named on
/// every line. The skill-check result (when a cue rolled a check) is always prepended before the cue text,
/// mirroring how the sighted cue view draws it.
/// </summary>
internal static class DialogText
{
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

        // The skill-check RESULT ("Failed a Persuasion check") is a runtime prefix the game's cue view
        // composes from the cue's SkillChecks (UIUtility.SkillCheckText) — it is NOT part of RawText, so
        // the narrative line alone drops it. Prepend it the way the game draws it, before the cue text.
        var check = BuildSkillCheckPrefix(cue);
        if (!string.IsNullOrEmpty(check)) line = check + " " + line;

        if (!includeSpeaker) return line;

        var speaker = dc?.CurrentSpeakerName;
        return string.IsNullOrWhiteSpace(speaker) ? line : speaker + ". " + line;
    }

    /// <summary>
    /// The rich-text-stripped skill-check result the game prepends to a cue after a roll (e.g. "Failed a
    /// Persuasion check"), or null when the cue rolled no check. Honors the game's own
    /// <c>ShowSkillcheckResult</c> toggle internally (returns "" when off — matching the answer side, which
    /// gates on the same setting). Guarded: a formatting failure must never swallow the cue line itself.
    /// </summary>
    private static string BuildSkillCheckPrefix(CueVM cue)
    {
        var checks = cue?.SkillChecks;
        if (checks == null || checks.Count == 0) return null;
        var colors = UIConfig.Instance?.DialogColors;
        if (colors == null) return null;
        try { return Clean(UIUtility.SkillCheckText(checks, colors)); }
        catch { return null; }
    }

    private static string Clean(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var clean = TextUtil.StripRichTextSpaced(raw);
        return string.IsNullOrEmpty(clean) ? null : clean;
    }
}
