# Overnight WrathAccess → RTAccess port — session report

Autonomous session (started 2026-06-30, overnight). Goal: port as much of the WrathAccess
(Pathfinder: WOTR) accessibility mod to RTAccess as possible, using subagents, testing each
feature end-to-end through the dev harness, and **deferring** anything that needs the maintainer's
ears/judgment (audio quality, voice tuning) for review.

Baseline: clean working tree at commit `69b8c20` (parallel accessible UI: menu, settings, full
new-game flow). Debug build green (0/0). Dev harness live (port 8772): `/eval /speech /gui /input
/screenshot /loadsave /health`.

## Verification model (what "tested end-to-end" means here)
I cannot hear TTS. Correctness is verified via the harness:
- `/speech?since=N` — the exact text that WOULD be spoken (the announcement is correct).
- `/gui` — the built nav tree for the active screen (structure/labels correct).
- `/input <action>` — drive navigation/activation; confirm labels + that real actions fire.
- `/eval` — read live game state to confirm an action changed it.
**Defer boundary:** audio *quality* (spatial sonar, wall tones, SAPI voice choice/rate, earcon
mix) cannot be self-verified → ported but gated off-by-default and flagged for maintainer review.

## Target backlog (priority × testability × risk)
| # | Feature | Value | Testable | Risk | Status |
|---|---------|-------|----------|------|--------|
| 1 | Speech fallback (SAPI5 + Clipboard + auto roster) | high | yes | low | in progress |
| 2 | Review buffers (Alt+arrows, UnitBuffer) | high | yes | med | research |
| 3 | DialogueScreen (+ answer/links/transcript) | high | yes | med | research |
| 4 | InGameScreen (surface HUD) | high | yes | med-high | research |
| 5 | Combat events (damage/death/buff/heal) + WarningReader | high | partial | med-high | research |
| 6 | Spatial audio (NAudio earcons/sonar) | high | NO (ears) | med | DEFER (port+gate) |
| 7 | Inventory / Journal / Character screens | med | yes | med-high | stretch |
| 8 | Tooltip/encyclopedia drill reader | high | yes | high | stretch |

## Log
- Baseline confirmed green (Debug 0/0). Dev server down (game not running) at session start.
- **#1 Speech fallback — DONE (built 0/0).** New: `Speech/ComDispatch.cs` (verbatim Win32 IDispatch COM),
  `Speech/SapiSpeech.cs` (`ISpeech` SAPI5 via ComDispatch), `Speech/ClipboardSpeech.cs`. `Speaker.Initialize`
  now an ordered roster Prism → SAPI → Clipboard (never silent). Static knobs SapiSpeech.Rate/Volume/PreferredVoice
  for a future settings hook. NEEDS: in-game verify ActiveBackend resolves + SAPI actually speaks (harness/ears).
- **#6 Audio earcons (DEFERRED, gated OFF) — DONE (built 0/0).** Added NAudio 1.10.0 (single net35 DLL, deploys
  beside prism.dll). New: `Audio/AudioMixer.cs` (shared NAudio mixer + panned one-shot), `Audio/Earcons.cs`
  (synthesized chimes: Focus/ScreenChange/Activate/Boundary/TurnStart/Error; `Enabled=false` default,
  `Earcons.Test()` to audition via /eval). One gated hook wired: `ScreenManager.SyncFocus` → `Earcons.ScreenChange()`
  (no-op unless Enabled). FOR MAINTAINER: `/eval RTAccess.Audio.Earcons.Test()` to hear the palette; flip
  `Earcons.Enabled=true` to enable screen-change cue; tune pitch/volume; then we wire the rest + sonar.
- **Research agents (4) returned full binding reports** (persisted in scratchpad/report-*.md). Then resumed each
  to IMPLEMENT its feature in parallel (disjoint new files; main session owns shared-file integration):
  - #2 Buffers → `Buffers/{Buffer,BufferManager,BufferControls,UnitBuffer}.cs` (Alt+arrows, Global category).
  - #3 Dialogue → `Screens/{DialogueScreen,BookEventScreen}.cs` + `UI/Proxies/DialogAnswerButton.cs` (Layer 15).
  - #4 InGame HUD → `Screens/InGameScreen.cs` + re-gate `ExplorationNav/TileExplorer/LandmarkNav/PartyHotkeys`
    to mouse mode (via `InGameScreen.ExplorationActive`). IsActive = RootUiContext.IsSurface.
  - #5 Combat events → `Accessibility/{CombatEvents,WarningReader}.cs` (event templates already in ui.json).
- PENDING INTEGRATION (main session, after agents deliver): register screens in ScreenManager.Initialize;
  wire buffer Alt+arrow actions in InputBindings + BufferManager.RegisterDefaults in Main.Load; subscribe
  CombatEvents/WarningReader + Tick in Main; build; fix; launch once; harness-test the batch.
- Note: DialogCuePatch.AutoReadEnabled already false → DialogueScreen won't double-read (no change needed).
</content>
