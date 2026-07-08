using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;
using UnityEngine;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Long-lived <see cref="Kingmaker.PubSubSystem.Core.EventBus"/> subscriber that voices "barks" — the short
/// spontaneous lines characters and objects emit outside formal dialogue. Subscribed once at mod load and
/// unsubscribed at unload (see <see cref="Main"/>). No Harmony patch needed: every bark funnels through
/// <c>BarkPlayer</c>, which raises these PubSub interfaces. See [[rt-bark-system]].
///
/// Scope (decided with the user): match what a SIGHTED player READS, not the voice-only combat chatter.
/// <list type="bullet">
/// <item><see cref="IBarkHandler"/> — on-screen overhead speech bubbles (the <c>ShowOnScreen</c> barks, plus
/// star-system exploration barks and named object barks).</item>
/// <item><see cref="ISubtitleBarkHandler"/> — bottom-of-screen subtitles (text arrives pre-formatted as
/// "Speaker: text").</item>
/// </list>
/// We deliberately do NOT subscribe <c>ICombatLogBarkHandler</c>: it carries the frequent voice-only quips
/// (Pain/CriticalHit/etc.) the game already voices, and skipping it also removes the only cross-channel
/// duplicate (a <c>ShowOnScreen</c> bark raises both that and <see cref="IBarkHandler"/>). Bubbles and
/// subtitles never overlap, so no cross-interface dedupe is required.
///
/// A speaker-name prefix ("Name: text") is added when known (mirrors the combat log and subtitles). Speech is
/// queued (interrupt:false) per [[rt-interrupt-speech-rule]]. A light same-text window dedupe mirrors the
/// game's own <c>CombatLogBarkLogThread</c> so a repeated idle line isn't spoken twice in quick succession.
/// </summary>
internal sealed class BarkEvents : IBarkHandler, ISubtitleBarkHandler
{
    internal static readonly BarkEvents Instance = new BarkEvents();

    private const double DedupeWindowSeconds = 2.0;
    private string _lastSpoken;
    private double _lastSpokenAt = -100.0;

    // IBarkHandler — overhead speech bubbles. The plain variant carries no name, so recover the speaking
    // entity from the event invoker (valid here: global subscribers run inside the invoker context).
    public void HandleOnShowBark(string text) => Speak(text, SpeakerName());

    public void HandleOnShowBarkWithName(string text, string name, Color nameColor) => Speak(text, name);

    public void HandleOnShowLinkedBark(string text, string encyclopediaLink) => Speak(text, SpeakerName());

    // ISubtitleBarkHandler — subtitle text already includes the "Speaker: " prefix, so don't add another.
    public void HandleOnShowBark(string text, float duration) => Speak(text, null);

    // Shared by both interfaces (identical signature); nothing to announce when a bark is dismissed.
    public void HandleOnHideBark() { }

    private static string SpeakerName()
    {
        try { return EventInvokerExtensions.MechanicEntity?.Name; }
        catch { return null; }
    }

    private void Speak(string text, string speakerName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var clean = TextUtil.StripRichTextSpaced(text);
            if (string.IsNullOrEmpty(clean)) return;

            var line = string.IsNullOrWhiteSpace(speakerName) ? clean : speakerName.Trim() + ": " + clean;

            // Drop a repeat of the exact same line within a short window (idle barks, near-simultaneous duplicates).
            var now = Game.Instance.TimeController.RealTime.TotalSeconds;
            if (line == _lastSpoken && now - _lastSpokenAt < DedupeWindowSeconds) return;
            _lastSpoken = line;
            _lastSpokenAt = now;

            Speaker.Speak(line, interrupt: false);
        }
        catch (Exception e)
        {
            Main.Log?.Log("bark announce failed: " + e.Message);
        }
    }
}
