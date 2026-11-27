# Main HUD completeness — prioritized fix plan

Plan name: `docs/plans/tiered-gauging-hollerith.md`. Sits on top of the shipped surface HUD
(`Screens/InGameScreen.cs`), the combat-event pipeline (`Accessibility/CombatEvents.cs`), and
the localization helper (`Localization/Loc.cs` + `assets/locale/enGB/ui.json`). Companion:
the combat plan `docs/plans/phased-skirmishing-liskov.md` (targeting / dispatch / prediction).

---

## 0. Why this plan exists

An audit compared the sighted **surface HUD** (`SurfaceHUDVM` + `SurfaceStaticPartVM` +
`SurfaceDynamicPartVM`) against what the mod exposes through its parallel Tab tree
(`InGameScreen`), passive announcers, and hotkeys. Four questions:

1. **Do we surface all the info the HUD shows?** Mostly for the core loop — but the
   Rogue-Trader-specific pressure gauges are 100% unsurfaced.
2. **Are all focusable buttons covered?** The region skeleton is good, but 4 window-openers,
   deployment controls, and weapon-set switching have no equivalent; `HitPredictor` is built
   but unwired.
3. **Are all Tab regions covered?** Status / Actions / Party / Combat / Windows / Menu exist;
   the gaps are the buttons above.
4. **Do we use localized labels/tooltips?** **No** — the combat-event pipeline is localized,
   but HUD labels, hotkey/passive announces, and slot-state strings are hardcoded English,
   often duplicating a `ui.json` key that already exists. Violates the mod's own rule
   (`Loc.cs:3-9`).

### Verified fact base (decompiled source, cited in slices)

- Gauge VMs and their live fields:
  - Momentum: `SurfaceActionBarVM.SurfaceMomentumVM.MomentumEntityVM.Value` →
    `CurrentPercent` (0..1), `HeroicActActive`, `DesperateMeasureActive`
    (`MomentumEntityVM.cs:28,32,34`). Max = `Game.Instance.BlueprintRoot.WarhammerRoot.MomentumRoot.MaximalMomentum`
    (`BlueprintMomentumRoot.cs:31`).
  - Veil: `SurfaceActionBarVM.VeilThickness.Value` (int, `VeilThicknessVM.cs:19`). Max/critical =
    `PsychicPhenomenaRoot.MaximumVeilOnAllLocation` / `.CriticalVeilOnAllLocation`
    (`BlueprintPsychicPhenomenaRoot.cs:44,46`).
  - Profit factor: `Game.Instance.Player.ProfitFactor.Total` (float, `ProfitFactor.cs:32`; used
    by `ProfitResourcePCView.cs:32`).
  - Boss HP: `SurfaceStaticPartVM.BossHPBarVM` → `IsShowing`, `BossName`, `HPLabel`
    (`BossHPBarVM.cs:17-23`).
  - Turn timer: `SurfaceStaticPartVM.TurnTimerVM` → `IsShowing`, `Counter` ("MM:SS")
    (`TurnTimerVM.cs:21-25`).
  - Necron timer: `SurfaceStaticPartVM.NecronTimerVM` → `IsUnlockedAndVisible`, `CurrentTimerValue`
    (`NecronTimerVM.cs:16-18`).
  - Etude counter: `SurfaceStaticPartVM.EtudeCounterVM` → `IsShowing`, `Label`, `ShowCounter`,
    `Counter`, `ShowProgress`, `Progress` (`EtudeCounterVM.cs:18-32`).
- Window-opener gates (`IngameMenuNewPCView.cs:189-215`):
  - Ship: `Player.CanAccessStarshipInventory && !Player.ServiceWindowsBlocked`.
  - Colony: `Player.CanAccessStarshipInventory && !Player.ColoniesState.ForbidColonization`.
  - Cargo: `!Player.ServiceWindowsBlocked`.
  - Augmentations: DLC3 installed `&& !Player.AugmentationsWindowBlocked`.
- Reusable game-localized labels (if we ever prefer them over our `ui.json`): `UIStrings.Instance.MainMenu.*`,
  `HUDTexts.EndTurn/Pause`, `CommonTexts.Pause/Unpause`, `FormationTexts.FormationLabel`,
  `TurnBasedTexts.StartBattle`.
- `Loc.T(key)` / `Loc.T(key, new { var = ... })` resolves via `ui.json`; `{var}` substitution is
  reflection over an anonymous object (`Message.cs:48,129-135`).

---

## 1. Tiering rule

**Tier 1 — ship now** (read-only reads + string routing; fully build-verifiable, no game run
needed to be *safe*). **Tier 2 — deferred**: touches combat dispatch / combat flow, or drops the
player into a screen we haven't made accessible yet, so it needs live in-game verification before
it can ship. This plan implements Tier 1 first; Tier 2 items are specified but gated on a test pass.

---

## Tier 1 (implement now)

**Status (2026-07-01): S1 + S2 landed, compile-clean, and in-game verified.** S1 refined to prefer
the game's own localized strings for HUD button labels via `Localization/GameText.cs`. The full
localization sweep (announcers + buffers) also landed. **Speech-log verification** (a Russian test
session, `speech_log.txt` 3022 lines): zero unsubstituted `{placeholders}`, zero raw keys spoken;
slot state / wounds / exploration / landmark all render (English mod labels over Russian content).
Not positively sampled in that log: the HUD Windows/Menu `GameText` labels + K gauges (no leaks
anywhere, though). Tier 2 remains deferred.

**Game-string reuse (`GameText.Or(() => <LocalizedString>, "<ui.json fallback key>")`)** — prefers
the game's translation, falls back to `ui.json` on null/empty/throw (lazy `Func`, so the blueprint
root is only touched at call time). Wired for the button labels:
`UIStrings.Instance.MainMenu.{Inventory,CharacterInfo,Journal,LocalMap,Encyclopedia,Inspect}`,
`.CommonTexts.{Pause,Paused}`, `.ActionTexts.Stop`, `.FormationTexts.FormationLabel`,
`.HUDTexts.EndTurn`, and the keybind descriptions
`UISettingsRoot.Instance.UIKeybindGeneralSettings.{Hold,CameraRotateToPointNorth}.Description` +
`.UIKeybindSelectCharacterSettings.SelectAll.Description`. Left on `ui.json` (no clean game 1:1):
the status/roster composition, the `Unpaused`/`Holding position.`/`Stopped.`/Inspect-on-off announces,
and the `HudGauges` readout prefixes (their *values* already come from game VMs).

### S1 — Localization discipline  *(answers Q4)*  — DONE

Route every hardcoded English HUD string through `Loc.T`, reusing the `ui.json` keys that already
exist and adding the few that don't. Scope:

- `InGameScreen.cs`
  - Region names: `Status / Actions / Party / Combat / Windows / Menu` → `hud.*`.
  - Window labels (`WindowLabel`, `:149-160`) → `screen.inventory/character/journal/map/encyclopedia`.
  - Menu labels (`BuildMenu`, `:222-237`) → `hudmenu.pause/hold/stop/select_all/formation/inspect/reset_camera`.
  - `TogglePause` announce (`:249`) → `pause.paused/unpaused`; `ToggleInspect` (`:258`) → `hud.inspect_on/off`.
  - `End turn` (`:388`) → `turn.end`.
  - Status/roster composition (`StatusLine`/`PartyLabel`/`AppendWounds`/`InitiativeLabel`, `:314-441`)
    → format keys `unit.wounds` / `unit.wounds_temp` / `combat.ap_mp` / `unit.selected` /
    `combat.current`, and reuse `combat.turn` / `combat.turn_enemy` for the whose-turn tail.
- `PartyHotkeys.cs` — `SelectAll`/`Hold`/`Stop`/`CombatStatus` announces (`:114-143`) →
  `party.selected_all[_one]` / `party.holding` / `party.stopped` / `status.none`.
- `Message.cs` — fix the stale "Localization isn't wired yet" comment (`:14-17`); the resolver is
  wired at `LocalizationManager.cs:48`.

New `ui.json` keys: `hud.status/actions/party/combat`, `hud.inspect_on/off`, `unit.wounds`,
`unit.wounds_temp`, `unit.selected`, `combat.ap_mp`, `combat.current`, `party.holding`,
`party.stopped`, `status.none`.

Acceptance: no user-facing English literal remains in `InGameScreen`/`PartyHotkeys`; all reads
resolve through `ui.json`; the mod compiles. (Broader announcer localization — `WheelMenus`,
`ExplorationEvents`, `LandmarkNav`, `HitPredictor`, `ProxyActionBarSlot` state, the review
buffers — is a follow-up, tracked but out of this slice to keep it reviewable.)

### S2 — Read-only HUD gauge readout  *(answers Q1: the missing RT indicators)*  — DONE (`Accessibility/HudGauges.cs`, K)

New `Accessibility/HudGauges.cs`, one on-demand hotkey (**K**, a confirmed-free letter) that speaks
every currently-relevant gauge in a single line, self-filtering by each VM's `IsShowing`/visibility:

- Momentum `{value} of {max}` + `heroic act ready` / `desperate measure ready` (combat only).
- Veil thickness `{value} of {max}` + `veil critical` past the critical threshold.
- Profit factor `{value}` (always available).
- Boss HP `{name}, {hp}` (when `BossHPBarVM.IsShowing`).
- Turn timer `{time}` (when `TurnTimerVM.IsShowing`).
- Necron countdown `{value}` (when `NecronTimerVM.IsUnlockedAndVisible`).
- Objective `{label} {counter}` (when `EtudeCounterVM.IsShowing`).

All reads are the verified fields in §0. Register K under `InputCategory.Exploration` (live when
`InGameScreen` owns the world, like the party hotkeys). Purely read-only — no dispatch — so it is
safe to land without a game run. `gauge.*` locale keys added.

Acceptance: `HudGauges.ReadAll()` compiles against the real VM fields; K registered without
colliding with the existing keymap; speaks `gauge.none` when nothing applies.

---

## Tier 2 (deferred — needs in-game verification before shipping)

### S3 — Missing window-openers (Augmentations / Ship / Colony / Cargo) — **DONE 2026-07-01**
Added the four openers to the Windows region (`InGameScreen.WindowButtons`). **Dispatch is uniform** —
`ServiceWindowsType` has `ShipCustomization/ColonyManagement/CargoManagement/Augmentations` and
`ServiceWindowsVM.HandleOpenWindow`'s `ShowWindow` switch handles them all, so no `IngameMenuVM` methods
are needed (verified against decompiled source). New `WindowEnabled(type)` gates each to match the game's
own button visibility (`IngameMenuNewPCView.CheckEnabled*/CheckServiceWindowsBlocked`): Ship =
`CanAccessStarshipInventory && !ServiceWindowsBlocked`; Colony = `CanAccessStarshipInventory &&
!ColoniesState.ForbidColonization`; Cargo = `!ServiceWindowsBlocked`; Augmentations =
`StoreManager.CheckIfDlcPurchasedAndInstalled(DLC3TheInfiniteMuseion) && !AugmentationsWindowBlocked`.
Labels reuse the game strings (`UIStrings.MainMenu.{ShipCustomization,ColonyManagement,CargoManagement,
Augmentations}` via `GameText`, `screen.*` fallback). `HandleOpenWindow` self-no-ops on a blocked window,
so a stale-enabled read is still safe. **The destination screens still need accessibility work** (the
original blocker) — the user is doing that in parallel; verify openers + screens together in-game.

### S4 — Wire `HitPredictor` (dead code → live) — **DONE**
~~`HitPredictor.Describe` has no production caller.~~ It was wired by the targeting-from-cursor /
combat-legibility commits: `Exploration/Targeting.PredictLine` calls it, and `Scanner` appends the
line while aiming (terse on target-cycle, verbose on re-announce); `CombatReads.CoverRangeThreat` is
the passive non-aiming counterpart. **2026-07-01 follow-up:** its vocabulary is now localized —
`HitPredictor` + `CombatReads` route through `Loc.T` (new `predict.*`/`cover.*`/`combat.*` keys).
Correctness vs the sighted reticle still needs a live-fight check (milestone-D, `phased-skirmishing-liskov.md`).

### S5 — Deployment / combat-start window as a focusable region
Surface `CombatStartWindowVM` (Start Battle + deploy). **Deferred**: combat-flow, needs a live
preparation-phase to verify.

### S6 — Weapon-set switch on the HUD
Expose `SurfaceActionBarPartWeaponsVM` set switching (`ChangeWeaponSet`). **Deferred**: mutates
combat state; verify live.

### S7 — Passive threshold cues — **DONE 2026-07-01**
Implemented in `CombatEvents.PollThresholds()` (polled each frame from `Tick`, enqueued into the shared
passive queue). Edge-detected one-shots: momentum heroic-act ready (`MomentumEntityVM.HeroicActActive`),
desperate-measure ready (`DesperateMeasureActive`), veil crossing into critical (`>= CriticalVeilOnAllLocation`),
turn timer's last five seconds (`TurnTimerVM.IsFiveSecsLeft`, ready-made), and each 25% the boss loses
(`BossHPBarVM.Progress` bucketed by `floor(×4)`; announces 75/50/25 on the way down, none on first sight
or a heal). Each cue self-gates on its VM visibility. Reuses `gauge.heroic_act/desperate_measure/veil_critical`;
new `cue.turn_timer_low`/`cue.boss_hp`. **Cadence/noise still needs a live-play check** (esp. boss-HP buckets
and how it interleaves with the combat-log queue).

---

## 3. Follow-ups (tracked, not scheduled)
- ~~Localize the remaining announcers~~ **DONE 2026-07-01** (compile-clean): `WheelMenus`,
  `ExplorationEvents`, `InteractionEvents`, `LandmarkNav`, `ProxyActionBarSlot` state, and `Buffers/*`
  now route through `Loc.T` (+ `wheel.*`/`explore.*`/`interact.*`/`landmark.*`/`slot.*`/`buffer.*`
  keys); `WheelMenus`/Formation reuses the game's `FormationTexts.FormationLabel` via `GameText`.
  `HitPredictor`/`CombatReads` vocabulary localized in the S4 follow-up (see S4). Minor known edge:
  the two `Buffers` labels resolve at boot (no mid-session language retranslation). Not yet run in-game.
  **Exploration folder finished 2026-07-01:** `Targeting.cs` (aiming prompts + ArmAnnounce),
  `Scanner.cs` (all inline readouts), `AbilityTargeting.cs` (commit outcomes), `Inspect.cs` now route
  through `Loc.T` (new `aim.*` / `scan.sum_*` / `taxonomy.{hazards,buffzones,zones}` / `inspect.cant`
  keys + heavy reuse of existing `scan.*`/`taxonomy.*`/`nav.position`). `grep 'Speak("'` over
  `Exploration/` is now empty.
- ~~Prune the WrathAccess-era `ui.json` cruft~~ **DONE 2026-07-01** (compile-clean): removed ~39
  grep-confirmed-dead WOTR keys (`spellbook.*`, `metamagic.*`, `spell.*`, `app.loaded`, `geo.miles`,
  `unit.ac`, `hud.spells`, `screen.{kingdom,city,mythic_path,spellbook}`, `char.{mythic,alignment,
  negative_levels}`, `chargen.alignments`, `group.mythic_tag`). Also fixed a live leaked string:
  `wizard.nav.help` "Pathfinder: Wrath of the Righteous" → "Warhammer 40,000: Rogue Trader".
- **File logging** **DONE 2026-07-01**: `Accessibility/ModLog.cs` wraps UMM's `ModLogger`; `Main.Log`
  mirrors every line to `rtaccess_log.txt` (there is no UnityModManager.log on disk).
- ~~Extend the `GameText` game-string reuse to the remaining announcers~~ **DONE 2026-07-01**
  (compile-clean): new `GameText.Action(verb)` maps the action verbs the game ships a word for
  (`select/cancel/increase/decrease/expand/collapse` → `UIStrings.CommonTexts.*`, `ui.json` fallback);
  `ProxyActionButton` (the canonical HUD/menu/window button) now routes its action label through it,
  so most buttons follow the game's language. `hud.menu` (region label + in-game-menu wheel) reuses
  `CommonTexts.Menu`. Also fixed a latent bug: `action.cancel` was referenced (`NameEntryScreen`) but
  missing from `ui.json`. No clean game 1:1 for the wheel names (`UIPartySelectorTexts` has only
  Link/Unlink) or the `activate/open/toggle/choose/next/previous` verbs — those stay on `ui.json`.
  The per-control proxies (dropdown/slider/stepper/etc.) still call `Message.Localized("ui","action.*")`
  directly; they could route through `GameText.Action` later, but the verbs they use mostly have no
  game equivalent, so the payoff is small.
