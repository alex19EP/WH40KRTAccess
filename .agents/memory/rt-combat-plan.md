---
name: rt-combat-plan
description: "Turn-based combat accessibility — RT combat-model facts, the act/read API funnels + gotchas, the piloted-aiming architecture, and the HideRealHealthInUI parity rule"
metadata: 
  node_type: memory
  type: project
  originSessionId: 2f822d1e-f036-4c1c-bca4-a9d4bc347439
---

Turn-based combat a11y. Plans: `docs/plans/phased-skirmishing-liskov.md` (+ `-apis.md`),
`docs/plans/patterned-blasting-hamming.md` (AoE), `docs/plans/positional-deploying-dijkstra.md` (deployment),
`docs/plans/piloted-aiming-lamport.md` (aiming). Broadly shipped + live-verified; open items at the end.

**RT combat model facts:** attacks ARE abilities (`PlayerUseAbilityParams`, there is NO `UnitAttack`); **no
delay-turn**; **no `PathVisualizer`** (WOTR-only). AP = `ActionPointsYellow` (int), MP = `ActionPointsBlue`
(float, max `ActionPointsBlueMax`).

**ACT — drive the game's own funnels** (`Combat/CommandDispatch.cs`):
- Unit ability: `ClickWithSelectedAbilityHandler.SetAbility(ability)` then `OnClick(view.gameObject, pos, 0)`
  (survives the mouse-mode gate [[rt-mouse-mode-engine-gate]]). Point/AoE: `TryUnitUseAbility(Vector3
  TargetWrapper)`. Do NOT hand-build `PlayerUseAbilityParams`+`Commands.Run` — `IsDirectionCorrect` NREs; use
  SetAbility+OnClick or `UnitCommandsRunner.TryUnitUseAbility`.
- Move (TB): `unit.TryCreateMoveCommandTB(new MoveCommandSettings{Destination=node.Vector3Position,
  DisableApproachRadius=true}, false, out status)` + `unit.Commands.Run(cmd)`. See [[rt-tile-explorer]] for the
  exploration-vs-TB move split.
- `ActingUnit` guard: TB + IsPlayerTurn + `SelectionCharacter.SelectedUnit == CurrentUnit` + IsDirectlyControllable.
- Deploy: commit = `ClickSurfaceDeploymentHandler.CanDeployUnit` gate + `unit.Commands.Run(new UnitTeleportParams(
  node.Vector3Position, isSynchronized:true))` on `SelectionCharacter.SelectedUnit` (NOT the click handler — NPEs
  on a synthetic click); finish = `TurnController.RequestEndPreparationTurn()` gated on `CanFinishDeploymentPhase()`;
  budget = `BlueprintCombatRoot.DistanceInPreparationTurn`; zone = `CurrentUnitMovableArea` minus
  `DeploymentForbiddenArea`; pet co-teleports.
- Start battle = **SPACE** (2026-07-12; was bare B, retired): the game's OWN key — `Game.EndTurnBind()` routes Space
  to prep-end (sound + `RequestEndPreparationTurn`); sighted CombatStartWindow has NO other hotkey, its disabled-button
  hint = `UIStrings.TurnBasedTexts.CannotStartbattle` (spoken by `DeploymentMode.StartBattle` on refusal — the game is
  SILENT then). Plumbing: InGameScreen advertises `ActionIds.Space` gated `DeploymentMode.Active` (the loot-screen
  mechanism) + `ui.tooltip.space` got `ClaimsWhenUnfocusedIf(Current.HasAction(Space))` — REQUIRED because deployment
  blurs the HUD and a yield-marked chord dispatches-but-doesn't-claim when unfocused: without the claim the game's
  handler double-fires alongside ours. Claim predicate = the advertisement check, so they can't drift.

**READ — pure/dry-run rules** (reveal only what a sighted player sees — [[rt-visual-parity]]). Hit-prediction
recipe = `LineOfSightVM.UpdateHitChance` verbatim: `VirtualPositionController.GetDesiredPosition` →
`AoEPatternHelper.GetGridNode` → `ability.CanTargetFromNode` → `GetBestShootingPositionForDesiredPosition` →
`AbilityTargetUIDataCache.Instance.GetOrCreate(ability,target,shootPos)` [the SAME cache the reticle reads] →
`HitWithAvoidanceChance`; crit is separate — `new RuleCalculateHitChances(caster,target,ability,0,shootPos,
target.Position)` + `Rulebook.Trigger` + `.ResultRighteousFuryChance`.
- **Invariant:** always anchor validate/preview/commit on `VirtualPositionController.GetDesiredPosition` /
  best-shooting-position (top correctness trap: `Caster.Position` drifts preview≠commit for predicted-position abilities).
- **`AbilityData.CanTargetFromNode` cover gotcha (AbilityData.cs:1637):** its `out los`/cover param is HARDCODED to
  `CoverType.None` and never reassigned (LOS tested via a separate `HasLos` bool at :1711 that never flows out) → you
  CANNOT read cover from it. Use its BOOL only for range/targetability; when the bool is false, speak the game's own
  localized `GetUnavailabilityReasonString(reason, casterPos, target)` (many refusal reasons, not just range). Compute
  cover the way the game overtip does: `GetBestShootingPosition(GetDesiredPosition(me), me.SizeRect, target.Position,
  target.SizeRect)` → `LosCalculations.GetWarhammerLos(best, me.SizeRect, target).CoverType`. `CoverType` =
  None/Half/Full/**Invisible**(=no LOS).
- `AbilityTargetUIData` is a STRUCT → `Cache.Instance?.GetOrCreate(...)` yields `Nullable<>` (CS1503); null-check the
  CACHE not the result. Its members are PROPERTIES not fields. `HitAlways==true` → "sure hit" (hit%=0 sentinel).
  Reticle flags: `.CanDie` (possible-to-kill), `.CanPush` (knockback).
- AoE: use `GetPattern` (pure) NOT `GatherAffectedTargetsData` (mutates execution-context); caught units via
  `AoEPatternHelper.WouldTargetEntity(pattern,u)`. Movement preview: MP cost via `RuleCalculateMovementCost`; AoO via
  `unit.CalculateAttackOfOpportunity(IEnumerable<GraphNode>)`.

**Turn lifecycle = POLL each frame** (NOT EventBus — turn/round handlers are entity-targeted, unreliable for a global
subscriber): combat start/end (`Player.IsInCombat`), round (`TurnController.CombatRound`), whose-turn (`CurrentUnit`
change — dedup on `MechanicEntity.UniqueId` STRING, not ReferenceEquals, because CurrentUnit re-wraps the same unit
each frame), deployment (`IsPreparationTurn`). Ordered queue: "Battle begins" must precede "Your turn". MP reads 0 on
the very first turn (real game state, matches HUD).

**PILOTED AIMING — the current aim architecture** (`Combat/Aim*.cs`; supersedes re-deriving numbers in
HitPredictor/AoEPreview while armed). Drive the game's OWN aim pipeline at our keyboard cursor and read the
affected-target list it builds — gets overpen chain, AoE friendly-fire, faction gating, cover, ricochet
correct-by-construction.
- **Inject** = Harmony POSTFIX on `PointerController.Tick` (`AimPointerDriver`): while `Targeting.Aiming`, set
  `ClickEventsController.WorldPosition = MapCursor.Position` + `PointerOn = unit?.View.gameObject` via publicized
  private setters. Postfix runs after Tick's raycast write → last write before `AbilityRange.Update()` reads it
  (that ticks every frame while armed, NO IsControllerMouse gate). A naive one-shot WorldPosition write is stomped
  every BeginOfFrame (refuted); cursor-warp rejected (BaseCursor.Position re-clobbered from hardware).
- **Read** = subscribe our own `ICellAbilityHandler` (`AimReadTap`), snapshot the reused `List<AbilityTargetUIData>`.
  NOT the overtip VMs: `OvertipHitChanceBlockVM` updates via `.ObserveLastValueOnLateUpdate()` (deferred over multiple
  frames) and is gated on on-screen + isHover||isAoETarget; single-target overtip is DEAD in forced controller-mouse
  mode (lights only via IsMouseOverUnit → SurfaceMainInputLayer). Our keypress readout must answer in ONE frame — only
  the source list has it then; it also carries per-entry detail the VM drops. `AimRead.RefreshAtCursor()` forces one
  synchronous `SetRangeToWorldPosition` on the active range so the tap is current on the step frame.
- **Parity gate** (`AimParity.cs`): voice ONLY entries passing the game's own `OvertipHitChanceBlockVM.CalculateCanHit()`
  (reads UnitState.Ability + our injected WorldPosition + CanTargetFromDesiredPosition/IsUnitRicochetTarget — NOT the
  visibility pipeline, so callable directly) AND the fog gate (`InCombat && IsVisibleForPlayer && !dead`). VM-primary
  with a VM-independent StaticGate transcription fallback. Project values with the overtip's exact transforms
  (ricochet/redirect damage→0 mask). **Friendly fire = the subset of legal-hit entries whose Target is an ALLY** (NOT
  "entries failing the faction gate" — that inverts it). `wrapper.IsPlayer == entity.IsPlayerFaction` (the VM's Ally
  IsPlayer-branch is a no-op).

**HUD parity (no-leak):** the ONE un-mirrored masking rule across the whole mod is **`HideRealHealthInUI`** (the
game's "???" concealed-HP mask; fog-INDEPENDENT, so IsVisible gating misses it). Read via
`_unit.HasMechanicFeature(MechanicsFeatureType.HideRealHealthInUI)` (ext in `Kingmaker.UnitLogic`, enum in
`Kingmaker.UnitLogic.Enums`). Fog gating otherwise sound — `Entity.IsVisibleForPlayer` folds in `!IsInFogOfWar`.
**Starship combat is a SEPARATE subsystem** (`Kingmaker.UI.MVVM.View.SpaceCombat.*` + `Warhammer.SpaceCombat.*`), NOT
the personal-combat SurfaceActionBar the mod mirrors — accessible ship combat is a separate project of similar size.
API when it happens: `AbilityDataHelper.GetStarshipDamagePrediction(ship, ship.Position, ability,
ability.StarshipWeapon).resultShields`.

**Event narration = the game log is authoritative** — one Harmony postfix on `LogThreadBase.AddMessage` (shipped as
universal `Accessibility/LogTap.cs`, see [[rt-message-log]]); CombatEvents' damage/heal/death EventBus handlers were
retired, the buff reconciler kept (the log has no buff-REMOVAL thread). Render any proxy to its spoken string without
navigating via `AnnouncementComposer.Compose(proxy, proxy.GetFocusAnnouncements()).Resolve()`.

**Shipped:** turn/round/deployment cues; log narrator; `CommandDispatch` (execution-proven); HitPredictor;
action-bar reads (range/target-kind/uses/why-disabled off `ActionBarSlotVM.AbilityData`); targeting (aim→cycle→fire),
AoE cell targeting + friendly-fire; deployment placement + vantage (bare-V); battlefield scanner (U) + move preview.
**Open:** route `Targeting.PredictLine` (scanner cycle) + HitPredictor canKill/canPush/ricochet through AimParity so
scanner-cycle can't drift; 4 recovered aim elements (ricochet / chain-lightning / virtual-caster "firing-from" /
overpen-cap); low-value polish (turn-skipped/surprise cues, flat initiative-list enrichment).
Related: [[rt-a11y-architecture-verdict]] [[rt-mouse-mode-engine-gate]] [[rt-tile-explorer]] [[rt-inspect-contract]]
[[rt-object-interaction]] [[wotr-access-prior-art]] [[rt-dev-harness]] [[rt-message-log]] [[rt-input-system-verdict]].
