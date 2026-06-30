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

## Wave-1 integration build + in-game verification (committed: branch `overnight-pathfinder-port`, 569fab2)
Full integration build **0 errors / 0 warnings on the first pass** (all ~12 new files; the
research-against-decompiled approach meant zero signature mismatches). Deployed, launched, and
driven through the dev harness. Results:

- **Speech fallback** ✅ `/eval Speaker.ActiveBackend` → `Prism (NVDA)` (roster picks Prism; SAPI/
  Clipboard sit behind it for non-screen-reader users).
- **Review buffers** ✅ `/input buffer.next` → "Selected unit. Багардор" → "22 of 22 hit points" →
  "absorption 0 percent, deflection 0, dodge 65 percent, parry 0 percent" (live rule-computed) →
  switch → "Target is empty." Both axes (Alt+L/R buffers, Alt+U/D lines) work.
- **InGameScreen HUD** ✅ `/loadsave` → screen=`ctx.ingame`; `/gui` shows Status/Party/Combat/Windows;
  Tab nav reads "Party, list, Багардор, 22 of 22 wounds, selected, 1 of 1"; Windows region lists
  Inventory/Character/Journal/Map/Encyclopedia. Combat region empty out of combat (correct).
- **Service-window open** ✅ Inventory + Encyclopedia open via the HUD button / VM once in-world
  (`CurrentWindow=Inventory`, `invVM=True`). (Initially blocked only by the loading screen — see below.)
- **MessageBoxScreen** ✅ (incidental) the startup DLC popup rendered as text + Ок/Отменить and
  dismissed via `OnAcceptPressed`.
- **WarningReader** ✅ raised `IWarningNotificationUIHandler.HandleWarning("RTAccess warning test")`
  → spoken. Validates the EventBus-subscriber mechanism.
- **CombatEvents** ✅ raised `IUnitDeathHandler.HandleUnitDeath(Багардор)` (alive) → "Багардор is
  down" (correct alive→downed branch + ally classification + `event.downed` template + per-frame flush).
- **Audio earcons** — built/gated off; not auditioned (avoided playing tones while the maintainer sleeps).
- **DialogueScreen / BookEventScreen** — compile + binding + registration verified; `IsActive` resolves
  without throwing. Full cue/answer runtime flow not exercised (needs a live conversation) → verify in play.

### IMPORTANT FINDING — "Press any key" loading screen is an unannounced barrier
After every area load the game shows a **"Нажмите любую клавишу" (Press any key)** screen
(`LoadingProcess.IsLoadingScreenActive` stays true, `IsBlockedFullScreenUIType()` true → service
windows refuse to open) until a raw key is pressed. Confirmed via `/screenshot`. The mod does NOT
break loading — a real keypress dismisses it fine — but a blind player gets **no audible prompt**, so
they can be stuck after every transition. RECOMMENDED follow-up: detect this state and announce
"Press any key to continue" (and it's already dismissable by any key). Not yet built (needs a small
investigation of the loading-screen view's waiting-for-input flag). The harness `/loadsave` leaves
this screen up (it was flagged "not yet exercised"); driving the menu Continue button hits the same
screen — both need the keypress.

## Wave 2 — service-window screens — DONE & VERIFIED IN-GAME (committed f6b55e3)
Build 0/0 (one integration fix: CharacterInfoScreen `StatType` is `Kingmaker.EntitySystem.Stats.Base`,
not `Kingmaker.Enums`). All three verified end-to-end with real prologue character data:
- **InventoryScreen** (+ ProxyInventoryItem/ProxyEquipSlot, Layer 10) ✅ — equipment doll read live
  ("Primary hand: Стаб-револьвер", all empty slots), Load 0/12800, Money 0, empty stash. Equip via
  EventBus IInventoryHandler.TryEquip; context menu via ChoiceSubmenuScreen; unequip via InventoryHelper.
- **JournalScreen** (+ ProxyJournalQuest, Layer 10) ✅ — quest list + detail: "По праву крови, radio
  button, selected, active, updated" + heading/description/active-objective in the detail pane.
- **CharacterInfoScreen** (Layer 10, live off SelectedUnitInUI) ✅ — characteristics + skills as per-stat
  TreeGroups; **the modifier-breakdown drill-in works** (Сила 45 → expand → "Мир смерти +5, Развитие
  Силы +10"; Дальний Бой 35 → "Комиссар +5"); wounds/defenses; careers (Воин rank 1). Nav verified:
  Tab→Characteristics→expand stat→reads the modifier source.
All resolve via the Surface-OR-Space ServiceWindowsVM resolver; `ScreenName=>null` so ServiceWindowAnnounce
owns the "Inventory"/"Journal"/"Character" lead-in (confirmed no double-announce). Plus the
**LoadingScreenAnnounce** ("Press any key to continue.") verified firing on the post-load prompt.

### Minor polish findings (not blocking; for the maintainer)
- A few defensive StatTypes show raw enum names (`WarhammerInitialAPBlue/Yellow`, `Resolve`,
  `Initiative`) where `LocalizedTexts.Stats.GetText` has no localized string — could map those to
  friendly names. The saves (SaveFortitude/Reflex/Will) localize to the same words as their base
  characteristics (Toughness/Agility/Will), so they look duplicated in "Wounds and defenses".
- **Full-screen-UI FocusMode trap:** if a full-screen UI the mod doesn't own opens (e.g. the DLC mod
  manager, which the startup DLC popup's "OK" opens), FocusMode's keyboard suppression blocks the game's
  own Escape, and the mod has no screen for it → the user must toggle FocusMode off (Ctrl+Shift+A) to
  close it. Worth either an EscMenu/DLC screen or a "close any unknown full-screen UI" fallback.

## Wave 3 — EscMenu + Save/Load — DONE & VERIFIED IN-GAME (committed 50986d2)
- **EscMenuScreen** (CommonVM.EscMenuContextVM.EscMenu, Layer 20) ✅ — "Game Menu" with Save/Load/
  Formation/Options/Mods/Main Menu/Exit; driven off EscMenuVM command methods (RT has no
  ContextMenuEntityVM list like WOTR); Back → vm.OnClose(). Verified: opens + Back returns to game.
- **SaveLoadScreen** (CommonVM.SaveLoadVM, Layer 22) ✅ — Save/Load mode tabs + save-slot FlowSheet
  table (name/location/date/playtime/type, grouped by playthrough); Enter selects, Save/Load/Delete
  act on the selection; Back → vm.OnClose(). Verified: all slots render with metadata; Back closes.
  (Found + fixed: SaveLoadScreen was missing its Back action — added it, re-verified.)
  Deferred: custom save-name typing (needs MessageBoxScreen TextField support).

## Summary for the maintainer (morning review)
Branch **`overnight-pathfinder-port`** (off `main` @ 69b8c20). Debug **and** Release build 0/0.
Commits:
- 569fab2 — speech fallback roster, review buffers, in-game HUD, dialogue, combat events, audio earcons.
- f6b55e3 — inventory/journal/character-sheet windows + press-any-key announce.
- 50986d2 — Esc (pause) menu + Save/Load window.

**Verified in-game** (Prism/NVDA active): speech roster; review buffers (HP/AP/defenses/buffs); the
surface HUD; all 5 service-window screens (Inventory/Journal/Character + the stat modifier drill-in,
EscMenu, SaveLoad) + their Back/close; WarningReader; CombatEvents (synthetic death event →
"Багардор is down"); MessageBox; the press-any-key announce. Build verified clean for both configs.

**Please sanity-check in normal play** (couldn't trigger cleanly via the harness at night):
1. **Talk to an NPC** → DialogueScreen should read the cue + speaker and let you pick answers.
2. **Enter one fight** → you should hear damage/death/buff lines and turn/AP info; "not enough action
   points"-type refusals should speak (WarningReader).
3. **Audio earcons** are ported but OFF. Run `RTAccess.Audio.Earcons.Test()` (via the dev REPL, or flip
   `RTAccess.Audio.Earcons.Enabled = true`) to audition the palette; tell me the pitch/volume you want
   and which events to wire (focus / screen-change / activate / boundary / turn-start / error).

**Known issues / follow-ups** (details above): a few defensive stats show raw enum names; the
full-screen-UI FocusMode trap (DLC manager); custom save-name typing deferred. **Not yet ported**:
Vendor/Loot/Encyclopedia/Rest/GroupChanger screens, spatial sonar/wall-tones, the rich
tooltip/encyclopedia drill reader (the flagship XL item).

The ExplorationNav interactable-cycling is intentionally gated OFF (`InGameScreen` / the engine-gated
`SurfaceMainInputLayer`): the HUD works without it, but cycling nearby world objects in mouse mode still
needs the self-driven scan from the pivot plan — that's the main exploration gap left.
</content>
