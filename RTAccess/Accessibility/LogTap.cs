using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker;                                         // Game (RealTime — unused dir; frame dedupe uses Time)
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem;   // LogThreadBase, CombatLogMessage

namespace RTAccess.Accessibility;

/// <summary>
/// The universal log narrator: taps the game's log at its single choke point —
/// <see cref="LogThreadBase"/>'s <c>AddMessage</c> — and voices every line a sighted player reads in the
/// combat/message log window, across ALL channels (combat resolution, plus life-events the mod never read
/// before: XP, items, reputation, Profit Factor, cargo, colony, Navigator resource, …). This makes the
/// game log the single source of truth for live event narration. See
/// docs/plans/unified-logging-shannon.md.
///
/// CAPTURED and VOICED are decoupled. The game itself retains the full history
/// (<c>LogThreadService</c>, uncapped, session-scoped), so the review screen reads THAT store directly —
/// this tap only decides what to SPEAK live. It voices every thread EXCEPT the ones a dedicated announcer
/// already owns (<see cref="OwnedElsewhere"/> — barks, warnings, dialogue cue/answer text), plus
/// separators — everything a sighted player reads in the log window is spoken.
/// Conviction (soul-mark) shifts are the ONE notification the game never logs, so they come from
/// <see cref="ConvictionEvents"/> instead.
///
/// Lines are stripped of TMP rich text (SPACED strip — a combat line welds its damage segment to a
/// "Critical hit!" suffix with only a tag otherwise), guarded against a same-frame double-fire, and
/// enqueued into <see cref="CombatEvents"/>' shared per-frame queue so they interleave with combat/buff/
/// lifecycle cues in arrival order and never interrupt navigation (see [[rt-interrupt-speech-rule]]).
/// </summary>
[HarmonyPatch(typeof(LogThreadBase), "AddMessage")]
public static class LogTap
{
    // Threads whose lines a dedicated announcer already voices; narrating them here too would double-speak.
    // Matched by concrete thread type Name — the namespace is NOT a reliable channel key (e.g.
    // DialogLogThread lives in the LifeEvents namespace but the Dialog channel).
    private static readonly HashSet<string> OwnedElsewhere = new HashSet<string>
    {
        "CombatLogBarkLogThread",                  // BarkEvents (overhead / subtitle barks)
        "WarningNotificationLogThread",            // WarningReader (instant refusal toasts)
        "DialogLogThread",                         // DialogueScreen voices the current cue
        "DialogHistoryLogThread",                  // DialogueScreen owns the transcript
    };
    // NOTE: the buff-application threads (Rulebook/MergeRuleCalculateCanApplyBuff) are intentionally NOT owned.
    // The game groups a multi-target application into one "group gains X" line and honours every hidden/own-self
    // filter, so we let it voice buff GAINS directly rather than re-deriving them per-unit. Buff removal/expiry
    // has no log thread and is no longer announced (sighted parity — the log never shows expiry either).

    // Same-frame exact-duplicate guard: the engine occasionally raises AddMessage twice for one event in a
    // single frame. This drops that echo WITHOUT touching legitimate cross-frame repeats (e.g. a burst of
    // identical damage lines a frame apart, which must all read).
    private static int _lastFrame = -1;
    private static string _lastLine;

    private static void Postfix(LogThreadBase __instance, CombatLogMessage newMessage)
    {
        try
        {
            if (newMessage == null) return;
            var name = __instance?.GetType()?.Name;
            if (name == null) return;

            var raw = newMessage.Message;
            var clean = string.IsNullOrWhiteSpace(raw) ? "" : TextUtil.StripRichTextSpaced(raw);

#if DEBUG
            RecordDiag(name, newMessage.IsSeparator, clean);
#endif
            if (newMessage.IsSeparator || clean.Length == 0) return;
            if (OwnedElsewhere.Contains(name)) return;

            int frame = UnityEngine.Time.frameCount;
            if (clean == _lastLine && frame == _lastFrame) return; // same-frame double-fire
            _lastLine = clean;
            _lastFrame = frame;

            CombatEvents.Instance.EnqueueLogLine(clean);
        }
        catch (Exception e) { Main.Log?.Log("log tap read failed: " + e.Message); }
    }

#if DEBUG
    // Dev-only calibration ring: the last N messages across ALL threads (name + separator flag + text),
    // so a live session can be inspected over /eval to tune which threads are signal vs noise.
    // Read via: RTAccess.Accessibility.LogTap.DumpDiag()
    public static readonly List<string> Diag = new List<string>();

    private static void RecordDiag(string thread, bool separator, string text)
    {
        Diag.Add(thread + (separator ? " <SEP>" : "") + ": " + text);
        if (Diag.Count > 400) Diag.RemoveAt(0);
    }

    public static string DumpDiag() => string.Join("\n", Diag);
#endif
}
