using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem;

namespace RTAccess.Accessibility;

/// <summary>
/// Combat-log narrator (A2). Taps the game's combat log at its single choke point —
/// <see cref="LogThreadBase"/>'s <c>AddMessage</c> — so every line a sighted player reads in the combat
/// log window (attack rolls: hit/miss/dodge/parry/crit, damage, cover, healing, deaths, buffs) is voiced.
///
/// The combat log is the AUTHORITATIVE source for combat resolution: <see cref="CombatEvents"/> no longer
/// voices damage/heal/death off the EventBus (removed), so those come only from here — richer than the old
/// hand-rolled lines (damage type, misses, crits). Buffs stay in <see cref="CombatEvents"/> because the log
/// records buff APPLICATION but has no removal thread, so its reconciler is the only source that can also
/// announce expiry; the buff-apply log threads are therefore in <see cref="ExcludedThreads"/> to avoid a
/// double on gains.
///
/// Only the combat channels are read: threads whose namespace ends in <c>.LogThreads.Combat</c> (the
/// AnyCombat + InGameCombat channels — see <see cref="LogThreadService"/>). Barks (<c>.LogThreads.Dialog</c>)
/// and warnings (<c>.LogThreads.Common</c>) are handled elsewhere (<see cref="BarkEvents"/> / WarningReader).
/// Separator/divider messages (round markers) are skipped — the round cue comes from the lifecycle poll in
/// <see cref="CombatEvents.PollLifecycle"/>. Momentum chatter is excluded as noise. Lines are stripped of TMP
/// rich text and enqueued into <see cref="CombatEvents"/>' shared per-frame queue so they interleave with
/// lifecycle/buff cues in arrival order.
/// </summary>
[HarmonyPatch(typeof(LogThreadBase), "AddMessage")]
public static class CombatLogReader
{
    // Combat-channel threads all live in this namespace suffix; everything else (dialog barks, common
    // warnings, life-events, colony chatter) is read by other subscribers or not at all.
    private const string CombatNamespaceSuffix = ".LogThreads.Combat";

    // Combat-channel threads whose per-event chatter is noise or is owned elsewhere (matched by concrete
    // thread type name — see LogThreadService):
    //  • momentum fires on every kill and turn start — verbose, low per-event value (the momentum VALUE is
    //    better surfaced on the HUD than narrated each change);
    //  • buff application/calc is owned by CombatEvents' deduped gain/loss reconciler, which — unlike the log —
    //    can also announce expiry (the log has no buff-removal thread), so keeping buffs there is one
    //    consistent voice for both directions.
    private static readonly HashSet<string> ExcludedThreads = new HashSet<string>
    {
        "RulePerformMomentumChangeLogThread",
        "RulebookCanApplyBuffLogThread",
        "MergeRuleCalculateCanApplyBuffLogThread",
    };

    private static void Postfix(LogThreadBase __instance, CombatLogMessage newMessage)
    {
        try
        {
            if (newMessage == null) return;
            var type = __instance?.GetType();
            var ns = type?.Namespace;
            if (ns == null || !ns.EndsWith(CombatNamespaceSuffix, StringComparison.Ordinal)) return;

            var raw = newMessage.Message;
            // Spaced strip (not the tight StripRichText): combat-log lines join segments like a damage line
            // and its "Critical hit!" suffix with only a rich-text tag, which must not weld into one word.
            var clean = string.IsNullOrWhiteSpace(raw) ? "" : TextUtil.StripRichTextSpaced(raw);
            bool excluded = ExcludedThreads.Contains(type.Name);

#if DEBUG
            RecordDiag(type.Name, newMessage.IsSeparator, excluded, clean);
#endif
            if (excluded || newMessage.IsSeparator || clean.Length == 0) return;
            CombatEvents.Instance.EnqueueLogLine(clean);
        }
        catch (Exception e) { Main.Log?.Log("combat log read failed: " + e.Message); }
    }

#if DEBUG
    // Dev-only calibration ring: the last N combat-channel messages seen (thread name + separator flag +
    // text), so a live fight can be inspected over /eval to tune which threads are signal vs noise.
    // Read via: RTAccess.Accessibility.CombatLogReader.DumpDiag()
    public static readonly List<string> Diag = new List<string>();

    private static void RecordDiag(string thread, bool separator, bool excluded, string text)
    {
        Diag.Add(thread + (separator ? " <SEP>" : "") + (excluded ? " <EXCL>" : "") + ": " + text);
        if (Diag.Count > 300) Diag.RemoveAt(0);
    }

    public static string DumpDiag()
    {
        return string.Join("\n", Diag);
    }
#endif
}
