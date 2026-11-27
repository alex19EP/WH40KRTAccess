---
name: rt-bark-system
description: "How RT's bark/quip/subtitle system routes text, and the exact EventBus hook set to read every bark for TTS accessibility"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 2788251e-002b-426e-aa5e-38bcc34ebb35
---

RT "barks" = short spontaneous lines (overhead speech bubbles, combat quips, companion banter, subtitles, object/interaction lines). For blind accessibility we read the text via TTS. See [[rt-bark-system]] context in [[rt-speechmod-reference]] (SpeechMod patches `BarkPlayer.Bark`/`BarkExploration`) and [[rt-a11y-architecture-verdict]] (prefer EventBus over Harmony where possible).

**Central funnel:** `Kingmaker.Code.UI.MVVM.VM.Bark.BarkPlayer` (static) — `Bark(...)` (3 overloads), `BarkSubtitle(...)` (4), private `BarkExploration(...)` (2). Nearly all bark producers call it: `ContextActionShowBark`, `ShowBark` GameAction, `BarkBanterPlayer` (companion banter — replays line-by-line through `BarkPlayer.Bark`, so banter needs NO separate hook), cutscene `CommandBarkUnit`/`CommandBarkEntity`.

**Bark → EventBus routing (the non-obvious part):**
- `BarkPlayer.Bark(entity, string,...)` for a VISIBLE entity raises `ICombatLogBarkHandler.HandleOnShowBark(text)` DIRECTLY, then constructs a `BarkHandle` which raises `IBarkHandler.HandleOnShowBark/WithName/LinkedBark` (the on-screen bubble). [surface/default mode → BOTH fire]
- StarSystem mode + entity barks → `BarkExploration` → `BarkHandle` → `IBarkHandler` ONLY (no ICombatLogBark).
- entity==null + overrideName (object barks) → `BarkHandle` → `IBarkHandler` ONLY.
- `BarkSubtitle` → `SubtitleBarkHandle` → `ISubtitleBarkHandler.HandleOnShowBark(text, duration)` ONLY. Subtitle text is pre-formatted `"Speaker: text"`.
- **GOTCHA — combat quips (`UnitBarksManager.Play`, the `UnitAsksComponent.Bark` system: Aggro/Pain/Death/CriticalHit/OutOfAmmo/space-combat reactions/etc.):** if `wrapper.Bark.ShowOnScreen` → `BarkPlayer.Bark` (both events); ELSE raises `ICombatLogBarkHandler` ONLY (no BarkHandle, so `IBarkHandler` never fires). So **`IBarkHandler` alone MISSES voiced no-bubble quips.**

**Conclusion — to capture EVERY bark, subscribe globally to all three:** `IBarkHandler` + `ICombatLogBarkHandler` + `ISubtitleBarkHandler` (all in `Kingmaker.PubSubSystem`). Overlap: surface/ShowOnScreen barks fire IBarkHandler AND ICombatLogBark → must DEDUPE by text within a short window (the game's own `CombatLogBarkLogThread` keeps a 15-entry dedupe queue — model to copy). Pattern = a global `EventBus.Subscribe(Instance)` object like `ExplorationEvents` (entity-targeted events reach global subscribers: `EventBus.RaiseEvent<T,TInvoker>` raises to `GlobalSubscribers` first, then entity-bound; recover the speaker via `EventInvokerExtensions.Entity`). NO Harmony patch needed for reading.

**SHIPPED v1 (2026-06-06, `RTAccess/Accessibility/BarkEvents.cs`):** global `EventBus.Subscribe(BarkEvents.Instance)` (wired in `Main.Load`/`OnUnload` next to `ExplorationEvents`) implementing `IBarkHandler` + `ISubtitleBarkHandler` ONLY. Decisions made WITH the user: (a) scope = "bubbles + subtitles" — match what a SIGHTED player READS; deliberately NOT subscribing `ICombatLogBarkHandler` (the frequent voice-only Pain/CriticalHit quips are left to the game's own VO, and skipping it removes the only cross-channel duplicate). (b) "always speak" — no VO suppression, so pure event sub, NO Harmony patch. (c) speaker-name prefix "Name: text" via `EventInvokerExtensions.MechanicEntity?.Name` for the no-name `HandleOnShowBark`; the WithName variant uses its arg; subtitles already carry the prefix so none added. Light 2s same-line dedupe; strips `<...>` rich-text; queued (interrupt:false). One `HandleOnHideBark()` satisfies both interfaces (no-op). Builds clean. VERIFIED working in-game (2026-06-06).

**Design forks (the original analysis; (1) below is still relevant if scope is later widened):** (1) Voice-acted barks — many barks pass `playVoiceOver` and play actor VO; speaking them via TTS doubles audio. The EVENT path can't see the voiceOver flag; only patching `BarkPlayer` (SpeechMod's approach) exposes it. So if VO-suppression matters, patch `BarkPlayer.Bark`/`BarkExploration`/`BarkSubtitle` (proven to bind on this build per [[rt-speechmod-reference]]) instead of/alongside events. (2) Noise — combat quips fire very frequently (Pain/CriticalHit per hit); game has its own `SettingsRoot.Sound.VoicedAskFrequency` throttle. Consider speaking only `ShowOnScreen` bubbles + subtitles by default and treating voiced quips as opt-in. (3) Speaker-name prefix ("Name: text"). Interrupt: default queue (interrupt:false) per [[rt-interrupt-speech-rule]].
