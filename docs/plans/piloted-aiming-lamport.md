# Piloted Aiming (lamport) — read the game's OWN aim pipeline at our keyboard cursor

**Status:** designed, verified against decompiled source (two adversarial passes), **not yet built / not yet
in-harness**. Supersedes the mod re-deriving hit numbers itself while aiming.

## Decision

Instead of recomputing hit%/damage/overpen/AoE ourselves (`HitPredictor`, `AoEPreview`), **drive the game's own
ability-aim pipeline so it evaluates at OUR keyboard cursor**, then **read the affected-target list the game itself
builds**. The game then does overpenetration pierce chains, AoE friendly-fire, faction gating, cover, ricochet,
best-shooting-position — correct-by-construction, at our tile. This is the "aim like the game does" direction the
maintainer chose (mutating/disabling the pointer explicitly permitted).

Chosen over the physical-cursor-warp (`SetCursorPos`) alternative: the warp needs reflection on `BaseCursor.Position`
(protected set), is re-clobbered by `PCCursor.OnUpdate` from the hardware each frame, requires the point on-screen,
and is skipped when `InGui`. The postfix has none of those problems.

## Why it works (verified this session)

- `AbilityRange.Update()` (`AbilityRange.cs:32`) is a plain MonoBehaviour `Update`, gated ONLY on
  `m_IsActive && ClickEventsController != null` — **no `IsControllerMouse` gate** (unlike `SurfaceMainInputLayer`).
  Our existing arm (`ClickWithSelectedAbilityHandler.SetAbility`) already flips `m_IsActive` via
  `HandleAbilityTargetSelectionStart`, so the per-frame loop is already live in our flow.
- Each frame it calls `SetRangeToWorldPosition`, which resolves the target from
  `ClickEventsController.WorldPosition` / `.PointerOn`, builds `m_AbilityTargets` via `GatherAffectedTargetsData`,
  pushes `CombatHUDRenderer.SetAbilityAreaHUD`, and raises `EventBus<ICellAbilityHandler>.HandleCellAbility(m_AbilityTargets)`.
- `PointerController.WorldPosition`/`PointerOn` are fed each `BeginOfFrame` from a hardware-cursor raycast
  (`PointerPosition => CursorController.CursorPosition`, `PointerController.cs:105`). The naive "write it once" is
  stomped every frame — **so inject every frame as a postfix on `PointerController.Tick`**, which runs after the
  body's write and is therefore the last write before `AbilityRange.Update` reads it (ordering-robust, no dependence
  on MonoBehaviour vs controller script order).

## Injection (write path) — `RTAccess/Combat/AimPointerDriver.cs`

Harmony **postfix** on `PointerController.Tick`:

```csharp
if (!Targeting.Aiming) return;               // Handler.Ability != null (AbilityTargeting.cs:26)
var pc = Game.Instance?.ClickEventsController; if (pc == null) return;
var unit = CursorTarget.Inside()?.TargetUnit;
pc.WorldPosition = MapCursor.Position;                                         // publicized private setter
pc.PointerOn     = unit?.View != null ? unit.View.gameObject : null;          // unit → faithful; null → GetTarget node-fallback
```

- Unconditional overwrite (the game's own write is conditional on a raycast hit; a miss would leak a stale value).
- `PointerOn = null` self-resolves: `GetTarget` falls back to `worldPosition.GetNearestNodeXZUnwalkable()?.GetUnit()`.
- Restore on commit/cancel is **automatic** — the postfix no-ops the instant `Targeting.Aiming` goes false.
- No mouse suppression needed (we're not warping the OS cursor; we clobber its write in the postfix each frame).

## Read (data back) — subscribe our own `ICellAbilityHandler`

```csharp
sealed class AimReadTap : ICellAbilityHandler {
    public readonly List<AbilityTargetUIData> Last = new();
    public void HandleCellAbility(List<AbilityTargetUIData> t) { Last.Clear(); Last.AddRange(t); } // SNAPSHOT; game reuses the list instance
}
```

Subscribe/dispose off the `_wasAiming` edge in `Targeting.Tick`. **Reject the overtip-VM walk** — the VM's
`CanAoETarget` faction gate ZEROES ally hit data, so it literally cannot answer "is my ally in the grenade"; the raw
list keeps friendly fire intact (`GatherAffectedTargetsData` does no faction filter).

Per entry (`AbilityTargetUIData`, all public): `Target` (guard `BaseUnitEntity` cast — destructibles/ricochet
appear too), `HitWithAvoidanceChance` (−1 = blocked/no-LOS), `Min/MaxDamage`, `Dodge/Parry/Cover/Block/Evasion`,
`CanPush`, `HitAlways`, `BurstIndex`, `BurstHitChances`, `IsAbilityRedirected`. `CanDie` = recompute
`MaxDamage >= HitPointsLeft + TemporaryHitPoints`.

**Legal-hit classification (mirror `OvertipHitChanceBlockVM.CalculateCanHit`):** an entry is a *hit the game shows*
iff `CanAoETarget(Blueprint.AoETargets)` (Enemy→`IsPlayerEnemy`, Ally→`IsPlayerFaction`, Any→true) **AND**
`CanTargetFromDesiredPosition(...)`. Voice ONLY these — an entry that FAILS this is geometrically in the pattern but
the ability cannot affect it, and the game shows NO overtip on it, so voicing it would VIOLATE visual parity and be
factually wrong.

**Friendly fire is the subset of legal-hit entries whose `Target` is an ALLY** (`caster.IsAlly(u)`), i.e. an ally
the ability WILL actually hit — this is exactly `Any`/`Ally`-targeted abilities (grenades), where the sighted player
DOES see the ally's overtip, so voicing the warning is parity-safe. **NOT** "entries that fail the faction gate"
(that inverts it: a faction-gate failure means no friendly fire and nothing shown). The raw-list advantages over the
overtip VM are therefore (a) it works off-screen / without hover, and (b) it carries the full per-unit struct
(overpen-reduced values, burst chances) the VM drops — **not** that it reveals extra friendly fire.

## What we voice

- **Primary line** (aimed unit): `"<name>, <hit%> to hit, <min>–<max> damage[, lethal][, can knock back]"`.
- **Overpen / pierce chain**: multiple in-line entries read in caster→out order.
- **AoE membership + FRIENDLY-FIRE warning** (the headline capability): "catches N enemies" + explicit
  `"WARNING: <ally> (ally) would take <min>–<max>"`.
- **Refusal reason** when illegal (game's own reason string).
- New additive cues: attack-of-opportunity warning (`UnitPredictionManager`), best-shooting-position hint
  (`LineOfSightVM.BestStartPos`, enemy-only + transient — read mid-aim, never cache), caster cover at firing tile.
- Verbose (O): avoidance breakdown + per-shot burst chances + area cell counts.

Keys unchanged — Enter commits at cursor, I on scanner selection, Backspace cancels; the postfix + tap only change
WHAT the existing move/cycle keys announce.

## Refactor / retire

- `HitPredictor` — **thin, keep** as the *pre-aim* estimator (no live pipeline when nothing is armed);
  `Targeting.PredictLine` reads the tap first, falls back to `HitPredictor.Describe`.
- `AoEPreview.ShapeLine` stays (arm announce); `AoEPreview.CursorTail` recompute is **superseded** by the tap.
- `AbilityTargeting` arm/commit/cancel — **untouched** (injection + read are purely additive).

## Risks (top)

- R1 per-frame stomp → postfix injects every frame. R2 retaining the handed list → snapshot via `AddRange`.
  R4 non-unit `Target` → guard cast. R5 list not faction-filtered → that's the friendly-fire feature; classify
  before calling anything a "hit". R6 hit%=−1/ricochet → special-case. R7 commit reads the same driven
  WorldPosition/PointerOn — acceptable (commits where we aim), keep ownership discipline in mind.

## Build order (each independently harness-testable)

1. **Injection only, no speech.** Postfix writes WorldPosition/PointerOn from MapCursor.Position. Verify via `/eval`
   that `WorldPosition` tracks the cursor and the game's red highlight / `CombatHUDRenderer` area moves to our tile.
2. **Read tap, log-only.** Subscribe `AimReadTap`; dump `Last` per cursor step to `rtaccess_log.txt`.
3. **Voice primary line + refusal.**
4. **Voice overpen chain + AoE membership + friendly-fire.**
5. **Reroute `Targeting.PredictLine` + scanner cycle to tap-first;** thin `AoEPreview.CursorTail` / `HitPredictor`.
6. **Additive cues:** AoO, best-shooting-position, caster cover, verbose O.

## In-harness probes

**P1 + P2 + AoE + friendly-fire: LIVE-CONFIRMED 2026-07-05** (TaintedStreets battle, forced-mouse TB combat, via
`/eval`). Method: arm a game ability through `SelectedAbilityHandler.SetAbility`, set `WorldPosition`/`PointerOn`
via the (reflected) private setters, force one `SetRangeToWorldPosition(casterDesired, true)` on the active range,
read the range's `m_AbilityTargets`, then `DropAbility()`+`ClearPointerMode()` (all cleanly reversible — `armed=False`
after each).
- **P2** ✓ `PointerController` is live and ticking; both `AbilitySingleTargetRange` and `AbilityPatternRange`
  MonoBehaviours are active+enabled in forced-mouse combat, `m_IsActive` flips true on arm. (`AbilityCounterAttackRange`
  is ALSO always active — read the SPECIFIC range type, not "first active".)
- **P1** ✓ From a COLD pointer (`WorldPosition=(0,0,0)`, zero hover), injecting `WorldPosition`+`PointerOn=enemy`
  for a Unit-anchored ability (Заклятый враг) → `AbilitySingleTargetRange.m_AbilityTargets` = 1, the exact enemy,
  `HitWithAvoidanceChance=100`. No hover needed.
- **AoE + FRIENDLY FIRE** ✓ Frag grenade (point AoE, range 8) aimed at ally Кассия →
  `AbilityPatternRange.m_AbilityTargets` = 1, `Кассия [ALLY]`, **dmg 9–18** — the ally IS captured with real damage
  (raw list is unfiltered by faction, as designed).
- **READ shape** ✓ `AbilityTargetUIData` members are PROPERTIES (`Target`, `HitWithAvoidanceChance`, `MinDamage`,
  `MaxDamage`, `CanPush`, `HitAlways`, …), not fields — read via getters. `HitAlways` matters: the grenade read
  `hit%=0` because it's a guaranteed area hit (`HitAlways=true`), so voice "sure hit" not "0%".

**Still to confirm during build (secondary/additive):**
- **P3 (step 4):** AoE pattern node-identity cache still refreshes across two different tiles mapping to one node;
  else pass `ignoreCache`.
- **P4 (step 6):** `LineOfSightControllerVM.LinesVMs` populated in our forced-mouse combat before relying on it.
