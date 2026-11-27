---
name: rt-hud-completeness-plan
description: "Main-HUD completeness audit — gauge readout (K), window-openers, threshold cues, GameText game-string reuse, ModLog file logging; VM field paths"
metadata: 
  node_type: memory
  type: project
  originSessionId: 0c45ecf5-d564-41f5-8726-283472c83408
---

Audited the sighted surface HUD (`SurfaceHUDVM` + `SurfaceStaticPartVM` + `SurfaceDynamicPartVM`) vs
what the mod exposes (`Screens/InGameScreen.cs` Tab tree + passive announcers + hotkeys). Plan in
`docs/plans/tiered-gauging-hollerith.md`.

**Gaps found:** RT-specific gauges (momentum/veil, profit factor, boss HP, turn/Necron timers, etude
objective) were 100% unsurfaced; 4 window-openers (Augmentations/Ship/Colony/Cargo) missing;
`HitPredictor` looked like dead code (it is actually live, see below); HUD labels + hotkey/passive
announces were hardcoded English despite matching `ui.json` keys existing.

**Gauge readout — `Accessibility/HudGauges.cs` on K** (Exploration category): read-only, self-filtering
readout of momentum/veil/profit-factor/boss-HP/turn-timer/Necron/objective. VM field paths:
`ProfitFactor.Total`, `SurfaceActionBarVM.VeilThickness.Value.Value`, momentum via
`MomentumEntityVM.CurrentPercent * MaximalMomentum`.

**GameText (`Localization/GameText.cs`) — prefer the game's own localized strings, ui.json as fallback:**
`GameText.Or(() => UIStrings.Instance.MainMenu.Inventory, "screen.inventory")`; keybind labels via
`UISettingsRoot.Instance.UIKeybind*.X.Description` (`UISettingsRoot.Instance` IS the nested links object;
`UISettingsEntityBase.Description` is a public property). `GameText.Action(verb)` maps
`select/cancel/increase/decrease/expand/collapse` → `UIStrings.CommonTexts.*`; `hud.menu` reuses
`CommonTexts.Menu`. No game 1:1 for wheel names or `activate/open/toggle/choose/next/previous` (stay ui.json).

**File logging — `Accessibility/ModLog.cs`** wraps UMM's `ModLogger`; `Main.Log` is a `ModLog`
(constructed in `Load` BEFORE the try, so early crashes are captured). Forwards Log/Error/Warning to UMM
AND mirrors every line (timestamp + level tag) to `rtaccess_log.txt` in the deployed folder — there is no
UnityModManager.log on disk, so this is the only on-disk mod-error record.

**Window-openers (S3):** Ship/Colony/Cargo/Augmentations added to `InGameScreen.WindowButtons`. Dispatch
is UNIFORM — all four are `ServiceWindowsType` values handled by `ServiceWindowsVM.HandleOpenWindow`'s
`ShowWindow` switch, so `HandleOpenWindowOfType(type)` suffices (NO `IngameMenuVM` methods). `WindowEnabled(type)`
gates each to the game's own button visibility: Ship=`CanAccessStarshipInventory && !ServiceWindowsBlocked`;
Colony=`… && !ColoniesState.ForbidColonization`; Cargo=`!ServiceWindowsBlocked`;
Augmentations=`StoreManager.CheckIfDlcPurchasedAndInstalled(DLC3TheInfiniteMuseion) && !AugmentationsWindowBlocked`
(needs `using Kingmaker.Stores` + `.Stores.DlcInterfaces`). Labels via `GameText`(`UIStrings.MainMenu.*`).

**HitPredictor is live (S4):** `HitPredictor.Describe` is called via `Exploration/Targeting.PredictLine` by
`Scanner` while aiming (terse on target-cycle `Select`, verbose on re-announce `ReSpeakSelection`);
`CombatReads.CoverRangeThreat` is the passive non-aiming counterpart. Both localized via
`predict.*`/`cover.*`/`combat.no_los|in_range|out_of_range|threatening`.

**Threshold cues (S7):** `CombatEvents.PollThresholds()` (per-frame poll → passive queue), edge-detected: turn-timer
last-5s (`TurnTimerVM.IsFiveSecsLeft`), boss-HP 75/50/25 (`BossHPBarVM.Progress` floor×4 buckets), and an
etude-objective FAIL/SUCCESS flip (`EtudeCounterVM`, rising-edge). Momentum (heroic-act / desperate-measure
readiness) and veil-critical were DROPPED from the poll — the game logs both, so they ride LogTap
([[rt-message-log]]); the on-demand K gauge carries their persistent state. Needs live cadence/noise tuning.

**Still deferred (Tier 2, need live combat):** S5 deployment/combat-start window as a focusable region,
S6 weapon-set switch (`SurfaceActionBarPartWeaponsVM.ChangeWeaponSet`). Logs live in
`%LocalLow%\...\UnityModManager\RTAccess\` (speech_log.txt / focus_log.txt / rtaccess_log.txt).
