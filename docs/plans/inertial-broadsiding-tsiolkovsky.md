# Space (voidship) combat accessibility — research + porting plan

Plan name: `docs/plans/inertial-broadsiding-tsiolkovsky.md`. Status: **static research +
Phase 0 live verification done (dev harness, 2026-07-12, `ScenarioSpaceCombat_1` save) —
see §0b; the plan is sound and the act-loop is execution-proven.** Builds on
`HOOKMAP.md` SUBSYSTEM 7, the shipped surface-combat stack
(`docs/plans/phased-skirmishing-liskov.md`, `piloted-aiming-lamport.md`) and the graph-nav
screen conventions (`keyed-graphing-tarjan.md`). Trigger: the campaign's first mandatory
space battle (chapter 1 warp exit).

---

## 0. Executive summary — the architecture verdict

Space combat is **not a separate combat engine**. It is `GameModeType.SpaceCombat` layered on
the same substrate the mod already owns:

- **Same square `CustomGridGraph`** (ships occupy `SizeRect` footprints, e.g. frigate 1×2;
  8-direction facing via `CustomGraphHelper.GuessDirection(Forward)`).
- **Same `TurnController`** (initiative, rounds, `TryEndPlayerTurnManually()`, the same
  `ITurnBasedModeHandler`/`ITurnStartHandler` events our `CombatEvents`/turn cues already poll).
  `GamesModeFactoryFacade` registers `TurnController`, `DefaultPointerController`,
  `VirtualPositionController`, `SelectionCharacter` for SpaceCombat — the mode composition is
  "Default mode ± a starship controller set", not a new stack.
- **Same action-bar slot VM** — every ship weapon/post/ability renders as `ActionBarSlotVM`
  (via `MechanicActionBarShipWeaponSlot` / `MechanicActionBarSlotAbility`), i.e. our
  `ActionBarNodes` availability-first readout applies as-is.
- **Same aim pipeline** — `ClickEventsController.WorldPosition` → `AbilityRange` →
  `ICellAbilityHandler` → `AbilityTargetUIDataCache`; the space overtip hit-chance block reads
  the very same `AbilityTargetUIData` (burst hit chances, min/max damage, `HitAlways`,
  `UnitState.IsStarshipAttack`). Our `AimPointerDriver`/`AimReadTap`/`AimParity` carry over.
- **Same TB move commit** — `UnitCommandsRunner.MoveSelectedUnitToPointTB` →
  `TryCreateMoveCommandTB(MoveCommandSettings{Destination, DisableApproachRadius=true})` with
  the two-click virtual-move confirm; it explicitly special-cases starships (skips
  `CanUnitStandOnNode`, trusts `ShipPath`).

What is genuinely new: **the movement model** (inertia + forced-forward `ShipPath`, speed
modes, push/finish phases), **ship systems** (sector shields/armour, posts+officers, weapon
arcs, ammo/arsenal, crew/morale), **torpedo player-turns**, and **the exit-battle rewards
popup**. And the one structural gap: **no mod base context is active in SpaceCombat mode**
(`InGameScreen` gates on `IsSurface`, `SystemMapScreen` on `GameModeType.StarSystem`) — the
blind player currently gets log/bark narration and nothing else.

---

## 0b. Live verification status (dev harness, 2026-07-12, first space battle)

Ran in the real `ScenarioSpaceCombat_1` fight (player Firestorm frigate 144 HP vs two
PirateRaider1 Cobra destroyers, 30 HP each). **Every load-bearing claim held; the full
act-loop was executed through the mod's own funnels.**

**Confirmed live:**
- `RootUiContext.SpaceVM.StaticPartVM.TryGetComponentVM(SpaceStaticComponentType.SpaceCombat)`
  → `SpaceCombatVM`; weapon groups Port=1/Prow=2/Dorsal=1/Starboard=1 + 4 ship abilities +
  6 posts, all `ActionBarSlotVM`s with localized names + `AbilityData` + `IsPossibleActive`.
- **Same grid**: `nodeSize=1.35`, 500×500; frigate SizeRect 1×2, facing via `GuessDirection`.
- Movement state: speedMode Normal, speed 11 = blue AP 11/11, inertia 3, 87 reachable
  (all standable), finishTiles 3, pushTiles 6, `GetEndNodes()`=52.
- **Forced movement confirmed** (`PartUnitCombatState.CanEndTurn():437`): starship branch
  returns false while `ActionPointsBlue >= FinishingTilesCount && HasAnotherPlaceToStand` —
  after moving to 1 MP, `CanEndTurn` flipped true. Voice this ("must move N more tiles").
- **`CommandDispatch.MoveTo(node)` works verbatim for the ship** (moved 10 cells + 135°
  facing change, AP 11→1). NOTE: the *double-call* `UnitCommandsRunner.MoveSelectedUnitsToPoint`
  preview→confirm path did NOT commit from the REPL (no pointer hover) — keep our funnel.
- **`CommandDispatch.UseAbilityOnUnit` fires ship weapons** (starboard plasma battery →
  adjacent raider; 21 shield + 19 hull damage landed; charges 1→0).
- Refusal reasons via `ad.CanTarget(tw, out UnavailabilityReasonType? r)` (note: the out
  param is **nullable**): `TargetTooFar` (range in cells), `HasNoLosToTarget` (≈ firing-arc
  fail for ships). After the real maneuver the arc verdicts opened exactly as geometry
  implied (dorsal + starboard bear, prow doesn't when the target is abeam).
- Prediction parity: `RuleStarshipCalculateHitChances` (hit 80 / crit 5 / evade 0),
  `GetStarshipDamagePrediction` per weapon (hull + shield ranges vs enemy 35/35 sectors),
  `AbilityTargetUIDataCache.GetOrCreate(ad, (MechanicEntity)target, pos)` populates for
  starship weapons (`HitWithAvoidanceChance=80`, dmg 0..3; `BurstHitChances` empty for the
  single-shot lance — verify a burst weapon later).
- **Narration already flowing** (speech transcript): "Combat started", "Round N", "Your
  turn, Праведное Отпущение, 0 AP, 11 MP", enemy turn cues by ship name, and rich LogTap
  attack lines — "использует: <weapon>" + "Атакует цель <ship> 2 раз, наносит урон щитам
  (21) и корпусу (19)". The log carries attack count + shield + hull damage.
- **Inspect works in space**: `EventBus.RaiseEvent<IUnitClickUIHandler>(h =>
  h.HandleUnitConsoleInvoke(enemy))` populated `SpaceVM.StaticPartVM.InspectVM` (an
  `InGameInspectVM`) with **`TooltipTemplateSpaceUnitInspect`** — TooltipReader territory.
- Posts live: Кассия on SupremeCommander (skill 70, grants Swing Run), Идира on WarpGuide;
  empty posts read skill 0. Shields/armour/crew/morale/military-rating all read.
- Vantage points: `PartVantagePoints` present but empty in this scenario — defer.

**Findings → work items (fold into Phase 1):**
1. Turn cue says "0 AP, 11 MP" for a starship — drop the AP half (ships have no yellow-AP
   economy in practice) and say "movement 11" (+ speed mode).
2. "Раунд 2" (game log) and "Round 2" (our TurnCues) **duplicate** — suppress ours when the
   log line lands (or mute that log line into the queue-dedupe).
3. Sector shields dropped (S 70→62, A 70→64) with **no sector-level cue** — add the
   `IShieldAbsorbsDamageHandler` announcer for the player ship ("starboard shields 62 of 70").
4. Speed-mode changes are **ability-driven** (`WarhammerContextActionSetStarshipSpeedMode`,
   Ram forces LowSpeed; no dedicated UI found and the starter ship has no such abilities) —
   announce mode at turn start; no separate control needed until abilities appear.
5. REPL/ports gotchas recorded: `AbilityTargetUIDataCache` lives in
   `Kingmaker.UI.SurfaceCombatHUD`; blueprint `GetComponent<T>()` needs
   `Kingmaker.Blueprints` extensions (enumerate `ComponentsArray` instead).

**Still unverified (needs the right game state):** torpedo turns (no torpedoes owned — the
MasterOfOrdnance post is empty), burst `BurstHitChances` population, custom-path abilities
(Ram/Strafe/Swing Run cell-target flow), TimeSurvival rounds-left areas, keybind dump for
the mode + TileExplorer/Sonar misbehavior audit (needs keypresses — do at Phase 1 start),
post-blocked events, the exit popup (do NOT trigger it casually — it ends the fight and the
exit path may autosave).

---

## 1. Engine facts (decompiled-source pass — all types in `Code.dll`)

### 1.1 Mode, entry, exit
- Mode: `GameModeType.SpaceCombat`; combat runs on the star-system area scene
  (`SpaceCombatBackgroundComposerController` registered for StarSystem+SpaceCombat).
- Entry: encounter/etude spawns enemy ships; `AutoJoinSpaceCombatController` joins them.
- Exit: `IEndSpaceCombatHandler.HandleEndSpaceCombat` (combat won) →
  `ExitBattlePopupVM.IsActive=true`; player leaves via
  `ExitBattlePopupVM.ExitBattle(bool forceOpenVoidshipUpgrade)` → `ExitSpaceCombatGameCommand`
  → `ExitSpaceCombatController.ExitSpaceCombat` → back to StarSystem mode.
- Some areas are **TimeSurvival** ("survive N rounds"): `Kingmaker.AreaLogic.TimeSurvival`
  component on the area; `RoundsLeft`, `UnlimitedTime` (service panel shows the counter).

### 1.2 Turn system
- `Game.Instance.TurnController` verbatim. Player ship turn: `IsPlayerTurn &&
  CurrentUnit == Game.Instance.Player.PlayerShip`.
- **Torpedo turns**: `IsPlayerTurn && CurrentUnit != PlayerShip` — player-launched torpedo
  salvos are *soft* `StarshipEntity` units (`Blueprint.IsSoftUnit`, `PartVantagePoints` skip)
  that get their own commandable turns (move + `TorpedoSelfDestruct` ability).
- Initiative UI: the shared `InitiativeTrackerVM` (`SurfaceCombatUnitVM` entries) inside
  `SpaceCombatServicePanelVM`.
- End turn: `TurnController.TryEndPlayerTurnManually()`; the sighted button binds keybind
  name **"EndTurn"** (`Game.Instance.Keyboard.GetBindingByName("EndTurn")`).

### 1.3 Movement model (`PartStarshipNavigation`, `ShipPath`)
- Per-turn movement budget = float blue AP (`CombatState.ActionPointsBlue`), initial value =
  `CurrentSpeed`, derived from `WarhammerInitialAPBlue` × speed mode:
  `enum SpeedModeType { Normal, Deccelerating (2/3), LowSpeed ((n+1)/2), FullStop (0) }`.
  Setting `SpeedMode` adjusts blue AP immediately; on turn end it decays one step back toward
  Normal (`HandleUnitEndTurn`). (How the player changes speed in the UI — verify in-harness;
  the setter is public and there's a `set_starship_speed_mode` cheat.)
- Reachability: `Navigation.UpdateReachableTiles(...)` computes a **`ShipPath`** from
  `{Inertia (PartStarship.Inertia), MovementPoints (blue AP), LastStraightMoveLength,
  LastDiagonalCount, direction, turnAngles[]}`. Result:
  `Dictionary<GraphNode, ShipPath.PathCell>` where
  `PathCell { int Direction; int[] ParentDirections; int StraightDistance; int DiagonalsCount;
  int Length; bool CanStand }`; also `RawReachableTiles` (`DirectionalPathNode` set with
  `turnCount`). `TurnAngleType { Turn45, Turn90, Turn135, Turn180 }`;
  `CanTurn90Degrees` via the `StarshipMovementVariation` fact (Master Helmsman talents).
- Phases: `PushPhaseTilesCount` (acceleration window, plasma-drive dependent),
  `FinishingTilesCount` (must be able to *stop* in the last N tiles; blueprint default 3),
  `IsEndingMovementPhase`, `IsAccelerationMovementPhase`.
- Dead end: `StarshipPathController.IsCurrentShipInDeadEnd` — when <2 standable cells remain
  and AP > `BlueprintRoot.DeadEndTurnCost`, the game recomputes a 1-tile escape set.
- End-node review: `Navigation.GetEndNodes()` (cells whose remaining length < finishing
  count); `Navigation.CanStand(node, direction)`; `FindPath(Vector3 destination)` → the
  concrete `ForcedPath` (route preview: turns, diagonals, length).
- Commit: identical surface funnel — first commit sets the virtual move (preview), second
  confirms (`MoveCommandStatus.SamePath` → `TryRunVirtualMoveCommand`). The pointer-driven
  path markers (`ShipPathNodeMarkersController`) read `ClickEventsController.WorldPosition` —
  the exact field `AimPointerDriver` already injects.

### 1.4 Ship systems (read surfaces)
- `StarshipEntity : BaseUnitEntity` — parts: `Hull`, `Shields`, `Navigation`, `Crew`,
  `Morale`, `Engine`, `StarshipProgression`, `PartVantagePoints`. Player ship:
  `Game.Instance.Player.PlayerShip`.
- **Hull**: `Health.HitPointsLeft/MaxHitPoints`; `PartStarshipHull { Posts, HullSlots,
  WeaponSlots, CurrentMilitaryRating, InternalStructure, ProwRam,
  GetLocationDeflection(StarshipHitLocation) }`.
- **Shields**: `PartStarshipShields.GetShields(sector)` → `StarshipSectorShields { Current,
  Max, Reinforced, WasHitLastTurn }`, sectors `{ Fore, Port, Starboard, Aft }`; sums
  `ShieldsSum/ShieldsMaxSum` (already used by our `ProxyUnit` enemy-shields tail).
- **Armour**: `ship.Stats.GetStat(StatType.ArmourFore/Port/Starboard/Aft).ModifiedValue`.
- **Crew/morale** (recipe from the *orphaned* `ShipCrewPanelVM` — no live instantiation site
  found; read the parts directly): `Morale.MoraleLeft/MaxMorale`, `Crew.Ratio`,
  `Crew.Count + "k"`, `Hull.CurrentMilitaryRating`, ship buffs = `Buffs` (non-hidden).
- **Posts** (`Warhammer.SpaceCombat.StarshipLogic.Posts`): `PostType { SupremeCommander,
  MasterOfOrdnance, EnginseerPrime, WarpGuide, MasterHelmsman, MasterOfEtherics }`; `Post {
  CurrentUnit (officer), CurrentSkillValue, HasPenalty, IsBlocked, BlockingBuff,
  UnlockedAbilities() }`. Localized post names:
  `UIStrings.Instance.SpaceCombatTexts.GetPostStrings(index)` (already used by our
  `ShipCustomizationScreen`). Events: `IStarshipPostHandler.HandlePostBlocked/BuffDidAdded/
  BuffDidRemoved`.
- **Weapons** (`WeaponSlot : ItemSlot`): `Type ∈ { Prow, Dorsal, Port, Starboard, Keel }` →
  `RestrictedFiringArc` (Dorsal fires Fore+Port+Starboard); `Weapon`
  (`ItemEntityStarshipWeapon`), `ActiveAbility`, arsenal variants
  (`WeaponVariants`/`ActiveWeaponIndex` — switching raises
  `IWeaponSlotHandler.HandleActiveWeaponIndexChanged`), `AmmoSlot` + `Weapon.Charges` /
  `Reload()`. Arc geometry: `GetRestrictedFiringArcNodes(range, comp)` (the highlightable
  cell set) and `IsTargetInsideRestrictedFiringArc(target, range, comp)` (the canonical
  "can this battery bear on X").
- **Vantage points**: `PartVantagePoints` + `StarshipVantagePointsController` +
  `IStarshipVantagePointsHandler` — bonus tiles on the map (details: Phase 0 in-harness).
- **Scrap**: `Kingmaker.SpaceCombat.Scrap` — the post-combat repair/upgrade currency
  (`Player.Scrap`), granted via the reward popup.

### 1.5 The sighted UI tree (what parity requires us to mirror)
`RootUiContext.SpaceVM.StaticPartVM` creates `SpaceCombatVM` for
`SpaceStaticComponentType.SpaceCombat` (`SpaceStaticPartVM.cs:326`) — same tree our
`SystemMapScreen` already walks. Children:
- `ShipWeaponsPanelVM` — `WeaponAbilitiesGroups: Dictionary<WeaponSlotType, AbilitiesGroupVM>`
  (Port/Prow/Dorsal/Starboard, localized group labels in `UIStrings.SpaceCombatTexts`) +
  `AbilitiesGroup` (non-weapon, non-cantrip ship abilities) — all `ActionBarSlotVM`s;
  `IsPlayerTurn`, `IsTorpedoesTurn` reactives.
- `ShipPostsPanelVM` — `List<ShipPostVM>`; each `ShipPostVM { PostType, Portrait,
  AbilitiesGroup (ActionBarSlotVMs), IsPostBlocked, BlockDuration }`.
- `SpaceCombatServicePanelVM` — `ShipHealthText/Ratio`, `RoundsLeft` + `IsInTimeSurvival`,
  `IsPlayerTurn/IsTorpedoesTurn/CanEndTurn`, `EndTurn()`, `InitiativeTrackerVM`,
  `SelectedUnitVM`, `CombatActionsCount` (how many holders still have possible actions —
  feeds the sighted "you still have moves" nudge; `SpaceCombatMovementActionHolder` counts
  blue AP > 0 as an action and its `HighlightOn` raises the localized
  `CombatMovementActionHint` warning).
- `ExitBattlePopupVM` — see 1.7.
- `SpaceCombatCircleArcsVM` — enemy movement-arc rendering during AI turns (visual-only;
  no reactive payload worth mirroring).
- `ShipCrewPanelVM`/`ShipShieldsPanelVM` exist but are **never instantiated** (orphaned) —
  treat them as field recipes only.
- Enemy-ship data a sighted player reads via **unit overtips** (same
  `Kingmaker.Code.UI.MVVM.VM.Overtips.Unit` system): `OvertipHealthBlockVM` shows enemy
  shields and, while aiming, `AbilityDataHelper.GetStarshipDamagePrediction(...)`;
  `OvertipHitChanceBlockVM` shows hit% + `BurstHitChances` + `MinDamage..MaxDamage`
  (hover-gated + LateUpdate-deferred — same reasons we bypass it on surface).

### 1.6 Targeting & prediction (pure reads)
- Hit chance rule: `RuleStarshipCalculateHitChances : RulebookTargetEvent<StarshipEntity,
  StarshipEntity>` → `ResultHitChance` (per-shot list feeds `AbilityTargetUIData.
  BurstHitChances`).
- Damage/shield prediction: `AbilityDataHelper.GetStarshipDamagePrediction(StarshipEntity
  target, Vector3 casterPosition, AbilityData ability, ItemEntityStarshipWeapon weapon)` →
  `(DamagePredictionData resultDamage, ShieldDamageData resultShields)`.
- Attack resolution event: `RuleStarshipPerformAttack` via
  `IStarshipAttackHandler.HandleAttack` — `ResultIsHit/ResultIsCritical/ResultDamage/
  ResultShieldStrengthLoss/ResultHitLocation/Weapon`.
- Shield absorb event: `IShieldAbsorbsDamageHandler.HandleShieldAbsorbsDamage(before, after,
  sector)`.
- Torpedo launch event: `ITorpedoSpawnHandler.HandleTorpedoSpawn(BaseUnitEntity torpedo)`.
- Movement special-abilities (`AbilityCustomStarship{Ram,Strafe,SwingRun,Backing,...}` +
  `ICustomShipPathProvider`/`CustomPathNode`) present their own path node sets —
  `StarshipPathController.ShowCustomShipPath` renders them; our aim reader must handle
  abilities whose "targets" are path cells, not units (Phase 3 detail, verify live).

### 1.7 Exit-battle popup (`ExitBattlePopupVM`)
`IsActive` (on `IEndSpaceCombatHandler`), ship XP block (`CurrentLevel, ExpLevel, LevelDiff,
GainedExpAmount, CurrentExp/PrevExp/NextLevelExp/ExpRatio, IsUpgradeAvailable`), rewards
(`ScrapVM (ScrapRewardSlotVM)`, `ItemsSlotsGroup (ItemSlotVMs)`, `CargoRewards`, `HasItems/
HasCargo`, `UpdateRewards` command), actions: `ExitBattle(forceOpenVoidshipUpgrade)` —
`true` = jump straight into the voidship upgrade window (our `ShipCustomizationScreen`
already covers that window).

---

## 2. Reuse map (mod subsystem → verdict)

| Subsystem | Verdict |
|---|---|
| `TurnCues` / turn polling (`CombatEvents`) | **Fires already** (`Player.IsInCombat`, shared TurnController). Audit wording (ship names, torpedo turn, no "MP" jargon), don't rebuild. |
| `LogTap` narration | Mode-agnostic; expected to voice space attack lines. Verify channel coverage vs `IStarshipAttackHandler` detail (sector/shield loss) in Phase 0. |
| `ActionBarNodes` (availability-first slot readout) | Direct reuse — ship weapons/posts/abilities are `ActionBarSlotVM`s. |
| `CommandDispatch` TB move (`TryCreateMoveCommandTB` + confirm) | Direct reuse; reachability check swaps to `Navigation.ReachableTiles` (`PathCell.CanStand`). |
| `AimPointerDriver` (pointer injection) | Direct reuse — `DefaultPointerController` registered for SpaceCombat; `ShipPathNodeMarkersController` and `AbilityRange` read the injected `WorldPosition`. |
| `AimReadTap`/`AimParity` (hit-chance parity) | Reuse; add starship projections (BurstHitChances aggregate, shield prediction, `IsStarshipAttack` branch of the overtip gate). |
| `MapCursor`/`TileExplorer` | Same graph — works mechanically; needs a space describer (ships/soft units instead of walls/cover; no fences in space — verify) and a mode gate audit. |
| `Scanner`/`WorldModel` | Reuse the review-cursor machinery with a space taxonomy (enemy ships, torpedoes, asteroids/meteor zones, vantage points, scrap). |
| `InspectVM` path (K) | `IUnitClickUIHandler.HandleUnitConsoleInvoke` on a `StarshipEntity` — expected to work (space bestiary pages exist); verify live. |
| `ShipCustomizationScreen` | Already ships posts/hull upgrade reads — reuse post-name/localization helpers; the popup's `forceOpenVoidshipUpgrade` lands there. |
| `FogProbe` gates | Space combat has no fog overlay (`FogOfWarArea` absent in ch.1 system scenes) — visibility gate is `IsVisibleForPlayer` only; keep `HideRealHealthInUI` mask. |
| `GameKeybinds` Ctrl+letter relocation | Audit which service windows are legal in SpaceCombat mode; keep relocation consistent. |

**Ungated-misbehavior audit (Phase 1):** confirm what `TileExplorer`, `Sonar`/`WallTones`,
`ExplorationEvents`, `SelectionAnnouncer` do in SpaceCombat mode today and gate what
misfires. (`SpaceEvents` gates on StarSystem — fine.)

---

## 3. Design (following the graph paradigm + house rules)

One new base context **`SpaceCombatScreen`** (`ctx.spacecombat`), `IsActive()` =
`Game.Instance.CurrentMode == GameModeType.SpaceCombat`, layer 0 — mutually exclusive with
`ctx.ingame`/`ctx.systemmap`/`ctx.sectormap` by mode. StartUnfocused (grid verbs own the
bare keys; Tab enters the HUD), one `BeginStop` per zone:

1. **Ship** — hull HP, four sector shields (+armour), crew %, morale %, military rating,
   speed mode + remaining move points, position + facing (compass word), active buffs.
   Browse labels mirror the sighted HUD/overtip; Space = tooltip detail (weapon/ability
   tooltips are `TooltipTemplate*` — `TooltipViewScraper` fallback).
2. **Weapons** — four arc groups (Prow/Dorsal/Port/Starboard, localized group labels),
   slots via `ActionBarNodes`: "«Mars-pattern macrocannon», prow arc, 3 shots, ammo 2/2,
   unavailable — no target in arc" (availability leads, tooltip-faithful numbers). Enter
   arms + enters aim mode. Arsenal variants surface as a nested choice (drive
   `ActiveWeaponIndex`).
3. **Abilities & Posts** — the ship `AbilitiesGroup` (torpedo launch, Push Engines …) then
   the six posts: "Master Helmsman — Abelard, skill 45" (+ "blocked, 2 rounds" when
   `IsPostBlocked`); expand to the post's ability slots.
4. **Battle** — round (+ TimeSurvival rounds-left), initiative order (flat frozen list,
   `InitiativeTrackerVM` recipe), End Turn action (`TryEndPlayerTurnManually`, gate on
   `CanEndTurn`), and the "actions remaining" nudge (`CombatActionsCount`).

**Grid verbs (unfocused, same keys as surface combat):** tile cursor scan (arrows), U
battlefield scanner (ships/torpedoes/hazards/vantage points, nearest-first), K inspect,
movement plan/commit on the cursor (below), aim mode per armed weapon.

**Movement UX (the genuinely new interaction):** riding `Navigation.ReachableTiles`:
- Cursor on a reachable cell speaks: standable or pass-through, path length in tiles /
  remaining budget, resulting facing (`PathCell.Direction` compass), turns used, and phase
  warnings ("push phase", "must stop here", "dead end — 1-tile escape moves only").
- A "movement summary" verb groups `GetEndNodes()` by resulting facing: "ahead 4–7 tiles;
  45° port turn at 3; 45° starboard at 5" — the audible equivalent of the sighted
  path-marker fan.
- Commit = existing two-step (`MoveSelectedUnitsToPoint` preview → confirm), warnings pass
  through `WarningReader`.
- Speed: announce speed mode at turn start; expose speed-mode change wherever the game's
  own UI does (Phase 0 question — abilities vs direct setter; prefer the game's own path).

**Torpedo turns:** when `IsPlayerTurn && CurrentUnit != PlayerShip`, the screen re-labels
context ("Torpedo salvo, 2 rounds left") — same movement verbs + its ability group
(self-destruct); rounds-left via the `SummonedTorpedoesBuff` rank.

**Aiming:** arm weapon slot → `Targeting` aim flow: cycle **legal** targets only
(arc-gated via `IsTargetInsideRestrictedFiringArc`, parity-gated via the starship branch of
the overtip's `CalculateCanHit` + `IsVisibleForPlayer` + `HideRealHealthInUI`), speak
"Chaos raider, fore sector, 4 tiles — 62/55/48% burst, damage 8–14, shields 20 → 6". Fire =
`ClickWithSelectedAbilityHandler.SetAbility` + `OnClick` (the proven mouse-mode-surviving
funnel). Custom-path movement abilities (Ram/Strafe/Swing Run) present their
`CustomPathNode` sets as cell targets.

**Event narration:** keep the log as the single source of truth (LogTap). Add typed
handlers ONLY for what the log provably lacks (candidates: `IShieldAbsorbsDamageHandler`
sector cue on the player ship, `IStarshipPostHandler` post-blocked, `ITorpedoSpawnHandler`,
speed/phase transitions) — decided by the Phase 0 log transcript.

**Exit popup:** `ExitBattlePopupScreen` (layer ~21, Exclusive) — ship XP ("gained 120 XP,
level 2 → 3, upgrade available"), rewards (scrap, items via `ItemNodes`, cargo), actions:
Continue (`ExitBattle(false)`) / Upgrade voidship (`ExitBattle(true)`, gated on
`IsUpgradeAvailable`).

**Parity gates:** never speak enemies `!IsVisibleForPlayer`; keep `HideRealHealthInUI`;
browse labels mirror the card, tooltip detail on Space; all mod strings through
`Localization` (`spacecombat.*` table), game strings passed through
(`UIStrings.SpaceCombatTexts` has post/arc/hint strings ready).

---

## 4. Phases

**Phase 0 — live verification (dev harness, needs a save at the first space battle).**
Checklist: SpaceCombatVM tree paths resolve; which mod systems already fire (turn cues
wording, LogTap transcript of a full fight — capture `speech_log.txt`); keybinds active in
the mode (F10 dump; does "EndTurn" bind exist on keyboard? bare-Space behavior); what
`TileExplorer`/`Scanner`/`Sonar` currently do (misbehavior list); inspect (K) on an enemy
ship; `ReachableTiles` read + a scripted move commit; `AimPointerDriver`+`AimReadTap` with a
prow macrocannon (does `ICellAbilityHandler` fire; `AbilityTargetUIData` starship fields);
speed-mode change path in the sighted UI; vantage-point semantics; vertical console view
(`SpaceCombatUnitOrderVerticalConsoleView`) for anything the PC HUD hides.

**Phase 1 — follow a battle (legibility). SHIPPED + LIVE-VERIFIED 2026-07-12.**
- `Screens/SpaceCombatScreen.cs` — `ctx.spacecombat` base context, gated on the game's own
  SpaceCombat HUD component (`SpaceStaticPartVM.TryGetComponentVM(SpaceCombat)` — survives
  dialogs over the battle exactly like the sighted HUD; `SystemMapScreen.IsActive` now
  excludes it for in-system encounter fights). Two Tab stops: **Ship** (hull, per-sector
  shields + armour, speed mode + movement + facing, crew/morale/military rating, effects)
  and **Battle** (round + TimeSurvival rounds-left + whose-turn + the must-move explanation,
  End Turn, the actions-remaining nudge, initiative order via the shared
  `InGameScreen.InitiativeLabel`/`RoundDividerLabel` — made internal — and the log button).
  UI input category only (grid cursor is Phase 2); StartUnfocused.
- Turn cue (`CombatEvents.TurnCue`): starships read "Your turn, X, movement 11, Normal
  speed" (no yellow-AP noise; covers torpedo salvos too).
- Round dedupe (INVERTED 2026-07-12 by user call): the game's "Раунд N" banner
  (`InitiativeTrackerView.RoundChanged`) is KEPT — it's already localized in the game's
  language — and `CombatEvents`' own `combat.round` cue was removed instead (round 1 has no
  toast but coincides with "Battle begins"; the battle status line carries the labelled
  round). `IsTurnChromeToast` now drops only the coop YouTurn banner (our whose-turn cue is
  richer). (TimeSurvival's bare-number Counter toast left audible — not provably round-only.)
- `Accessibility/SpaceCombatEvents.cs` — `IShieldAbsorbsDamageHandler` speaking the PLAYER
  ship's per-sector shield hits ("starboard shields 64 of 70"), the one attack detail the
  log omits; enemy sectors stay un-cued (parity). Subscribed in Main.
- Locale: `spacecombat.*` + `combat.your_turn_ship` in enGB ui.json.
- Live transcript proof: single "Round 2", enemy volleys read "uses macrocannons →
  starboard shields 68/66/64 of 70 → attacks 2 times, 4 shield damage", ship turn cue with
  movement+speed; all zone lines render (hull/shields/armour/speed+facing/crew/effects/
  battle status "must keep moving, 11 movement left"/initiative rows with faction+HP+order).

**Phase 2 — move the ship. SHIPPED + LIVE-VERIFIED 2026-07-12** (in-harness, ScenarioSpaceCombat_1:
`MoveSummary` → "Move options: ahead 9 to 11 tiles; 45 degrees starboard 9 to 11 …" (7 facing
groups, no reverse — consistent with inertia); `Preview` far cell → "Reachable, costs 11 of 11
movement, arrives facing north, turn can end there", near cell → "…then must keep moving, 10
movement left", raw non-metagrid cursor node quantized correctly (arrives facing north-east),
origin → "You are here."; Z handler spoke the fan end-to-end; `CommandDispatch.MoveTo` on the
picked end node moved the ship and `EndTurn` closed the turn. Round inversion proven the same
fight: game's "Раунд 2" toast read ONCE in the game's language, no mod "Round 2" anywhere.)
- `Exploration/ShipPathInfo.cs` — the starship counterpart of `PathInfo`: `Preview(ship, dest)`
  reads the game's own `Navigation.ReachableTiles` (kept fresh per-tick by the game's
  `StarshipPathController`), quantizes the cursor tile to the ship's metagrid cell via the
  game's own `GetNodeInMetagrid` (publicized private — the exact call inside `FindPath`, so
  the spoken verdict always matches a commit), and speaks cost / budget / ARRIVAL FACING /
  stop-legality (pass-through cells) / the end-turn consequence (finishing-window law:
  "then must keep moving, N left" vs "turn can end there") / the dead-end state.
  `MoveSummary(ship)` groups `GetEndNodes()` (the legal final positions = the sighted marker
  fan) by turn relative to the current heading: "ahead 4 to 7 tiles; 45 degrees starboard …".
  Direction convention decoded: grid dirs 0=S 1=E 2=N 3=W 4=SE 5=NE 6=NW 7=SW → mapped onto
  the shared Compass8 words; delta clockwise = starboard.
- `PathInfo.Preview` delegates to it for any acting `StarshipEntity` (torpedo turns included —
  same navigation part), so the tile cursor's Backspace arm/commit two-step works verbatim
  in space (commit already ship-proven via `TryCreateMoveCommandTBShip`).
- `TileExplorer.ReadMoveSummary` (bare **Z**, new `path.summary` action, Exploration
  category) — ships get the facing-grouped fan, surface units get `PathInfo.MoveAreaSummary`
  (previously unwired). `ReadVantage` (Semicolon) gained a starship branch: cover/threat is
  surface tactics, so ships speak the path verdict at the cursor instead.
- `SpaceCombatScreen` now declares the same category flip as `InGameScreen`
  (Exploration-first unfocused / UI-first focused / none without world control), so the tile
  cursor + scanner + move verbs are live in space combat; `BattleStatusLine` gained the
  dead-end tail (`StarshipPathController.IsCurrentShipInDeadEnd`).
- Deliberately NOT built: speed-mode control (ability-driven — §0b), push-phase per-cell
  warnings (current-phase is readable off the speed line; revisit if players ask).
- ADDED 2026-07-12 (user request, after the hologram question): **the planned-move preview**.
  Engine facts settled first: ships have NO hover simulation (the surface
  `UnitPathManager→SetVirtualHoverPosition` feeder is dormant in space; `ShipPathManager` never
  touches the prediction manager — the HoloSim ship gate is correct parity), but they DO have
  the sighted two-click planned-move hologram (`TryCreateMoveCommandTBShip(showMovePrediction:
  true)` → destination ghost + path line + `SetVirtualMoveCommand`; the facing math even
  special-cases 1×2 frigates; while pinned, `UpdateVirtualLoSPosition` writes the virtual
  position). The mod now rides it byte-for-byte (`CommandDispatch.ShipMoveStep`): the Backspace
  ARM press plants the game's preview (ghost for the co-pilot + virtual position/facing), the
  second press hits `SamePath` → `TryRunVirtualMoveCommand` (the game's confirm), a fresh arm
  drops any stale pin first (`CancelMoveCommandLocal` — a lapsed window can never instant-commit),
  and the game's own Esc cancel stays wired. `StarshipAim` switched to
  `CanTargetFromDesiredPosition` + `GetDesiredPosition` for the prediction point, so cycling
  enemies while a move is armed answers arcs/odds FROM THE PLANNED CELL WITH THE ARRIVAL HEADING
  — the sighted preview workflow, complete. (Compile-only at ship time.)
- ADDED 2026-07-12 (user feedback after live testing): **per-tile reachability on the cursor
  browse** — the ground cursor's additive "unreachable" note rode
  `UnitMovableAreaController`, which never runs for starships, so space tiles browsed
  silent. `ShipPathInfo.TileReachabilityWord` (metagrid-quantized `ReachableTiles` read) now
  feeds `DescribeTile` §2b during a ship's player turn: silent = can move here and stop
  (like ground), "pass-through only" = the inertia fan crosses but can't stop,
  "unreachable" = can't be entered this turn. Suppressed while aiming (the aim readout owns
  the cursor). Compile-only at ship time.

**Phase 3 — fight. SHIPPED + LIVE-VERIFIED (core loop) 2026-07-12.**
- `Combat/StarshipAim.cs` — the ship sibling of `HitPredictor`: refusals via the game's
  `CanTarget` (starship arc failure surfaces as `HasNoLosToTarget` → spoken "out of firing
  arc"; range → "out of range"), odds via `RuleStarshipCalculateHitChances` + the overtip's
  own `GetStarshipDamagePrediction` under `DisableStatefulRandomContext` (new csproj ref:
  `StatefulRandom.dll`). Line: "80% to hit, 5% crit, 2 shots, shield damage 21 to 42,
  shields 35 of 35, hull damage 0 to 3[, can kill]" — ship bursts share ONE hit chance
  (`AbilityTargetUIData.UpdateWithStarshipWeapon` fills no per-shot list; shots =
  `DamageInstances`). Fog parity + `HideRealHealthInUI` on the kill flag.
- `HitPredictor.Describe` delegates to it for ship-vs-ship weapon aims → the scanner's
  Period target-cycle + O breakdown speak ship odds unchanged; `AimRead.CursorReadout`
  ship branch reads the ship under the tile cursor directly (the cell-ability broadcast is
  not the source for ships).
- `SpaceCombatScreen` gained **Weapons** (four arc groups; slot readout = availability →
  name → reason → ARC + shots → generic detail; arc word = the game's own group label
  passed through) and **Ship abilities** Tab stops — all `ActionBarNodes.Slot`, so arsenal
  variants ride the standard convert flyout (`MechanicActionBarShipWeaponSlot.
  GetConvertedAbilityData`), Enter arms into the existing aim flow (arm announce verified
  live: "Aiming <lance>, range 6 cells …"), Escape closes open flyouts first.
- LIVE-PROVEN act loop (ScenarioSpaceCombat_1): battle-start refusals correct per weapon
  (arc vs range); after moving adjacent — dorsal + starboard read full odds; fired the
  starboard battery via `CommandDispatch.UseAbilityOnUnit` → shields 140→109 (31 ∈
  predicted 21–42) + 9 hull, log narrated both lines.
- Deferred to live encounters that have them: torpedo launch/self-destruct + torpedo-turn
  weapons (no torpedoes owned — MasterOfOrdnance empty), ammo/reload readout (this ship's
  weapons carry no ammo — factory already handles `IsReload`/`CurrentAmmo`), custom-path
  ability commit (Ram/Swing Run arm fine; cell-target commit needs a live Ram pass),
  arc-gated *filtering* of the target cycle (deliberately NOT filtered — matches surface:
  every enemy cycles, out-of-arc ones say why, so the player still finds all threats).

**Phase 4 — crew. SHIPPED 2026-07-12 (compile-only — live pass pending, the maintainer was
play-testing on the shared instance).**
- SpaceCombatScreen **Posts** Tab stop: per post a header line — the game's position-keyed
  post name (`UIStrings.SpaceCombatTexts.GetPostStrings(index)`, the ShipCustomizationScreen
  recipe), officer + associated-skill name/value (`LocalizedTexts.Stats.GetText`), "vacant",
  "blocked, N rounds" (VM `IsPostBlocked`/`BlockDuration`), "penalty" (`Post.HasPenalty` =
  skill < 1) — followed by that post's ability slots (same `AddShipSlot`/aim flow as
  weapons). Reads the VM's private `m_Post` (publicized).
- `SpaceCombatEvents` implements `IStarshipPostHandler`: "{post} post blocked" /
  "{post} post free again" cues (player ship only; nothing reaches the combat log — the
  sighted cue is a lock overlay). Unblock is announced only for a block we announced
  (`HandleBuffDidRemoved` fires for EVERY post-buff removal — tracked via a small set).
- Ship buffs review = the Ship zone's effects line (Phase 1) — no extra surface.
- Live checks pending: post header wording against a real fight, a real block event
  (boarding/fire crits), post ability cast through a post slot.

**Phase 5 — win. SHIPPED 2026-07-12 (compile-only — needs a won battle for the live pass).**
- `Screens/ExitBattlePopupScreen.cs` — layer 21 Exclusive modal on `ExitBattlePopupVM.IsActive`
  (a SpaceCombatVM child, flipped by `IEndSpaceCombatHandler`). One wrapped list: XP line
  ("Gained {n} ship experience, level {a}[, can advance to level {b}], experience {cur} of
  {next}"), scrap ("Scrap: {n}", glossary tooltip on Space), item rewards (ItemLabel +
  card tooltip via `ItemNodes.OpenItemTooltip`, made internal), cargo rewards (card parity:
  the game's own origin label via `UICargoTexts.GetLabelByOrigin` + fill % + count; template
  tooltip on Space), then the popup's own buttons with the game's own strings — OK
  (`UIStrings.SettingsUI.DialogOk` → `ExitBattle(false)`) and Attune
  (`UIStrings.ShipCustomization.Attune` → `ExitBattle(true)` → ShipCustomizationScreen),
  the latter only while `IsUpgradeAvailable` (matching the sighted button's SetActive).
- Rewards are display-only (the game already granted them when the popup shows).
- LIVE-VERIFIED 2026-07-12 (the user's post-kill quicksave restored with the popup ACTIVE):
  screen announce + focus landing spoke "Battle won, list, Gained 1500 ship experience,
  level 1, can advance to level 2, experience 3160 of 7000, 1 of 3"; the user hand-navigated
  ("Ок, button, 2 of 3") and exited via OK — battle closed to the star system cleanly.
  Still pending: the Attune exit path, item/cargo reward rows (this fight dropped none).
- Feedback applied: the OK/Attune buttons moved to their OWN Tab stop — Tab jumps
  rewards ↔ buttons, no arrowing through the reward list to close.

**Phase 6 — mastery polish. EXPLORED + PARTIALLY SHIPPED 2026-07-12.**
- Scene census (in-harness, ScenarioSpaceCombat_1 mid-battle save): the arena is BARE —
  0 map objects, 0 area effects, 0 destructibles, 0 unwalkable cells in an 81×81 sample;
  only the three ships exist. `PartVantagePoints` is present on the player ship but the
  scenario places no vantage content; no TimeSurvival component. **Scanner taxonomy for
  space (asteroids / hazards / meteor streams / vantage points) therefore has nothing to
  classify yet — DEFERRED until a later encounter carries such content** (the enemy-ship
  cycle already works and carries the arc/threat lines).
- SHIPPED (compile-only): the end-of-turn nudge now rides the End-turn button itself — a
  LIVE announcement part speaks "…, N actions still available" while focused (the spoken
  twin of the sighted pulsing highlight; replaces the separate label row). TimeSurvival
  rounds-left cue: on each round advance in a "survive N rounds" area, CombatEvents
  follows the game's bare-number toast with the labelled "{n} rounds left" (self-gated on
  the TimeSurvival component; silent everywhere else — untestable in scenario 1).
- Also reconciled post-rebase: HoloSim gated off during starship turns (ships have no
  hover-sim mechanic; ShipPathInfo owns the ship-path readout).
- Posts census: 6 posts enumerate (SupremeCommander + MasterOfOrdnance carry 1 ability
  slot each on this ship; the rest 0) — the Phase 4 zone will render them; wording check
  still needs an in-battle look.
- PULLED FORWARD 2026-07-12 (user feedback after live testing): **arc awareness on the enemy
  browse tail** (`ProxyUnit.CombatSuffix`, space combat only) — `StarshipAim.OurArcsLine`
  ("Prow, Dorsal bear on it" — the panel's own group labels; per-weapon `CanTarget`, so
  arc + range) and `StarshipAim.ThreatArcsLine` ("it can fire on you: fore, dorsal" —
  direction words per the enemy's slot types; derived from visible facing + inspectable
  loadout, parity-safe). Cycling enemies now doubles as picking the right battery AND
  reading incoming threat. Compile-only at ship time (shared instance was in use).
- HOTKEY FINDING (user question): sighted space combat has NO slot hotkeys — the space
  slot views (`WeaponAbilitySlotPCView`/`ShipAbilitySlotPCView`) never call
  `ActionBarSlotPCView.SetKeyBinding` (only surface bar views do); the mode's only bound
  keys are EndTurn (Space) + pause. Mouse-only for sighted players, so the mod's Tab zones
  are at parity; mod-own quick keys would be an accessibility EXTRA if ever wanted.

---

## 5. Open questions (carry into Phase 0)

1. Speed-mode control: where does the sighted PC UI change `SpeedMode`? (No panel found in
   the VM sweep; cheat + setter exist. Suspect post abilities cover it; else drive the
   setter guarded by the game's own rules.)
2. Does the combat log carry starship attack detail (hit location, shield loss) or only
   summary lines? Decides the `IStarshipAttackHandler` postfix.
3. `ICellAbilityHandler`/`AbilityTargetUIData` behavior for starship weapons at our injected
   pointer — burst list population, and whether `AimParity`'s surface projections need a
   starship branch.
4. Torpedo-turn control surface: what commands does a torpedo unit accept (move + which
   abilities), and what does `SpaceCombatTorpedoPanelPCView` show that we must mirror?
5. Vantage points: what bonus, how displayed to sighted players (overtip? tile decal?).
6. Fences/obstacles on the space grid: are asteroids unwalkable nodes, units, or fences?
   (Affects tile-cursor describer + `WallTones`.)
7. Enemy movement arcs (`SpaceCombatCircleArcsVM`): is there readable "planned move"
   data worth voicing during enemy turns, or is it purely cosmetic?

---

## 6. References
- `HOOKMAP.md` SUBSYSTEM 7 (starship entity/turn/arcs/shields/posts/attack-rule map).
- Surface combat: `docs/plans/phased-skirmishing-liskov.md` (+`-apis.md`),
  `piloted-aiming-lamport.md` (aim architecture this plan extends).
- Space surfaces: `docs/space-maps-ui-research.md`, `docs/plans/orbital-listing-wilkes.md`
  (SpaceVM tree conventions, `SpaceEvents`).
- Key decompiled types (all under `decompiled/Code/`): `Kingmaker.Code.UI.MVVM.VM.SpaceCombat*`,
  `Kingmaker.UI.MVVM.View.SpaceCombat.*`, `Warhammer.SpaceCombat.StarshipLogic.*`,
  `Kingmaker.SpaceCombat.StarshipLogic.Parts.*`, `Kingmaker.Controllers.SpaceCombat.*`,
  `Kingmaker.Pathfinding.ShipPath`, `Kingmaker.Controllers.Units.UnitCommandsRunner`,
  `Kingmaker.RuleSystem.Rules.Starships.*`, `Kingmaker.Code.UI.MVVM.VM.ExitBattlePopup.
  ExitBattlePopupVM`, `Kingmaker.GameModes.GamesModeFactoryFacade`.
