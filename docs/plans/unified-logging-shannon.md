# Unified message log — the game log as the single source of truth

Plan name: `docs/plans/unified-logging-shannon.md`. Sibling to the dialogue-enrichment work
(`DialogueScreen` / `DialogNotify`) it partly supersedes. Turns RTAccess's fragmented, hand-rolled
event narration into ONE tap on the game's log, and adds the review buffer that reaches sighted-player
parity.

## Context & goal

A sighted player reviews everything that happens in a scrollable **combat/message log** window
(`CombatLogVM`). A blind player currently:
- gets combat resolution live (`CombatLogReader` taps only the `.LogThreads.Combat` channel),
- gets in-dialogue notifications live (`DialogNotify`, which *reconstructs* `DialogNotificationsPCView`
  from `DialogNotificationsVM` — a mirror that can drift), and
- has **no way to review** anything after it scrolls past, and **no live cue at all** for world/life
  events (XP/items/reputation/Profit Factor/colony/etc.) that happen outside a conversation.

User's directive (2026-07-02): **read new log messages automatically and make the game log the single
source of truth** for the notifications we currently hard-code via explicit per-event subscriptions.
Decisions taken (via AskUserQuestion):
1. **Full auto-narrate** — voice all enabled channels live, with per-channel toggles + dedupe/throttle.
2. **Fold `DialogNotify` into the log** — remove its reconstruction; the log tap voices in-dialogue
   life-events too; keep only a tiny **conviction (soul-mark)** hook, since that is the ONE thing the
   game does not log.
3. **Access:** a review screen on bare **L** + a HUD "Log" button (no read-last-N key, no Alt+arrows
   buffer).

## What the understand pass established (see the workflow findings)

- **One choke point:** every logged event funnels through `LogThreadBase.AddMessage(CombatLogMessage)`
  — already Harmony-patched by `CombatLogReader`. Dropping its channel filter makes it universal.
- **The game retains history for us:** `LogThreadService.Instance` (Game-lifetime) holds ~60 threads in
  7 channels (`LogChannelType`: Common, LifeEvents, InGameCombat, TacticalCombat, AnyCombat, Dialog,
  DialogAndLife); each `LogThreadBase.AllMessages` is an **uncapped** retained list. So the review
  reads the game's own store — no copy needed: `AllThreads.SelectMany(t => t.AllMessages)
  .Where(m => !m.IsSeparator).OrderBy(m => m.Received)`.
- **`CombatLogMessage`**: `.Message` (final localized text, may hold `<color>/<b>` tags), `.Received`
  (DateTime.Now sort key), `.Tooltip` (TooltipBaseTemplate, for drill-in), `.IsSeparator`, `.Unit`.
- **Persistence:** session-only — wiped on load (`LogThreadService.Cleanup`), not serialized; SURVIVES
  area + surface↔space transitions; ONE unified store (no separate space log).
- **The 4 sighted tabs** = thread groupings: All = everything; Events = LifeEvents+DialogAndLife;
  Dialogue = Dialog+DialogAndLife; Combat = AnyCombat+InGameCombat (Common shows only under All).
- **Reference impl:** `Kingmaker.Utility.ReportCombatLogManager` already does the AllThreads-ObserveAdd +
  strip-tags pattern.
- **Not in the log** (must keep a dedicated hook or lose): **conviction/soul-mark shifts** (no thread),
  buff **expiry** (no removal thread → `CombatEvents` reconciler stays), turn **lifecycle** (polled),
  instant **refusal warnings** (`WarningReader`), **bark** speaker-name recovery (`BarkEvents`).

## Core principle

**Decouple "captured/reviewable" from "voiced live."** Everything is reviewable (read straight from
`LogThreadService`). Live narration voices every thread EXCEPT the ones a specialist announcer already
owns — otherwise the tap double-speaks dialogue cues, barks, warnings, and buff applications.

Owned-elsewhere (never voiced by the tap; matched by concrete thread type Name):
- `CombatLogBarkLogThread` → `BarkEvents`
- `WarningNotificationLogThread` → `WarningReader`
- `DialogLogThread`, `DialogHistoryLogThread` → `DialogueScreen` (cue/answer text)
- `RulebookCanApplyBuffLogThread`, `MergeRuleCalculateCanApplyBuffLogThread` → `CombatEvents` (buffs)
- `RulePerformMomentumChangeLogThread` → noise (already excluded)

High-frequency "noise" (reviewable, not voiced live by default; tunable in M3):
- `GameTimeAdvancedLogThread`, `VeilThicknessLogThread`, `AwarenessLogThread`.

Conviction stays a dedicated `ISoulMarkShiftHandler` subscriber (the only content the log lacks).

## Milestones

### M1 — the unified live tap (source-of-truth live narration)  ← this pass
- Rework `CombatLogReader` → **`LogTap`**: patch stays on `LogThreadBase.AddMessage`, but now voices
  ALL channels except the owned-elsewhere + noise sets + separators. Strip via `StripRichTextSpaced`,
  short-window same-text dedupe, enqueue into `CombatEvents`' shared per-frame queue (arrival order).
  Broaden the DEBUG Diag ring to ALL threads (name+flags+text) for M3 calibration.
- New `ConvictionEvents : ISoulMarkShiftHandler` — voices "Faith +2" etc. via
  `UINotificationTexts.SoulMarksShiftFormat` + `UIUtility.GetSoulMarkDirectionText`. Queued.
- `Main.cs`: subscribe/unsubscribe `ConvictionEvents`.
- Remove `DialogNotify.cs`; revert the `DialogueScreen` notification subscription/drain (cue-speak
  returns to its pre-#2 form). Conviction now comes from `ConvictionEvents`; life-events from `LogTap`.
- Verify: build clean; in a game, world XP/item/reputation/PF gains now read live outside dialogue;
  in dialogue they still read (from the log); conviction still reads; no double-speak of cues/barks/
  warnings/buffs.

### M2 — the review screen (parity)
- `LogReviewScreen : Screen` — child overlay (DrillMenuScreen/TooltipScreen pattern), IsActive()=>false,
  static `Open()` via `ScreenManager.Current?.PushChild`. Reads `LogThreadService` live on open.
- Channel tabs mirroring the game's 4 (All/Events/Dialogue/Combat) via `GetThreadsByChannelType`; a
  `ListContainer`/`FlowSheet` of `TextElement` rows, newest last, cap ~200/tab. Each row: `GetText()` =
  stripped line; override `GetLinkSourceText()` = RAW message so Space drills glossary links
  (reuse #4's `GlossaryLinks`→`TooltipReader`→`DrillMenuScreen`/`TooltipScreen`); tooltip factory =
  `CombatLogMessage.Tooltip`.
- Open on bare **L** (Global, `InAGame()`-gated; verify GameKeybinds vacated the game's Encyclopedia
  off L → Ctrl+L) + a HUD "Log" button in `InGameScreen` Windows region (custom opener, not a
  `ServiceWindowsType`). Reuse pre-seeded locale keys `log.title`/`log.channels`/`log.empty`/`hud.log`.

### M3 — per-channel toggles + throttle tuning
- Channel classification via a cached `LogThreadService` reverse-map (namespace is NOT channel — e.g.
  `DialogLogThread` lives in the LifeEvents namespace but the Dialog channel).
- Settings tree under a new `log` category: per-channel live toggles (Combat/Events/World) + a handful
  of per-thread sub-toggles for the noisiest, all default sensible; RT-appropriate labels (the
  pre-seeded `overlay.log.*` taxonomy is WOTR-ported and needs a remap). Read via
  `ModSettings.GetSetting<BoolSetting>`.
- Throttle/coalesce for chatty channels; calibrate the noise set with the DEBUG Diag ring.

## Risks / notes
- Chattiness is the main risk; M1 mitigates with the noise set + dedupe, M3 with toggles/throttle.
- Losing in-dialogue cue-relative ordering + fade-gating for life-events is an accepted trade of
  decision #2.
- History is volatile (per session, wiped on load) — the review must say "this session".
- Bare L: confirm no residual game action fires under focus arbitration before shipping M2.

## AI Usage Disclosure
This plan and its implementation were developed with assistance from Claude (Anthropic). All code was
reviewed and tested by the author before submission.
