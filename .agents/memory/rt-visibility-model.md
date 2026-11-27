---
name: rt-visibility-model
description: "Scanner visibility model settled: the ENGINE decides the latch (units never, statics always); cursor-as-observer LOS via PartVision.HasLOS; the EyeShift/range traps"
metadata: 
  node_type: memory
  type: project
  originSessionId: 6971fd7f-1f30-40d2-8b47-5a4d702ca700
  modified: 2026-08-10T13:52:54.717Z
---

Settled 2026-08-03, from the July Discord feedback (chaosbringer's visibility thread + jean's discovery-model
counter-proposal). Answers "what should the scanner list?" — see [[rt-scanner-consistency]] for the older,
superseded attempt and [[rt-visual-parity]] for the governing design law.

**The latch is the ENGINE's call, not ours — and it is already exactly what a sighted player gets.**
`EntityVisibilityForPlayerController` has two `IsVisible` overloads and picks by type:
- `IsVisible(BaseUnitEntity)` — `if (unit.IsInFogOfWar) return false;` with **no `IsRevealed` escape**. Units can
  never be remembered.
- `IsVisible(MechanicEntity)` — `(!IsRevealed && IsInFogOfWar) → false`, i.e. statics latch once revealed.

`IsRevealed` itself latches in `EntityViewBase.SetVisible(visible: true)`, driven by blocker-aware, height-banded
fog culling. So "statics stay known, creatures don't" needs NO mod-side discovery model — we get it by reading the
right flag per kind (`ProxyMapObject` → `IsRevealed`, `ProxyUnit` → `IsVisibleForPlayer`). Consequence worth
remembering: in `DetectableFrom`, a fogged unit fails `IsVisible` first, so **the LOS clause only ever re-admits
statics**.

**Agreed surface rules** (user decision): PageUp/PgDn browse stays discovery-gated (`IsVisible`); `M` + sonar use
`DetectableFrom` with the **cursor as a hypothetical observer** ("is it visible from where I'm pointing", not "from
the party") — chosen because the cursor is already the audio camera everywhere else; `V` exits stay ungated by LOS
and room-scoped (WA does the same deliberately); creature cycles need no rule, the engine enforces them.

**Use the game's own rule, not a rebuilt one**: `PartVision.HasLOS(point, overridePosition)` is public and does
range + eye-shift + obstacle + scripted `ExtendedVisionArea` in one call. Reach it off a `MechanicEntity` via the
`GetVisionOptional()` extension (`Kingmaker.UnitLogic.Parts`), anchored on `MapCursor.Anchor()`.

**VISIBLE ≠ TARGETABLE — a second, independent axis** (2026-08-10, live-verified on the Void Shadows Beast).
`Features.IsUntargetable` is a retain-counted flag (cutscene `CommandMarkUntargetableSpan` / `CommandIgnoreCombat`,
`ResurrectionLogic`, or an encounter buff — the Beast's tails carry `SC_DW_Beast_Tail_Caution_Buff`, five
`AddMechanicsFeature` + a `PlayLoopAnimationByBuff`, so it flips mid-fight). Grepped EVERY use site: it appears in
the two overtip views, `UnitState.HasHiddenCondition` (:328 — hides name/health/level blocks), `ClickUnitHandler:61`
(click priority → `0`), `AbilityData.CalculateIsValid:1428`, the AI target selector and some ability target filters.
It appears in **NO renderer / `EntityVisibilityForPlayerController` path**. Confirmed live on a flagged tail:
`activeInHierarchy`, **7/7 renderers enabled, 7/7 inside the camera frustum**, some `isVisible` — identical shape to
the targetable controls, i.e. **no retract/burrow**; the Caution animation keeps the model on screen.
Decisive parity fact: **`InitiativeTrackerVM.CheckVisibiltyInTracker` gates on `IsVisibleForPlayer` +
`HasNoStandardTurn`, NOT on `IsUntargetable`** — verified live, both flagged tails held slots 7 and 11 in the
on-screen tracker. A sighted player SEES the unit, sees it in the turn order, and simply cannot lock onto it.
So: keep such units readable + tagged (`unit.untargetable`), drop them from the enemy TARGET cycle only —
**hiding them would be the parity break**, not the fix.
**Refusal-code trap:** `CalculateIsValid` reports IsUntargetable as `UnavailabilityReasonType.NullTarget` — the same
code as "no target at all" — which localizes to the bare generic "Unavailable"/"Недоступно". Read the flag to tell
the two apart (`HitPredictor.Untargetable`'s `when` guard) instead of trusting the reason code.
**`TargetAnchor` matters:** a Point-anchored ability (e.g. `MeleeLinearTwoHandedSword_ability`) targets the CELL, so
it validates fine against an untargetable occupant — never pre-gate a commit on the entity flag alone.

Traps that cost real time — all verified in the decompiled source:
- **Two different `EyeShift` constants.** `LineOfSightGeometry.EyeShift` (1 m) is DEAD; every game caller uses
  `LosCalculations.EyeShift` (**1.5 m**).
- **Obstacle testing alone is not visibility.** Without the range half it can never say no down a long open
  corridor. Player-faction `PartVision.RangeMeters` is a flat **22 m** (public — no reflection needed, unlike WoTR's
  private `FogOfWarController.VisionRadius`).
- **Only `from.y` matters.** `LineOfSightGeometry.LinecastCell` blocks iff a blocker's top is at or above `from.y`;
  the `to` endpoint is projected to 2D and its y is ignored entirely. So a ground-height origin reads the wrong
  side of the only vertical rule, and shifting the *target* is cosmetic.
- **Doors are free.** `FogOfWarBootstrapper` feeds blocker activate/deactivate into `UpdateBlocker`, so open/closed
  state is live in the oracle.
- **LOS cannot model ceilings/floors** (2D + one height test) — that is `Reachability`'s job.
