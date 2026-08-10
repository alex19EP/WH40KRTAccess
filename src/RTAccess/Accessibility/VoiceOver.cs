using HarmonyLib;
using Kingmaker.Localization;                 // LocalizedString.GetVoiceOverSound
using Kingmaker.Localization.Enums;           // Locale
using Kingmaker.Settings;                     // SettingsRoot.Sound (the game's own volume sliders)
using Kingmaker.UI.Sound;                     // VoiceOverPlayer — the single choke point for every voiced line
using Kingmaker.UI.Sound.Base;                // VoiceOverStatus
using RTAccess.Settings;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Knows which lines the GAME is already saying out loud, so the mod does not read them a second time on top
/// of the voice-over. Reported by testers as the single loudest source of noise: every voiced conversation
/// played twice at once (the actor and the screen reader), so the player spent the scene cancelling speech.
///
/// The game funnels every voiced line — dialogue cues, book-event passages, companion banter, cutscene
/// subtitles — through <see cref="VoiceOverPlayer.PlayVoiceOver(string, GameObject)"/>, which returns a
/// <see cref="VoiceOverStatus"/> or NULL when nothing actually started (no recorded take for the line, or the
/// Wwise event failed to post). We postfix both string overloads and remember the outcome per sound key, so
/// "will the player hear this line spoken?" is answered from what the engine really did, not from a guess —
/// a line whose take is missing still gets read aloud by the mod.
///
/// This SUPPRESSES SPEECH ONLY, never text: every cue, passage row and bark keeps its full label in the
/// graph, so arrowing onto a line always reads it. What stays spoken automatically is the part the actors
/// never recorded — the skill-check result the sighted player reads beside the portrait while the voice-over
/// runs (see <see cref="DialogText.BuildMechanicLine"/>).
///
/// Two gates keep it from firing where the voice-over is NOT a substitute for reading:
/// <list type="bullet">
/// <item>The recorded voice-over is <b>English only</b> (the game ships a single <c>English(US)</c> Wwise
/// language folder) while its text is localized to nine languages. In any other locale the voice is not
/// saying what the player's screen says, so nothing is suppressed.</item>
/// <item>The game's own voice sliders. At zero volume there is no voice-over to defer to.</item>
/// </list>
/// Off switch: the <c>speech.voiced_lines</c> mod setting.
/// </summary>
internal static class VoiceOver
{
    // Outcome of the LAST post per sound key: true = a voice actually started. Re-posting a key overwrites,
    // so a one-off failure (a destroyed speaker object) self-corrects the next time the line plays. The cap is
    // a runaway guard only — it sits above the ~5k voiced keys the whole game ships, and a full clear on
    // overflow is harmless anyway (a cleared key just reads as "assume it plays" until its next post).
    private const int MaxTrackedKeys = 8192;
    private static readonly Dictionary<string, bool> Started = new Dictionary<string, bool>();

    // The most recent post, for callers that get no line identity — bark handlers receive a display string,
    // not the LocalizedString, so they correlate by frame: BarkPlayer posts the voice-over and raises the
    // bark event inside one synchronous call, hence the same frame.
    private static int _lastFrame = -1;
    private static bool _lastStarted;

    /// <summary>Record what <see cref="VoiceOverPlayer"/> actually did with a line. Called from the patches.</summary>
    internal static void Note(string sound, VoiceOverStatus status)
    {
        bool started = status != null;
        _lastFrame = Time.frameCount;
        _lastStarted = started;
        if (string.IsNullOrEmpty(sound)) return;
        if (Started.Count >= MaxTrackedKeys) Started.Clear();
        Started[sound] = started;
#if DEBUG
        RecordDiag(sound + (started ? "" : " <FAILED>"));
#endif
    }

#if DEBUG
    // Dev-only ring of the voice-overs the engine actually posted, in order. Pair it with speech_log.txt to
    // tell a DEFERRED line (key here, silence there — working) from a lost one (neither).
    // Read via: RTAccess.Accessibility.VoiceOver.DumpDiag()
    public static readonly List<string> Diag = new List<string>();

    private static void RecordDiag(string line)
    {
        Diag.Add(line);
        if (Diag.Count > 200) Diag.RemoveAt(0);
    }

    public static string DumpDiag() => string.Join("\n", Diag.ToArray());
#endif

    /// <summary>
    /// True when the game is speaking <paramref name="text"/> aloud, in the player's own language, and the mod
    /// should therefore stay quiet about it. False for every unvoiced line, so text-only conversations read
    /// exactly as before.
    /// </summary>
    public static bool Covers(LocalizedString text)
    {
        if (text == null || !Enabled) return false;
        string sound;
        try { sound = text.GetVoiceOverSound(); }
        catch { return false; }
        if (string.IsNullOrEmpty(sound)) return false; // no recorded take — the mod is the only voice
        // Known-failed post (missing event / no sound object): nothing was heard, so read it.
        return !Started.TryGetValue(sound, out bool started) || started;
    }

    /// <summary>
    /// True when a voice-over started during THIS frame — the correlation a bark handler needs, since it is
    /// handed a display string with no line identity. Exact for barks: <c>BarkPlayer</c> posts the voice-over
    /// and raises the bark event in one synchronous call. The theoretical miss is an unvoiced bark raised in
    /// the same frame as some other line's voice-over, which then goes unspoken; the game's own log keeps it,
    /// so it is still reachable in the log review screen.
    /// </summary>
    public static bool CoveredThisFrame => Enabled && _lastStarted && _lastFrame == Time.frameCount;

    /// <summary>The mod setting, the text/voice language match, and the game's voice sliders, all together.</summary>
    private static bool Enabled
        => (ModSettings.GetSetting<BoolSetting>("speech.voiced_lines")?.Get() ?? true) && VoiceMatchesText && Audible;

    // The voice-over exists in English only; in any other text locale it is not reading what is on screen.
    // (dev is the designers' English pack.) Read live — the language can be changed from the game's settings.
    private static bool VoiceMatchesText
    {
        get
        {
            try
            {
                var locale = LocalizationManager.Instance?.CurrentLocale;
                return locale == Locale.enGB || locale == Locale.dev;
            }
            catch { return false; }
        }
    }

    // Zeroing any slider in the chain means the player hears no voice-over — so the mod must keep reading.
    // The dialogue slider is included deliberately: a player who muted dialogue voices wants the text.
    private static bool Audible
    {
        get
        {
            try
            {
                var s = SettingsRoot.Sound;
                return s.VolumeMaster.GetValue() > 0f
                    && s.VolumeVoices.GetValue() > 0f
                    && s.VolumeVoicesDialogues.GetValue() > 0f;
            }
            catch { return false; }
        }
    }
}

/// <summary>Records dialogue / book-event / bark voice-overs (the <see cref="GameObject"/>-targeted overload).</summary>
[HarmonyPatch(typeof(VoiceOverPlayer), nameof(VoiceOverPlayer.PlayVoiceOver), new[] { typeof(string), typeof(GameObject) })]
internal static class VoiceOverPlayerGameObjectPatch
{
    private static void Postfix(string voiceOverSound, VoiceOverStatus __result) => VoiceOver.Note(voiceOverSound, __result);
}

/// <summary>Records voice-overs posted without a world target (subtitle barks, book-event passages).</summary>
[HarmonyPatch(typeof(VoiceOverPlayer), nameof(VoiceOverPlayer.PlayVoiceOver), new[] { typeof(string), typeof(MonoBehaviour) })]
internal static class VoiceOverPlayerMonoBehaviourPatch
{
    private static void Postfix(string voiceOverSound, VoiceOverStatus __result) => VoiceOver.Note(voiceOverSound, __result);
}
