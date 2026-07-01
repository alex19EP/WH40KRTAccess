# Combat accessibility — porting plan (turn-based combat → screen-reader)

Plan name: `docs/plans/phased-skirmishing-liskov.md`. Companion reference with the full
per-facet verified-API digest: `docs/plans/phased-skirmishing-liskov-apis.md` (the raw
output of an 8-agent decompiled-source audit; cite it for exact signatures/line numbers).

This plan supersedes the "Combat (turn-based)" and "Event system" rows of
`docs/wotr-access-steal-report.md` and builds on the shipped surface HUD
(`Screens/InGameScreen.cs`), review buffers (`Buffers/`), the always-active grid cursor
(`Accessibility/TileExplorer.cs` + `Exploration/MapCursor.cs`), and the combat-event
pipeline (`Accessibility/CombatEvents.cs`, `Accessibility/WarningReader.cs`).

---

## 0. Executive summary

RT combat is **turn-based only** on a **square `CustomGridGraph`** (≈1.35 m cells) with a
two-pool economy: integer **AP** (`ActionPointsYellow`, spent by abilities/attacks) and float
**MP** (`ActionPointsBlue`, spent by movement). Every fact and action a blind player needs is
reachable through a small set of **verified, side-effect-free** funnels:

- **Turn/state** — `Game.Instance.TurnController` (whose-turn / round / TB-mode) raises typed
  EventBus handlers on combat-enter/leave, round start/end, per-unit turn start/end, deployment.
- **Reading a decision** — `AbilityData` (range, cost, cooldown, charges, `CanTargetFromNode` →
  distance+LOS+cover+reason), `AbilityTargetUIData` + `RuleCalculateHitChances` (hit% / damage /
  cover / dodge / parry / crit — the same numbers the sighted overtip shows), `GetPattern()` (AoE
  cells + who's caught), `LosCalculations` (cover/LOS), pathfinding (`FindAllReachableTiles_Blocking`,
  `RuleCalculateMovementCost`) for reach/cost. **All pure — callable without entering targeting.**
- **Acting** — one funnel: build a `UnitCommandParams` and `unit.Commands.Run(...)`; for
  abilities/attacks the game's own `ClickWithSelectedAbilityHandler` (`SetAbility` + `OnClick`)
  runs full validation+warnings+multi-target+approach and, being a pointer-controller call,
  **survives the mouse-mode engine gate**; movement already commits via `TryCreateMoveCommandTB`.
- **Feedback** — the combat log funnels every roll/hit/miss/dodge/parry/crit/save through
  `LogThreadBase.AddMessage(CombatLogMessage)` (already-localized text), a single Harmony tap.

**What already works:** the HUD reads AP/MP + whose-turn + initiative + an End-turn button; the
action bar lists usable slots and self-casts; review buffers read HP/AP/MP/defenses/buffs;
`CombatEvents` voices damage/heal/death/downed/buff; `WarningReader` voices refusals; the grid
cursor **already commits combat moves**.

**What is missing — the reason a blind player cannot fight today** (in value order):

1. **No automatic turn cues.** Nothing says "combat started", "your turn", "enemy turn", "round 2".
   The single most important signal — completely absent. *(cheap: EventBus handlers)*
2. **No attack-resolution narration.** `CombatEvents` speaks damage but never **miss / dodge / parry
   / block / crit** — those live only in the combat log. *(one Harmony tap)*
3. **No targeting.** Activating a targeted ability arms `SetAbility` then **dead-ends** — no way to
   pick or confirm a unit/cell, no AoE preview. *The crux.*
4. **No decision support.** Hit% / expected damage / cover / crit / in-range are never spoken.
5. **No battlefield awareness.** Can't enumerate enemies by distance/bearing/HP/cover/threat.
6. **Rough movement preview.** Only a Chebyshev straight-line tile count — no real path cost, MP,
   reachability verdict, or attack-of-opportunity / overwatch warning.
7. **Thin action-bar reads.** No range, charges/uses, why-disabled, or self-vs-targeted signal.

The work is organized into three milestones — **A: legibility** (follow a fight), **B: the
act-loop** (take a turn), **C: tactical mastery** (fight well) — sitting on one new shared
**`CommandDispatch`** service.

---

## 0b. Live verification status (dev harness, 2026-07-01)

Every load-bearing API in this plan was checked against the running game (`/eval` reflection +
behavioural probes in a live prologue turn-based fight). **Result: the plan is sound; the
decompiled audit was accurate.** Details:

**Confirmed live:**
- **All ~50 load-bearing types/methods/properties resolve with matching signatures** (dispatch,
  targeting, prediction, movement, log, turn-lifecycle, battlefield). All in `Code.dll`.
- **Hit-prediction is live-accurate.** For a real shot (Багардор → servitor) the recipe returned
  **80% hit / 4–8 dmg / 0 cover / crit 25%** — matching the game's own `AbilityTargetUIDataCache`
  (the object the HUD renders from). Crit via `RuleCalculateHitChances.ResultRighteousFuryChance`.
  Use `bestPos = ab.GetBestShootingPositionForDesiredPosition(tw).Vector3Position`.
- **Targeting validation** (`CanTargetFromNode`/`CanTargetFromDesiredPosition`) returns correct
  distance/LOS/cover/reason (dist 5, los None, reason None for a valid shot).
- **Refusal path works end-to-end**: `SelectedAbilityHandler.OnClick` on an invalid target
  (ally-only ability on an enemy) returned `false` and the game's refusal was spoken via
  `WarningReader` ("Может применяться только к союзникам"). So OnClick runs full validation + warnings.
- **Turn machinery works**: `TryEndPlayerTurnManually()` advanced the turn (player→enemy, round
  2→3); combat lifecycle is live and auto-progresses.

**Corrections to fold in (namespaces/signatures):**
- `IUnitMissedTurnHandler`, `IAnyUnitCombatHandler`, `IUnitCombatHandler` are in
  **`Kingmaker.PubSubSystem`** (not `...Controllers.TurnBased`). `IPartyCombatHandler` is in
  `Kingmaker.PubSubSystem.Core` (as stated). `IPreparationTurnEndHandler` is in `...Controllers.TurnBased`.
- `Game.State` is **`Kingmaker.EntitySystem.PersistentState`**; `AllBaseAwakeUnits` is a `List` field.
- `AbilityTargetAnchor` (Owner/Unit/Point) is in **`Kingmaker.UnitLogic.Abilities.Blueprints`**.
- `ClickWithSelectedAbilityHandler.OnClick(GameObject, Vector3, int button, bool simulate, bool
  muteEvents)` — 5 args; call `OnClick(go, pos, 0, false, false)`.

**Refined findings (update the relevant facets):**
- **C5 scanner faction rule (important):** `caster.IsEnemy(u)` is **CombatGroup-relative** — distant
  un-aggroed hostiles (100+ cells) read `foe=false`, and absolute `IsPlayerEnemy` under-reports.
  Enumerate `Game.State.AllBaseAwakeUnits` (the whole area, ~43 units), **filter `u.IsInCombat`
  first**, then segment by relative `caster.IsEnemy(u)`/`IsAlly(u)`; near non-combatant crew
  correctly read as neither. **Also: the mod's recent "world scanner" already emits faction-tagged,
  distance+cover unit enumeration in combat** — so C5 is *partially built*; re-scope it to
  **extend** the existing scanner with combat segmentation/threat/range rather than greenfield.
- **B0 dispatch caution:** do **not** hand-build `PlayerUseAbilityParams` and call
  `Commands.Run`/`CanRun` — `IsDirectionCorrect` (`Ability.IsTargetInsideRestrictedFiringArc(Target.Point)`)
  throws NRE on under-initialized params. Route abilities through the game's own
  `SelectedAbilityHandler.SetAbility` + `OnClick` (validated live) or `UnitCommandsRunner.TryUnitUseAbility`
  (its factory sets `IsSynchronized`, `AllTargets`, raises `IClickActionHandler`). Firing-arc for a
  properly-built target computes fine (`IsTargetInsideRestrictedFiringArc` returned true).

**Open (needs a tiny in-mod test, not async eval):**
- **Definitive proof that a queued ability command *executes* end-to-end.** Strong circumstantial
  evidence it does — the target lost 5 HP (matching the 4–8 pistol prediction) during OnClick
  dispatch, and enemies died across the session. But async `/eval` can't cleanly isolate
  before→fire→after because turns auto-advance faster than the HTTP round-trip and commands buffer
  through `UnitCommandBuffer`. Recommend verifying dispatch with a small DEBUG dispatch that runs
  synchronously inside the mod's `OnUpdate` (or a dev key) and reports the HP/AP delta — the first
  thing to build in B0.
- **Багардор repeatedly showed MP 0/6 at turn start** — confirm whether that's expected (prologue
  character state) or a save artifact.

---

## 1. Where combat stands today (do NOT re-plan)

| Built | File | Covers |
|---|---|---|
| Surface HUD screen | `Screens/InGameScreen.cs` | Status (AP/MP + whose-turn), Action-bar region, Party roster, Combat region (End-turn + initiative list), Windows, Menu |
| Action-bar slot proxy | `UI/Proxies/ProxyActionBarSlot.cs` | reads title/AP/ammo/cooldown/targeting-state; `OnMainClick` self-casts, arms `SetAbility` for targeted |
| Combat events | `Accessibility/CombatEvents.cs` | damage/heal/death/downed/buff (EventBus, per-frame queue, visibility gating, buff reconciler) |
| Refusal toasts | `Accessibility/WarningReader.cs` | `IWarningNotificationUIHandler` ("not enough action points", …) |
| Review buffers | `Buffers/{Buffer,BufferManager,UnitBuffer}.cs` | Alt+arrows: live HP/AP/MP/defenses/buffs; "Selected unit" + "Target" buffers; `FollowLatest` |
| Grid cursor + tile read | `Accessibility/TileExplorer.cs`, `Exploration/MapCursor.cs`, `Accessibility/InteractableDescriber.cs` | always-active cell cursor; occupant/walkable/cover/offset; **already commits TB moves** via `TryCreateMoveCommandTB` + `Commands.Run` (two-press confirm) |
| Object interaction | `Exploration/ProxyMapObject.cs`, `Exploration/Inspect.cs` | `ClickMapObjectHandler.Interact`, `IUnitClickUIHandler` — the pattern combat dispatch reuses |
| Input + speech infra | `Input/{InputManager,InputCategory}.cs`, `Speech/Speaker`, `Settings/` | mod-owned keyboard w/ priority model; interrupt/queue facade; typed settings tree |

---

## 2. Architecture — the funnels

### 2.1 Act funnel — a new `Combat/CommandDispatch.cs` (build FIRST)

Every facet acts **only** through this static service so guards, netcode buffering, and refusal
speech live in one place. Exposed API:

```
UseAbilityOnUnit(AbilityData, BaseUnitEntity target)   // game's own click/confirm path
UseAbilityOnPoint(AbilityData, CustomGridNodeBase, bool approach)  // AoE/grenade/charge
UseSelfAbility(AbilityData)                             // Owner-anchored / toggle
MoveTo(CustomGridNodeBase)                              // TB grid move (existing path)
EndTurn()                                               // TryEndPlayerTurnManually()
Interact(MapObjectEntity)                               // existing ClickMapObjectHandler path
```

**Unit-target abilities/attacks — prefer the game's own path** (verified robust in mouse mode):

```csharp
var h = Game.Instance.SelectedAbilityHandler;   // ClickWithSelectedAbilityHandler (Game.cs:600)
h.SetAbility(ability);                           // enters PointerMode.Ability (…Handlers/ClickWithSelectedAbilityHandler.cs:295)
var v = target.View;
h.OnClick(v.gameObject, v.transform.position, 0);   // resolves TargetWrapper, validates, TryUnitUseAbility, clears mode (:211/:269)
```

`OnClick` runs `ShouldHandleAbilityCastFail` (range/LOS/restrictions → `IWarningNotificationUIHandler`,
already voiced by `WarningReader`), accumulates multi-target, and calls
`UnitCommandsRunner.TryUnitUseAbility` — identical to a mouse click. It is a **pointer-controller
call**, so the `!IsControllerMouse` gate on `SurfaceMainInputLayer` (`[rt-mouse-mode-engine-gate]`)
does **not** touch it. This mirrors the shipped `ClickMapObjectHandler.Interact`/`Inspect` approach.

**Point/AoE targets** (no clean GameObject) — build the wrapper and dispatch directly:
`UnitCommandsRunner.TryUnitUseAbility(ability, new TargetWrapper(node.Vector3Position, orient, null), shouldApproach)`.

**Movement** — reuse the proven TileExplorer path unchanged (`TryCreateMoveCommandTB` +
`Commands.Run`); just relocate it behind `MoveTo` so the guard is shared.

**Central guard** (hand-rolled TB commands bypass the engine guards `UnitCommandsRunner` enforces):
`TurnBasedModeActive && IsPlayerTurn && SelectedUnit==CurrentUnit && unit.IsDirectlyControllable()`.

> **Decision D1 (resolve live):** unit targets via the game's `OnClick(view.gameObject)`; point
> targets via direct `TryUnitUseAbility(Vector3 wrapper)`. Verify each archetype (single melee,
> ranged burst, blast/point, cone, scatter, multi-target, charge) fires correctly in mouse mode
> and that AoE orientation matches (see §6).

### 2.2 Read funnels (all side-effect-free — no targeting mode required)

- **Turn state:** `Game.Instance.TurnController` — `CurrentUnit`, `IsPlayerTurn`,
  `TurnBasedModeActive`, `CombatRound`, `CanEndTurn`; `GetCombatStateOptional()` per unit → AP/MP.
- **Ability facts:** `AbilityData` — `RangeCells`/`MinRangeCells`, `CalculateActionPointCost()`,
  `GetAvailableForCastCount()`, `IsOnCooldown`/`Cooldown`, `TargetAnchor`,
  `GetUnavailableReason(pos)`, `CanTargetFromNode(node,hint,target,out dist,out cover,out reason)`.
- **Prediction:** `AbilityTargetUIData` (hit% / damage / cover / dodge / parry / block) +
  `RuleCalculateHitChances.ResultRighteousFuryChance` (crit) + `AbilityDataHelper.GetDamagePrediction`
  / `GatherAffectedTargetsData` (AoE who's-hit). Reference recipe: **`LineOfSightVM.UpdateHitChance`**.
- **Space/cover:** `LosCalculations` (`GetWarhammerLos`, `HasLos`, `GetBestShootingPosition`,
  `GetCellCoverStatus`), `EntityHelper.DistanceToInCells`, `AttackOfOpportunityHelper.IsThreat`,
  `CombatEngagementHelper.GetEngagedByUnits`.
- **Movement:** `PathfindingService.FindAllReachableTiles_Blocking` (reachable set + per-cell AP
  cost), `WarhammerPathHelper.ConstructPathTo` (cheap per-cell path from the cached set),
  `RuleCalculateMovementCost` (reachability verdict + full cost),
  `UnitMovementAgentBase.CacheThreateningAreaCells` + `PartOverwatch.OverwatchArea` (AoO/overwatch).

### 2.3 Cross-cutting invariants (get these right everywhere)

- **caster position:** validate/pattern/preview/**commit** must all use
  `Game.Instance.VirtualPositionController.GetDesiredPosition(caster)` (pending-move aware), and for
  shooting use `ability.GetBestShootingPositionForDesiredPosition(target.Position)`. Using raw
  `caster.Position` makes the mod's numbers disagree with the sighted overtip and with what a commit
  actually does. This is the #1 correctness trap across targeting/prediction/movement.
- **Speech policy** (`[rt-interrupt-speech-rule]`): automatic/event lines **queue**
  (`interrupt:false`, frame-flushed, arrival order); keypress-driven lines **interrupt**
  (`interrupt:true`). Reuse `CombatEvents`' `_pending`/`Tick` queue so a burst reads as one ordered
  sequence.
- **Visibility gating:** party always reads; enemy/neutral only while `IsVisibleForPlayer`
  (matches `CombatEvents.ShouldRead` and `InitiativeTrackerVM.CheckVisibiltyInTracker`). Fail-open
  on null. Whether a blind player may *optionally* reveal fogged enemies is a settings/design call.
- **Rulebook cost:** the pure preview rules are cached (`TryGetCachedOrTrigger`) but still trigger on
  the main thread — call them at focus/keypress time, never in a silent per-frame loop.
- **No RT `UnitAttack` / no delay-turn:** attacks ARE abilities (`PlayerUseAbilityParams`); there is
  no `UnitAttack` command and **no delay-turn concept** in RT (the WOTR facet does not port). There
  is **no `PathVisualizer`** — RT uses `FindPathTB_Blocking` + `RuleCalculateMovementCost`.

---

## 3. Milestones & components

Effort key: S ≤ ~1 session, M ~1–2, L ~3–4, XL 5+. Each component is independently testable via
the dev harness (`/speech` for the exact spoken text, `/eval` to read/verify live state, `/input`
to drive keys, `/loadsave` to reach a fight). Live checks are consolidated in §6.

### Milestone A — Legibility (follow a fight)

Low-risk, mostly independent of the act-loop; delivers the biggest value-per-effort immediately.

#### A1 · Turn-lifecycle cues  — ✅ **SHIPPED + verified 2026-07-01**
**Folded into `CombatEvents`** (shares its `_pending`/`Tick` queue so "Round 2 · your turn · Ork
takes 5" stay ordered). Already registered/ticked/unsubscribed in `Main.cs`.

**Implemented by POLLING, not the EventBus handlers originally sketched below.** The turn-start/round
handlers are entity-targeted and unreliable for a *global* subscriber (mirrors WOTR, whose turn-ended
handler is never raised), so `CombatEvents.PollLifecycle()` reads authoritative `TurnController`/
`Player` state each frame and enqueues on a transition — the proven WOTR `CombatMode.TickTurn`
pattern. Shipped cues:

| Cue | Poll source | Note |
|---|---|---|
| "Combat started/ended" | `Player.IsInCombat` change | party-level truth; fires regardless of TB |
| "Round N" | `TurnController.CombatRound` change (>0, TB only) | announced before the turn cue |
| "Your turn, X, 2 AP, 6 MP" / "X's turn" / "X's turn, enemy" | `CurrentUnit` change | branch on `IsPlayerTurn` (player → economy via `GetCombatStateOptional`) then `IsPlayerEnemy` |
| "Deployment phase" | `IsPreparationTurn` rising edge | `CurrentUnit` is null in prep |

**Turn dedup keys on `MechanicEntity.UniqueId` (string), NOT `ReferenceEquals`** — `CurrentUnit`
returns a fresh entity instance for the same logical unit across frames, which would re-announce
mid-turn. Null ids are ignored (not tracked) so a between-turns null can't re-fire the same turn.

Deferred / findings (verified live across a full multi-round prologue fight):
- **Same-name disambiguation** — two distinct NPCs shared the display name "Офицер корабля" (different
  UniqueIds); both correctly got a cue. Numbering ("Ship Officer 1/2") is a **C5/targeting** concern, not A1.
- **MP=0 on the first combat turn**, 6 thereafter — real game state (matches HUD), not a bug.
- **Round redundancy** — the game emits its own round banner via the *bark* channel ("Round N", read by
  `BarkEvents`), a mild double with our cue. Reconcile in **A2** (log-authoritative). Kept our cue: it
  guarantees round-1 + combat-start context in a consistent voice.
- Not yet done (optional, low value): "turn skipped" (`IUnitMissedTurnHandler`), surprise/reinforcement
  tags, and the `InGameScreen` initiative enrichment (RoundIndex divider / per-entry AP+surprised).

> **Decision D2 (tune live):** enemy-turn verbosity — v1 announces EVERY unit's turn (your/ally/enemy).
> If chatty in large fights, gate to first-of-phase or add a setting. All cues queue (interrupt:false).

#### A2 · Combat-log narrator  — ✅ **SHIPPED + verified 2026-07-01**
**Implemented** as `Accessibility/CombatLogReader.cs`: one Harmony **postfix on
`LogThreadBase.AddMessage(CombatLogMessage newMessage)`** (the universal chokepoint; auto-applied via
`PatchAll`). Filters to combat channels by **namespace** — `__instance.GetType().Namespace` ending in
`.LogThreads.Combat` (the AnyCombat + InGameCombat threads; barks are `.Dialog`, warnings `.Common`) —
rather than an explicit allow-set, then skips `IsSeparator`/empty, strips rich text with the new
**`TextUtil.StripRichTextSpaced`** (tags→space, so a damage line and its "Critical hit!" suffix don't
weld), and enqueues into **`CombatEvents`' shared per-frame queue** via `EnqueueLogLine` (so log lines
interleave with lifecycle/buff cues in arrival order — no separate queue/dedupe window needed).

**Verified live** (prologue TB fight, dev harness): narrates ability-use headers ("X uses: Laspistol /
Pistol Shot"), ability casts, **damage with type** ("deals damage (5, Energy) to Y"), **misses**,
**crits** ("… Y Critical hit!"), and deaths ("Y dies!") — resolution detail that was entirely absent
before. Round separators correctly skipped. A DEBUG-only diag ring (`CombatLogReader.Diag` /
`DumpDiag()`) records the raw thread-tagged stream for ongoing calibration over `/eval`.

Excluded as noise/owned-elsewhere (`ExcludedThreads`, by thread type name): `RulePerformMomentumChangeLogThread`
(momentum — fires every kill/turn-start; the VALUE belongs on the HUD), `RulebookCanApplyBuffLogThread` +
`MergeRuleCalculateCanApplyBuffLogThread` (buffs owned by `CombatEvents`, below).

_Superseded design notes (kept for reference):_

**Category filter** (by `__instance` type via `LogThreadService`): v1 allow-set = attack
resolution + effects — `PerformAttackLogThread`, `PerformScatterAttackLogThread`,
`GrenadeDealDamageLogThread`, `RulebookDealDamageLogThread`, `HealingLogThread`,
`RulebookSavingThrowLogThread` (+ `Merge…`), `RollSkillCheckLogThread`, `RulebookCastSpellLogThread`,
`PartyUseAbilityLogThread`, `AbilityImmunityLogThread`, `RulePerformMomentumChangeLogThread`,
`ContextActionKillLogThread`, `UnitLifeStateChangedLogThread`, `InterruptCurrentTurnLogThread`,
`UnitMissedTurnLogThread`, `PsychicPhenomenaAvoidedLogThread`. Deny dialog/loot/colony/warning
threads. Surface as a settings category.

**Reconcile with `CombatEvents` (avoid double-speak) — Decision D3: RESOLVED log-authoritative (user-confirmed).**
Retired `CombatEvents`' damage/heal/death EventBus handlers (interfaces + methods removed) — the log carries
richer lines (damage type, misses, crits). **Kept** `CombatEvents` as the per-frame **buff** reconciler and
**excluded the buff-apply log threads**, because live testing confirmed the log has NO buff-removal thread —
only the reconciler can announce expiry ("X lost Frenzy" verified), and keeping both directions there gives
one consistent gain/loss voice. No setting added yet (v1); the buff/log split is clean enough to not need one.

**Scrollback:** **New** `Buffers/LogBuffer.cs : Buffer` (`FollowLatest=true`, cap ~100), appended by
the tap, registered in `BufferManager.RegisterDefaults`. Alt+L/R selects "Combat log", Alt+U/D
scrubs. Store the `CombatLogMessage` (not just text) so a verbose "read the roll math" key can feed
`message.Tooltip` (`TooltipTemplateCombatLogMessage`) to the existing `TooltipReader` (phase 2).

### Milestone B — The act-loop (take a turn)

Depends on **CommandDispatch (§2.1)**. B4 (prediction) and B3 (targeting) are tightly coupled —
build the predictor first, then consume it in targeting.

#### B0 · CommandDispatch service  — ✅ **SHIPPED + verified 2026-07-01** — see §2.1.
`Combat/CommandDispatch.cs` (public static funnel): `ActingUnit` guard (TB + IsPlayerTurn +
selected==current + IsDirectlyControllable), `UseAbilityOnUnit`/`UseAbilityOnPoint`/`UseSelfAbility`
(SetAbility → OnClick — the game's own pointer path), `MoveTo` (relocated the proven
`TryCreateMoveCommandTB` + `Commands.Run` path out of `TileExplorer`, which now calls it), `EndTurn`,
`AbilityArmed`. **Execution proven end-to-end** (closes the long-standing open question, §6/D1): a
dispatched pistol shot returned `ok=True`, dropped the target's HP 28→24, and was narrated by the A2
combat-log reader ("Багардор uses: Stub-revolver / Pistol Shot → deals damage (4, Piercing) to …");
a second run through `CommandDispatch.DevTestAttackNearestEnemy()` (DEBUG self-test) fired again and
narrated a miss. A2 is the natural execution verifier. Point/AoE (`UseAbilityOnPoint` via
`OnClick(null, point)`) compiles but is unverified — validated when AoE targeting lands (B3).

#### B4 · Hit prediction / decision support  — ✅ **SHIPPED + parity-verified 2026-07-01**
**New** `Accessibility/HitPredictor.cs` — `Describe(caster, ability, target, verbose)` → one spoken
line, following **`LineOfSightVM.UpdateHitChance`** verbatim. Pipeline:
`VirtualPositionController.GetDesiredPosition(caster)` → `AoEPatternHelper.GetGridNode` →
`CanTargetFromNode` (out distance/los/reason; on `TargetTooFar` → "N cells too far", `TargetTooClose`
→ min-range, `HasNoLosToTarget` → "No line of sight") → `GetBestShootingPositionForDesiredPosition`
→ `AbilityTargetUIDataCache.Instance.GetOrCreate(ability, target, shootPos)` (the *same* cache the
reticle reads) → `HitWithAvoidanceChance` + `RuleCalculateHitChances.ResultRighteousFuryChance`
(crit). Terse = "H% to hit[, C% crit][, half/full cover]"; verbose adds base hit, each avoidance
that applies (dodge/parry/cover/block/evasion), damage range, and per-shot burst chances.

**Parity proof (live /eval, prologue TB fight):** mod line `80% to hit, 25% crit. Base 80%. 4 to 8
damage.` matched the raw cache/rule field-for-field (hitAvoid=80, initial=80, dmg=4-8,
dodge/parry/cover/block=0 → correctly omitted, crit=25, burst=none). It's a **pure DRY read** — no
command issued, no state mutated — and by construction reveals only what a sighted player sees on the
reticle (per user parity directive). Burst path is a trivial `List<float>` loop mirroring
`OvertipHitChanceBlockVM.cs:156-160`; not exercised live (this character has only single-shot
weapons) — observe opportunistically in B3. Crit isn't in `AbilityTargetUIData`, so it's a separate
`RuleCalculateHitChances` trigger. DEBUG self-test `HitPredictor.DevPredictNearestEnemy(verbose)`.
Consumed next by B3 (per-target line, preview key), the passive tile cursor (gated on an attack being
selected), and C5 (scanner) — B4 is a standalone service; those are its consumers.

#### B3 · Targeting mode  — ✅ **v1 SHIPPED + verified end-to-end 2026-07-01** (unit-target loop)
**Decision D4 resolved → the UNIFIED-cursor model.** No new state machine, no new `InputCategory`:
B3 v1 is built as an enrichment of the *existing* aim→cycle→fire bridge (`Exploration/Targeting.cs`
+ `Exploration/AbilityTargeting.cs`), because the mod's two-cursor model already makes the scanner's
review axis "pick a thing without moving the party" — which is exactly target selection. What v1 adds:

1. **Announce-on-arm** (`Targeting.Tick`, on the null→armed transition): "Aiming *name*, range *N*
   cells. Cycle enemies with period, fire with I, O for the breakdown, Backspace to cancel." Controls
   tailored by target kind (`CanTargetEnemies`→enemies/period; `CanTargetFriends`→allies/comma; else
   cursor+Enter). Blurs the HUD so the exploration keys go live immediately.
2. **Per-target hit prediction** (`Targeting.PredictLine` → appended by `Scanner.Select`): cycling
   enemies (`.`/`,`) speaks the scanner readout **plus** the terse B4 line ("80% to hit, 25% crit");
   dead/out-of-range/no-LOS targets speak B4's reason ("Can't target that."). Gated to attack
   abilities (`CanTargetEnemies`, non-self target) — no misleading to-hit on a heal.
3. **Verbose breakdown on `O`** (`Scanner.ReSpeakSelection`): the full line — base hit, each nonzero
   avoidance, damage range, per-shot burst — so O is "tell me more about this shot".
4. **Fire** with `I` (commit on the scanner selection) or Enter (commit at the cursor) — the existing
   `AbilityTargeting.CommitAt` path; multi-target re-arms with "Target added." The game's log narrates
   the resolution (A2). **Cancel** with Backspace ("Targeting cancelled.").

**Verified live (prologue TB fight, /eval + /input):** arm pistol → announce fired verbatim; cycle 3
enemies → live one read "…28 of 30 HP… 80% to hit, 25% crit", dead ones "Can't target that."; O →
"…80% to hit, 25% crit. Base 80%. 4 to 8 damage."; fire I → "Firing on …" then A2 "uses Pistol Shot"
→ "Deals damage (8, Piercing) Critical hit!" (the 80%/25%/4-8 prediction borne out: hit, crit, 8);
re-arm re-announced (proving aim cleared post-cast); Backspace → "Targeting cancelled."

**Deferred to B3 v2 (own slice):** the **Point/AoE cell cursor** — arrows aim a template point,
`GetPattern`/`GatherAffectedTargetsData` reads offset, in/out of range, pattern size, caught units
(flag allies), primary vs splash. Current Enter-at-cursor already *fires* AoE at a point but gives no
pattern readout. Also v2: explicit "target k of n" multi-target announce (v1 says only "Target
added."). These fold naturally into the C5 scanner work.

#### B2 · Action-bar read recipe + self-cast completeness  — ✅ **SHIPPED + verified 2026-07-01**
**Extended** `ProxyActionBarSlot.State()` (all off `_slot.AbilityData`, the same info the sighted slot
icon/tooltip shows; read on focus so the rule-triggering getters are fine): **range**
(`RangeCells`, `MinRangeCells` if >0 → "minimum range M"; `IsMelee` → "melee"; omitted for Owner
anchor), **target kind** (`TargetAnchor` → "self" / "targets a unit" / `IsAOE`?"area effect":"targets
a point"), **limited uses** (`GetAvailableForCastCount()`, −1==at-will so omit; skipped for ammo
weapons that already read their ammo), and **why-disabled** on greyed slots
(`GetUnavailableReason(GetDesiredPosition(caster))` — the game's own localized reason string, stripped
of rich text). Activation is UNCHANGED: `OnMainClick` is the game's canonical click and already
branches correctly (Owner/toggle → immediate self-cast; Unit/Point → `SetAbility` arms → B3 takes
over), so no dispatch rewire was needed.

**Verified live** (rendered live slots through `AnnouncementComposer`): pistol → "1 action point,
range 12 cells, targets a unit, 6 uses left"; reload → "self, 6 of 6 ammo, unavailable, *Cannot
reload a full weapon*, disabled"; charge → "2 action points, range 6 cells, minimum range 2, targets
a point, unavailable, *Requires a melee weapon*, disabled". Reason strings surface in the game's
locale (= sighted parity); the mod scaffolding stays English.

**Deferred (low value / optional):** heroic-act/desperate-measure category tags, `HasConvert` variant
sub-actions (currently the silent popup), and a post-cast remaining-AP cue (`IUnitSpentActionPoints`)
— none blocking; the core decision info (range/target/uses/why-disabled) is in.

### Milestone C — Tactical mastery (fight well)

#### C5 · Battlefield scanner (tactical radar)  — *L, deps B4 helpers*
**New** `Accessibility/CombatScanner.cs` (combat `InputCategory`, modeled on the exploration
`Scanner`). Enumerate `Game.Instance.State.AllBaseAwakeUnits` (**not** the `internal`
`UnitsInCombat`) filtered `IsInCombat && !IsDead && IsVisibleForPlayer`; segment by faction
(`IsPlayerEnemy`/`IsInPlayerParty`/`IsNeutral`); anchor = `CurrentUnit` else `SelectedUnit`; sort
`DistanceToInCells`. Per-target line: name + faction + cells + bearing
(`InteractableDescriber.DirectionAndDistance`/`RelativeTile`) + HP (respect
`HideRealHealthInUI`) + cover-vs-me (**replicate `OvertipCoverBlockVM.UpdateCover`**:
`GetBestShootingPosition` → `GetWarhammerLos` — do NOT read the VM, it's stale for off-screen/ally
units) + LOS + threat (`IsThreat`, `GetEngagedByUnits().Count`) + in-range (B4 helper). Cycle
`]`/`[` (Shift = allies); drive the shared `MapCursor`; whole-battlefield **summary key** ("4
enemies, 2 allies. Nearest Ork Boy, 3 east, half cover, in range. Threatened by 1."). Add a
"Scanned combatant" `UnitBuffer` to `BufferManager.RegisterDefaults` (one line).

#### C6 · Movement preview  — *M, deps B0*
**New** `Accessibility/CombatMovement.cs`; commit already works, only the **preview** is missing.
Cache `FindAllReachableTiles_Blocking(agent, pos, ActionPointsBlue)` once per turn (invalidate on
turn/move). Per cursor cell: reachable? = dict contains node → `cell.Length` AP,
`WarhammerPathHelper.ConstructPathTo` (no A*); else full `FindPathTB_Blocking` +
`RuleCalculateMovementCost` for the shortfall. `PathExtras.LengthInCells` for a true tile count
(replaces Chebyshev `TilesAway`). Threats crossed via `CacheThreateningAreaCells` (name via
per-enemy `CollectThreateningArea`); overwatch via enemies' `PartOverwatch.OverwatchArea`. Speak on
the **arm press** in `TileExplorer.MoveToCursor` (e.g. "4 tiles, 8 MP, reachable, crosses 1
threatened tile — provokes attack of opportunity. Press again to move."). Optional reachable-set
overview key. **Do not** use `UnitMoveTo`/`PathVisualizer` (WOTR-only); the TB path is
`UnitMoveToProper` via `TryCreateMoveCommandTB`.

---

## 4. Input & settings surface

- **New `InputCategory`s:** `Targeting` (transient, top-of-stack while B3 active),
  `CombatScanner`/combat keys (live only when `TurnBasedModeActive`). Fit the `InputManager`
  priority/shadowing model like `Windows`/`WorldMap`. Pick free keys per `[rt-keyboard-usage]`
  (free: K O U Z, F6/F7/F9/F12, PageUp/Dn, Home/End, Ins/Del, brackets). Proposed: `[`/`]`
  cycle scan/target, `P` preview/path-info, Enter confirm, Esc/Backspace cancel, a summary key,
  a full-breakdown key.
- **Settings** (typed tree exists): log-narrator categories + verbosity, enemy-turn chattiness,
  passive hit-preview on cursor on/off, reveal-fogged-enemies, D3 reconciliation mode, "Your turn"
  interrupt vs queue.

---

## 5. Suggested build order

```
B0 CommandDispatch ─┬─────────────────────────────┐
                    │                              │
A1 Lifecycle cues   B4 HitPredictor ─► B3 Targeting│─► B2 Action-bar
A2 Log narrator     │                              │
(A independent) ────┘        C5 Scanner ◄──────────┘   C6 Movement preview
```

Recommended sequence: **A1 → A2** (immediate legibility, low risk) → **B0** (foundation) → **B4 →
B3 → B2** (the act-loop) → **C5 → C6** (mastery). A-milestone can ship before B is finished — after
A, a blind player can already *follow* a fight; after B, *take a turn*; after C, *fight well*.

Rough total: ~2 S/M quick wins + 1 foundation + 1 crux (L) + ~4 M/L features ≈ a handful of focused
sessions, each shippable and harness-verifiable.

---

## 6. Open questions — resolve live via the dev harness

Grouped; all checkable with `/eval` (read state), `/speech` (captured text), `/loadsave` (reach a
fight). Blockers for the phase that consumes them.

**Dispatch / targeting (D1, B3):**
- Does `ClickWithSelectedAbilityHandler.OnClick(view.gameObject, …)` fire for **every** archetype in
  mouse mode — single melee, ranged burst, blast/point, cone, scatter, multi-target, charge?
- For point/AoE without a GameObject, is `TryUnitUseAbility(Vector3 wrapper)` enough, or does the
  game rely on `SelectedAbilityHandler` pattern/rotation set during `OnClick`? Confirm AoE
  orientation matches the sighted red highlight (may need `AoEPatternHelper.GetActualCastPosition`).
- Is `IAbilityTargetSelectionUIHandler` raised for **all** entries into targeting (thrown grenades,
  variant abilities, spontaneous conversions)?
- Does `PlayerInputInCombatController` auto-unlock after a queued command so the next mod command runs?

**Prediction / cost parity (B4, C6):**
- Does `RuleCalculateHitChances.ResultRighteousFuryChance` equal the tooltip's "Righteous Fury" %?
- Does `AbilityTargetUIData.HitWithAvoidanceChance` (at `bestShootingPos`) equal the on-screen
  overtip for the same attacker/weapon/target? (validates the position/cache recipe)
- Is repeated `HitPredictor`/`RuleCalculateMovementCost` triggering truly side-effect-free
  (no cooldown/ammo/RNG perturbation)?
- Confirm the sighted HUD shows movement as **MP points** vs **cells**, and
  `WarhammerMovementApPerCell` for a baseline character, so the readout unit matches.

**Lifecycle / log (A1, A2):**
- `CombatRound` value at the exact `HandleRoundStart` fire (should read the new round, e.g. "Round 1").
- Capture the real one-line phrasing for miss/dodge/parry/block/crit/negated and confirm
  placeholders are fully resolved at `AddMessage` time.
- D3 reconciliation policy (retire vs damage-filter); default log categories (is hit/miss/damage the
  minimal v1, or include momentum/saves/skill-checks?).

**Scanner (C5):**
- Include fogged/invisible enemies? (fairness vs completeness — maintainer call)
- `GetWarhammerLos` results match the on-screen cover pips? Squad/large-unit distance reads naturally?

---

## 7. Risks

- **casterPosition drift** — the pervasive correctness trap (§2.3). Every validate/pattern/preview/
  commit must use the desired/best-shooting position, or the mod lies about odds and mis-commits.
- **Per-keypress rule cost** — reachable-set + hit-preview trigger nested rules; cache per turn
  (`FindAllReachableTiles_Blocking`, `AbilityTargetUIDataCache`) and compute lazily for the
  highlighted target only — never in a silent Update loop.
- **Log double-speak** — unavoidable overlap between the narrator and `CombatEvents`; D3 must be
  settled or hits are voiced twice.
- **Verbosity flooding** — a burst turn produces many lines; the non-interrupt queue + category
  allow-set + settings toggles keep it usable; enemy-turn cues especially need tuning.
- **Mouse-mode gate** — confirmed to NOT touch `Commands.Run`/`UnitCommandsRunner`/pointer-controller
  click handlers; the whole dispatch strategy rides on that (verify D1 live).
- **Multi-target/AoE shared state** — `MultiTargetHandler` mutates engine state; always `DropAbility`
  on cancel or a stale partial target set leaks into the next cast.
- **Squads/placeholders/lightweight units** — `CurrentUnit` may be a `UnitSquad`/placeholder; spatial
  code must filter to `BaseUnitEntity` and null-guard `Health`/weapon slots.

---

## AI Usage Disclosure

The verified-API audit underpinning this plan (`phased-skirmishing-liskov-apis.md`) and this plan
itself were produced with assistance from Claude (Anthropic); all API claims are cited to the
decompiled source and are to be re-verified live during implementation.
