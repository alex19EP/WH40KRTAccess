# Tactical positioning — deployment phase + holographic vantage read

Plan name: `docs/plans/positional-deploying-dijkstra.md`. Extends the combat plan
`phased-skirmishing-liskov.md` (+ `-apis.md`) with the two positional-tactics gaps it never
covered. Grounded in a 3-agent audit of the decompiled RT source (`decompiled/`, git-ignored but
present) and the current mod; every API below is cited to a decompiled file+line and should be
re-verified live before it is relied on.

---

## 0. Executive summary

Two related capabilities a blind player is missing, both about **where a unit stands**:

1. **The pre-combat deployment (preparation) phase is announce-only.** Every non-space-combat
   encounter opens with a `Preparation` turn — the game grants each party member a 7-cell
   reposition budget and a "Start battle" button. The mod says "Deployment phase"
   (`CombatEvents.PollLifecycle`, `CombatEvents.cs:131`) and then **dead-ends**: there is no
   accessible way to pick a unit, choose a cell, place it, or start the battle. `Targeting.cs:15`
   already earmarks "deployment placement" as a planned aim-then-commit extension.

2. **No holographic / positional preview.** The sighted player hovers a move/deploy cell and sees
   how cover, line-of-sight and threat change from *there* (the ghost + the cover overtip that
   tracks the planned position). The mod has no "if I stood **here**, my cover/LoS/threat becomes
   X" read — neither during deployment nor mid-combat. C6's `PathInfo.Preview` stops at cost + AoO.

Both route through **verified, side-effect-free** funnels and reuse the mod's existing shapes:
- **Deployment** = the game's own placement (`ClickSurfaceDeploymentHandler.CanDeployUnit` gate +
  `UnitTeleportParams` teleport command) driven behind a `CommandDispatch.Deploy` funnel, wrapped
  in a `DeploymentMode` that clones `Targeting.cs`'s `Active`/`CommitAtCursor`/`Cancel` shape over
  the shared `MapCursor`/`TileExplorer` grid cursor.
- **Vantage read** = a position-parameterised overload of `CombatReads.CoverRangeThreat` — every
  underlying primitive (`GetBestShootingPosition`, `GetWarhammerLos`, `CanTargetFromNode`,
  `IsThreat`) already takes an explicit cell, so a "from cell C" read is a **pure read** needing
  **no `VirtualPositionController` mutation at all**.

These reinforce each other: accessible deployment is only useful *with* the vantage read (you place
a unit by evaluating cover/threat from candidate cells), so they ship as one slice.

---

## 0b. The one crux the research resolved

The `phased-skirmishing-liskov-apis.md` open question (line 473) — *does `GetDesiredPosition` track
a planned move so cover-vs-me updates?* — is answered:

- **Yes, but via the mouse.** `VirtualPositionController.VirtualPosition` is written **only** by
  `UnitPredictionManager.UpdateVirtualLoSPosition` (`decompiled/Kingmaker.UnitLogic/UnitPredictionManager.cs:323`),
  fed by `SetVirtualHoverPosition` off the mouse-pointer path in `SurfaceMainInputLayer` — which is
  **engine-dead in the mod's forced mouse mode** (`[rt-mouse-mode-engine-gate]`). So for the mod,
  `GetDesiredPosition(unit)` stays `== unit.Position` unless the mod sets it.
- **We don't need to set it.** `OvertipCoverBlockVM.UpdateCover` (the cover-vs-me overtip,
  `…UnitOvertipParts/OvertipCoverBlockVM.cs:89`) is just
  `GetWarhammerLos(GetBestShootingPosition(GetDesiredPosition(me), me.SizeRect, E.Position, E.SizeRect), me.SizeRect, E).CoverType`
  — and **every primitive takes an explicit `Vector3`/node**. Substitute candidate cell C for
  `GetDesiredPosition(me)` → the exact overtip number, from any cell, with zero controller state.
- **Do NOT set `VirtualPosition = C`.** It is mechanically safe (only 3 UI subscribers —
  `OvertipCoverBlockVM`, `LineOfSightVM`, `AbilityTargetUIDataCache` — no game-state mutation), but
  it visibly flickers on-screen cover pips + the LoS line, churns the hit cache, and only "takes"
  for the current player unit with no active command. All cost, no benefit for a speech read. Keep
  set-and-restore only as a fallback if live testing finds a hologram-only path the static
  primitives can't reproduce.

---

## 1. Verified game APIs

### Deployment / preparation phase — `decompiled/Kingmaker.Controllers.TurnBased/TurnController.cs`
- **`IsPreparationTurn`** (l.189) `=> TurnOrder.CurrentTurnType == CombatTurnType.Preparation`. The
  liveness flag. `NeedDeploymentPhase` (l.191) `=> CurrentMode != SpaceCombat` (all non-space combats
  get a prep phase). `IsDeploymentAllowed` (l.193) `=> UnitsInCombat.Any(u => u.IsDirectlyControllable && !Surprised)`
  — the `canDeploy` bool; false on an all-surprised ambush (prep opens, but only "Start battle").
- **`BeginPreparationTurn(bool canDeploy)`** (l.1116) grants each `PartyAndPets` unit blue points =
  `BlueprintCombatRoot.DistanceInPreparationTurn` (**=7**, `…SystemMechanics/BlueprintCombatRoot.cs:42`),
  sets `StartedCombatNearEnemy`, raises `IPreparationTurnBeginHandler.HandleBeginPreparationTurn(canDeploy)`
  (l.1141). Ended by `ForceEndPreparationTurn()` (l.1168) → `IPreparationTurnEndHandler.HandleEndPreparationTurn()`
  (l.1177). During prep **`CurrentUnit` is null**, so the whose-turn poll never fires — the begin/end
  handlers (or the existing `IsPreparationTurn` poll) are the only entry cue.
- **`RequestEndPreparationTurn()`** (l.1147) — THE "Start battle" action (the exact `Action` the game
  hands `CombatStartWindowVM`, `SurfaceHUDVM.cs:172`); coop-synchronized, resolves immediately in
  single-player. Gate on **`CanFinishDeploymentPhase()`** (l.344): true if `!IsDeploymentAllowed`,
  else false while any non-hidden party member (not `StartedCombatNearEnemy`) is within 1 cell of a
  player-enemy → speak `UIStrings.TurnBasedTexts.CannotStartbattle` when blocked.

### Placement commit — `decompiled/Kingmaker.Controllers.Clicks.Handlers/ClickSurfaceDeploymentHandler.cs`
- **`CanDeployUnit(GraphNode node, BaseUnitEntity unit, bool ignorePetRestriction=false)`** (l.172;
  1-arg overload l.167 uses `SelectionCharacter.SelectedUnit`) — the game's **full, public-static**
  legal-cell validation: node is a `CustomGridNode`, the unit's whole `SizeRect` footprint lies inside
  `UnitMovableAreaController.CurrentUnitMovableArea` **minus** `DeploymentForbiddenArea` (the ring ≤1
  cell from enemies), `unit.CanMove`, `node != current cell`, `WarhammerBlockManager.CanUnitStandOnNode`,
  pet ≤2-cell rule. **Reuse directly** — do not reimplement zone math.
- **`TryDeployCurrentUnit(Vector3)`** (l.137) is the private commit: `unit.Commands.Run(new
  UnitTeleportParams(node.Vector3Position, isSynchronized:true))` on `SelectionCharacter.SelectedUnit.Value`
  (l.153-154), then re-teleports the pet if >2 cells away. **A synchronized teleport, not a walk.**
  ⚠️ Do **not** drive `ClickSurfaceDeploymentHandler.OnClick` — it dereferences its `gameObject` arg
  with no null-check (l.71) and prefers `UnitPathManager.Instance.CurrentNode` over the passed point
  (l.144), so a synthetic keyboard click NPEs or mis-places. Mirror the two game calls (`CanDeployUnit`
  gate + `UnitTeleportParams`/`Commands.Run`) behind our funnel instead — the `CommandDispatch.MoveTo`
  precedent.
- **Unit selection:** `SelectionCharacterController.SetSelected(unit)` / `SelectedUnit`
  (`decompiled/Kingmaker.Controllers/SelectionCharacterController.cs:26`); `ActualGroup` (l.64) is the
  deployable roster. `SetSelected` drives `UnitMovableAreaController.UpdateMovableArea` to recompute
  that unit's deploy zone. `CombatStartWindowVM` auto-selects the first party unit at prep start.
- **Deploy zone data:** `UnitMovableAreaController.CurrentUnitMovableArea` / `DeploymentForbiddenArea`
  (`decompiled/Kingmaker.Controllers.Units/UnitMovableAreaController.cs:44/46`) — already read by
  `PathInfo.Preview:47`. Legal set = `CurrentUnitMovableArea.Except(DeploymentForbiddenArea)`.

### Holographic vantage read — pure reads, no controller
- **`LosCalculations`** (`decompiled/Kingmaker.View.Covers/LosCalculations.cs`):
  `GetBestShootingPosition(Vector3 shooterPos, IntRect shooterSize, Vector3 targetPos, IntRect targetSize)`
  (l.178), `GetWarhammerLos(Vector3 origin, IntRect fromSize, MechanicEntity target) → LosDescription`
  (l.476; implicit `CoverType {None,Half,Full,Invisible}`, Invisible = no LoS), and the directionless
  `GetCoverType(Vector3 position)` (l.398 — what the ghost itself uses for a "this cell is behind
  cover" cue). All static, explicit-position, no side effects (grid linecasts — call per-keypress,
  never per-frame).
- **Threat-at-C:** `AttackOfOpportunityHelper.IsThreat(this BaseUnitEntity attacker, GraphNode
  targetNode, IntRect sizeRect=default)` (`decompiled/Kingmaker.UnitLogic/AttackOfOpportunityHelper.cs:130`)
  — the dry read for "does enemy E threaten cell C where my size-S unit would stand". (The existing
  `CombatReads` threat uses `IsThreat(me)` keyed to me's **current** cell — see risk below.)
- **In-range-from-C:** `AbilityData.CanTargetFromNode(casterNode, …, out distance, out los, out reason)`
  (`decompiled/…/AbilityData.cs:1622`). ⚠️ For abilities where `TryGetCasterForDistanceCalculation`
  returns true (l.1628/1633) the passed `casterNode` is **ignored** and `caster.CurrentUnwalkableNode`
  is used — so "in range from C" may measure from the actual tile for the party's default weapon;
  verify per weapon, fall back to a direct `DistanceToInCells(C, target)` if so. (Its `out los` is
  hardcoded `None` at l.1637 — cover always comes from `GetWarhammerLos`, as the mod already knows.)

---

## 1b. Implementation status (2026-07-02, adversarially reviewed)

**SHIPPED + LIVE-VERIFIED 2026-07-03** (vantage half checked in an active fight — `VantageFrom` output = the game's
own `LosCalculations`/`CanTargetFromNode`/`IsThreat` ground truth exactly, incl. a non-degenerate cell next to an
enemy: cover None + in-range 1 + threatened 1; V-key end-to-end confirmed. Deployment half checked from a
battle-start save in `IsPreparationTurn=true`: "Deployment phase" + "Reposition up to N cells…" announce; `CursorTail`
"can deploy here"+vantage; Enter placed the unit at the exact target cell; a blocked cell refused with no move; **B**
ended prep with "Battle begins." correctly ordered *before* "Your turn". Pet co-teleport not exercised — the test unit
has no pet.)**  All of: T0 (`CommandDispatch.Deploy` + `CombatReads.CoverTo(from,…)`
overload + `CombatReads.VantageFrom`), T1 (`Exploration/DeploymentMode.cs` + `TileExplorer` Enter/Backspace/tail
wiring + `Main` tick + `InputBindings` B key), T2-A (deployment cursor-step vantage tail) and T2-C (bare-**V** vantage
key). Deviations from the sketch below: `DeploymentMode` lives in **`RTAccess.Exploration`** (beside `Targeting`, its
template), not `Combat/`; the entry announce is a **follow-up to `CombatEvents`' existing "Deployment phase" cue**,
spoken **queued** (interrupt:false) per the speech rule.

**Post-review fixes applied (10-agent adversarial review + verify):** (1) `Deploy` now **faithfully mirrors**
`TryDeployCurrentUnit` — added the **pet co-teleport** (owner's pet follows to the nearest deployable cell when left
>2 cells away, via `GridAreaHelper.GetNodesSpiralAround` + `CanDeployUnit(…,ignorePetRestriction:true)`, in a
best-effort `RepositionPet` helper with the empty-spiral guard the game omits) and the **`IsMyNetRole()`** co-op gate.
(2) The **"Battle begins"** exit cue was moved out of `DeploymentMode.Tick` into `CombatEvents.PollLifecycle`'s ordered
`_pending` queue (falling edge, before the round/whose-turn cues) so it can't trail "Your turn, X" on a battle-start
hitch frame. (3) `StartBattle` wrapped in try/catch (Main.OnUpdate has no top-level catch). Review **refuted** the
scarier claims: a stationary pet can never block `CanFinishDeploymentPhase` (`StartedCombatNearEnemy` is latched at
prep start); no NRE in `StartBattle` (the game calls the same `CanFinishDeploymentPhase` unguarded every frame); and
no save-load "Battle begins" misfire (the poll self-heals every frame on a fresh post-load `Game`).

**DEFERRED (not built):** T2-B (auto-append vantage to the C6 `PathInfo.Preview` combat move preview — held back to
avoid move-preview verbosity; the **V** key serves the "planning a combat move" case on demand) and **T3**
(`PathInfo.Preview` localization fix — an independent pre-existing hard-rule cleanup, out of scope for this slice).
New locale keys: `deploy.*`, `vantage.*`, `cover.none`, `vantage.hidden`.

## 2. Components

Effort key: S ≤ ~1 session, M ~1–2, L ~3–4.

### T0 · Shared primitives (build FIRST) — S
- **`CommandDispatch.Deploy(CustomGridNodeBase node)`** (new, `Combat/CommandDispatch.cs`) mirroring
  `MoveTo`: resolve the selected directly-controllable unit, gate on `ClickSurfaceDeploymentHandler
  .CanDeployUnit(node, unit)` (speak a localized refusal on fail — the game also raises its own
  `PathBlocked` warning), commit `unit.Commands.Run(new UnitTeleportParams(node.Vector3Position,
  isSynchronized:true))`, return bool. Pet reposition (`TryDeployCurrentUnit:156-163`) optional/deferred.
- **`CombatReads.CoverRangeThreatFrom(Vector3 from, BaseUnitEntity me, BaseUnitEntity target)`** (new
  overload) — identical body to `CoverRangeThreat` (`CombatReads.cs:68`) with `from` substituted for
  `GetDesiredPosition(me)` in `CoverTo`/`ShootNode`, and `target.IsThreat(me)` replaced by
  `target.IsThreat(GetGridNode(from), me.SizeRect)`. Refactor `CoverTo`/`ShootNode`/`CoverRangeThreat`
  to take an optional `from` (default `GetDesiredPosition`) so the scanner path and the new cursor
  path share one implementation.

### T1 · Deployment mode (accessible placement) — M/L
New `Combat/DeploymentMode.cs` (static, clones the `Targeting.cs` shape):
- `Active => TurnController.IsPreparationTurn`; `CanPlace => Active && IsDeploymentAllowed`.
- `Tick()` (pumped from `Main.OnUpdate` next to `Targeting.Tick`): on the `Active` rising edge, blur
  the HUD and announce a deployment `ArmAnnounce` — "Deployment phase. Reposition up to 7 cells; step
  the cursor and press Enter to place the selected character; press *StartBattle* to begin." (or the
  surprise/ambush variant when `!IsDeploymentAllowed`).
- `CommitAtCursor()`: guard `CanPlace` + `MapCursor.Has`; `CommandDispatch.Deploy(MapCursor.Node)`;
  speak `deploy.placed{name}` / refusal. Placement is free & repeatable during prep → no MP-style
  confirm window (the cursor's lazy-plant-first-read already prevents a blind placement).
- `StartBattle()`: `if (CanFinishDeploymentPhase()) RequestEndPreparationTurn(); else` speak the
  cannot-start reason (`CombatStartWindowVM.CannotStartCombatReason` if live, else
  `UIStrings.TurnBasedTexts.CannotStartbattle`).
- **Contextual keys, no new bindings for place/cancel:** at the top of `TileExplorer.InteractAtCursor`
  add `if (DeploymentMode.Active) { DeploymentMode.CommitAtCursor(); return; }` **before** the
  existing `if (Targeting.Aiming)` guard (Enter = place). Arrow-step, `C` recenter, `Delete`
  re-announce, and the party member-select hotkeys (Alt+1..6 / Shift+A/D) keep choosing WHICH unit
  deploys via `SelectionCharacter.SetSelected`. Register **one** new action `deploy.start_battle` on a
  free key.

### T2 · Vantage read (holographic positional preview) — S/M
Core = T0's `CoverRangeThreatFrom`. Three surfaces:
- **A · Deployment:** `DeploymentMode` appends the tail to each cursor-step announce — "from here,
  half cover from Ork, in range of 2, threatened by 1" (vs the nearest visible enemy / a small set).
- **B · Combat move preview (C6):** in `PathInfo.Preview`, when `Player.IsInCombat` and the cell is
  reachable, append `CoverRangeThreatFrom(dest.Vector3Position, unit, nearestEnemy)` so the arming
  move press already reports the destination's tactical value.
- **C · Dedicated "vantage" key (optional):** `read.vantage` on **bare `V`** (freed by `GameKeybinds`,
  see `GameKeybinds.cs:53`) → speak `CoverRangeThreatFrom(MapCursor.Position, actingUnit, nearest)` on
  demand, outside a move/deploy commit. Optionally lead with the directionless `GetCoverType(C)`
  ("behind cover here") then per-enemy deltas.

### T3 · `PathInfo.Preview` localization fix (bundle with T2-B) — S
`Exploration/PathInfo.cs` currently returns **hardcoded English** ("Out of movement.", "Blocked.",
"Reachable, N tiles, costs X of Y movement", "Provokes attacks of opportunity from …") + TileExplorer's
" Press again to move." — the only combat code bypassing Localization (**hard-rule violation**). Move
every string to `Loc.T` + `RTAccess/assets/locale/enGB/ui.json` when the file is touched for T2-B.

---

## 3. Input & settings surface

- **No new keys for deploy/place/cancel** — Enter/Backspace/arrows/member-select are reused
  contextually via `DeploymentMode.Active` (the `Targeting.Aiming` precedent).
- **New actions:** `deploy.start_battle` (a free key — `V`/`B`/`Insert`/brackets are open per
  `[rt-keyboard-usage]`; `U` is taken by the battlefield summary), and optionally `read.vantage` on
  bare `V`.
- **New locale keys** (`enGB/ui.json`): `deploy.arm_announce` / `deploy.arm_announce_surprise`,
  `deploy.placed{name}`, `deploy.blocked`, `deploy.start_battle_hint`, `deploy.cannot_start{reason}`,
  `deploy.battle_begins`, `vantage.*`, plus the relocated `path.*` strings from T3. Game-provided text
  (the `CannotStartbattle` reason) is passed through, never re-translated.
- **Settings (optional):** vantage-read verbosity; whether B (move-preview) auto-appends the tail or
  it's V-only.

---

## 4. Build order

```
T0 primitives ─┬─ T1 DeploymentMode ─── (T2-A deployment vantage tail)
               └─ T2 CoverRangeThreatFrom ─┬─ T2-B PathInfo.Preview + T3 localization
                                           └─ T2-C bare-V vantage key
```

Recommended: **T0 → T1** (the headline gap: a blind player can finally deploy) → **T2-B + T3**
(vantage in the move preview, closes the loc bug) → **T2-C** (the standalone vantage key). T2-A folds
into T1's announce once `CoverRangeThreatFrom` exists.

---

## 5. Open questions — resolve live via the dev harness

**Deployment (T1):**
- Does `UnitMovableAreaController.CurrentUnitMovableArea`/`DeploymentForbiddenArea` populate for the
  mod-selected unit immediately, or only after a `SetSelected`/EventBus recompute? Confirm driving
  `SetSelected` forces `UpdateMovableArea` before we read.
- Does `unit.Commands.Run(UnitTeleportParams)` actually re-position during prep and repeat freely?
  (`TryDeployCurrentUnit` checks `IsMyNetRole()` — always true single-player; verify.)
- Does the mod's `Exploration` input category still own world control while the `CombatStartWindow`
  overlay is up (arrows/Enter/member-select fire), or does the deployment window shadow it?
- Surprise start (`IsDeploymentAllowed==false`): confirm prep still enters with only a Start-battle
  action and `CanFinishDeploymentPhase()` short-circuits true.
- Pet reposition: replicate the >2-cell pet re-teleport, or defer for pet builds?

**Vantage (T2):**
- Does `CanTargetFromNode` honor the passed `casterNode` for the party's default weapon, or
  short-circuit to `caster.CurrentUnwalkableNode` (l.1628/1633)? If the latter, use a direct
  `DistanceToInCells(C, target)` for "in range from C".
- Do `GetBestShootingPosition`+`GetWarhammerLos` from an empty candidate cell C match the overtip once
  the unit actually stands there (check a walkable-empty C and a cover-edge C)?
- Speak per-enemy directional cover (`GetWarhammerLos`) or the ghost's directionless `GetCoverType(C)`?
  They disagree (vs-a-shooter vs any-side) — likely `GetCoverType` for a quick cue + per-enemy deltas.
- Confirm the pure-read path is truly side-effect-free (esp. `IsThreat` touching `attacker.Vision.HasLOS`
  — verify no reveal/known-state mutation via a `/eval` before/after diff).

---

## 6. Risks

- **`ClickSurfaceDeploymentHandler.OnClick` is a trap** — NPE on null gameObject + prefers a stale
  hover node. Always go through `CanDeployUnit` + `UnitTeleportParams`/`Commands.Run` (the `MoveTo`
  precedent), never the click handler.
- **Threat-from-C is not a clean origin swap.** Cover/LoS/in-range transfer by substituting the
  origin; `IsThreat` must switch to the `GraphNode` overload (l.130) or the threat clause is wrong.
- **`GetWarhammerLos` cost** — grid linecasts per enemy; compute on the deliberate keypress
  (cursor-step / arm / vantage key), never in a silent Update loop.
- **Input ownership during prep** — the deployment window is a game overlay; if it shadows the
  Exploration category the cursor won't drive. May need a prep-specific category push (like
  `Windows`/`WorldMap`).
- **`VirtualPosition` set-and-restore is tempting but wrong** — visible pip/LoS flicker + cache churn
  for zero speech benefit. Stay on the pure-read path.

---

## AI Usage Disclosure

The verified-API research underpinning this plan (a 3-agent audit of the decompiled RT source) and
this plan itself were produced with assistance from Claude (Anthropic). All API claims are cited to
the decompiled source and are to be re-verified live during implementation.
