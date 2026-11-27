---
name: rt-message-log
description: "message-log architecture — game log as single source of truth (LogTap universal AddMessage tap + LogReviewScreen), DialogNotify folded out, conviction the one exception"
metadata: 
  node_type: memory
  type: project
  originSessionId: f071ec19-5b6f-42fc-a163-3e9ae955e7a4
---

Shipped the message-log feature making the game's combat/message log the **single source of truth**
for event narration (plan: docs/plans/unified-logging-shannon.md). Decided with the user 2026-07-02.

**Core principle: decouple CAPTURED from VOICED.**
- The game retains all history itself: `LogThreadService.Instance` (Game-lifetime, `Kingmaker.UI.Models.Log.CombatLog_ThreadSystem`) holds ~60 `LogThreadBase` threads in 7 `LogChannelType` buckets; each `.AllMessages` is uncapped, session-only (wiped on load via `Cleanup`, NOT serialized), survives surface↔space, one unified store. `CombatLogMessage`: `.Message` (final localized, markup), `.Received` (DateTime sort key), `.Tooltip`, `.IsSeparator`.
- **`Accessibility/LogTap.cs`** (was `CombatLogReader`): the one Harmony postfix on `LogThreadBase.AddMessage`. Now voices ALL channels (not just combat) into `CombatEvents`' per-frame queue, EXCEPT `OwnedElsewhere` (barks→BarkEvents, warnings→WarningReader, DialogLogThread/DialogHistoryLogThread→DialogueScreen). The `Noise` mute set was REMOVED 2026-07-03 at the user's direction (sighted players read those lines, so we voice them) — GameTimeAdvanced/VeilThickness now speak live; if Veil churn floods TTS in playtest, the fix is a per-channel settings toggle, not a hardcoded set. Same-frame dup guard.

**Duplication-removal pass 2026-07-05 (user law: "read exactly what the game displays; don't filter for spam").** Removed every custom reader that re-derived something the game already writes to its log, so the log is the sole source for those:
- **Buff gain/loss reconciler DELETED** from `CombatEvents` (was `IUnitBuffHandler`). Un-suppressed `RulebookCanApplyBuffLogThread` + `MergeRuleCalculateCanApplyBuffLogThread` → gains now come from the log, CORRECTLY GROUPED (`MergeRule…` emits one "Group gains X" line for a multi-target apply; the reconciler spoke one line per unit = the batching bug). Bonus: log path also fixes immunity ("X is immune to Y"), trauma/wound wording, and `ShowInLogOnlyOnYourself`/hidden filtering the reconciler got wrong. **Buff EXPIRY no longer voiced** — the game has NO buff-removal log thread; it's only the portrait icon vanishing (a gauge/icon STATE, not a message). The reconciler synthesized it by watching state; dropped per user (log-parity).
- **Momentum readiness cues DELETED** from `CombatEvents.PollThresholds`; un-suppressed `RulePerformMomentumChangeLogThread` → every momentum change now voiced live from the log (with reason). **"Heroic act ready"/"Desperate measure ready" no longer voiced** — not a log line, only a lit gauge indicator; K-gauge (HudGauges) still reads it on demand.
- **Veil band cues DELETED** from `PollThresholds` — `VeilThicknessLogThread` (never owned) already voices every change with exact value.
- **`InteractionEvents.cs` (lock-pick) DELETED** — `PickLockLogThread` (never owned) already voices it; the custom reader double-spoke. Log line names the actor, not the object.
- **`WarningReader` KEPT + its save/load noise filter REMOVED.** KEY INSIGHT: a warning is an on-screen TOAST; `addToLog` gates whether it ALSO enters the log. Many refusals (loot collect-all, augment/cargo refusals, iron-man save/load restrictions) are `addToLog:false` → shown on screen but NEVER in the log, so `WarningReader` (reads the toast, the complete set) is the ONLY reader for them. Its thread stays owned/suppressed so `addToLog:true` warnings aren't read twice (toast + log). Cutting it would have made us read LESS. See `GameLogEventWarningNotification.EventsHandler` (gates on addToLog).
- Dead locale keys removed: `event.buff_gained/lost` + 6 pre-dead `event.*`, `interact.lock_*`, `combat.veil_rising/falling`, `combat.momentum_now`. `CombatEvents` is no longer an EventBus subscriber (poll/tap-only; removed from Main.cs Subscribe/Unsubscribe).
- **Net "stopped reading" = exactly two, both gauge/icon STATES the game never writes as a message:** buff expiry, momentum readiness. Everything the game shows as a log line or toast is now read. Restoring either needs a minimal state-watcher (the log can't provide them). Compile-clean; not yet ear-tested in-game.
- **`Screens/LogReviewScreen.cs`**: child overlay (DrillMenuScreen pattern, IsActive()=>false, `Open()` via PushChild). Reads `LogThreadService` directly (no copy). Channel Bar mirrors the game's 4 tabs (All/Events/Dialogue/Combat via `GetThreadsByChannelType`); messages NEWEST-FIRST, cap 200; each row a `LogRow : TextElement` (spoken = spaced-stripped; `GetLinkSourceText()` = raw for glossary drill-in; tooltip = msg.Tooltip). Opened on **bare L** (InputBindings Global+InAGame) + HUD "Log" button (InGameScreen Windows region).

**The one exception: conviction (soul-mark) shifts are NOT logged** (no thread/GameLogEvent), so they can't ride LogTap — **`Accessibility/ConvictionEvents.cs`** (`ISoulMarkShiftHandler`, subscribed in Main) voices them via `UINotificationTexts.SoulMarksShiftFormat`.

**Consequence: `DialogNotify` (the #2 in-dialogue notifications) was REMOVED** and its DialogueScreen hooks reverted — in-dialogue XP/items/reputation/PF now come from LogTap, conviction from ConvictionEvents. See [[rt-dialogue]], [[rt-bark-system]] (sibling passive streams).

Remaining (M3, deferred): per-channel live toggles + throttle/coalesce tuning (calibrate the Noise set via LogTap's DEBUG Diag ring, dumped over /eval). Not yet hand-verified in-game as of this writing; watch for bare-L colliding with the game Encyclopedia and channel chattiness.
