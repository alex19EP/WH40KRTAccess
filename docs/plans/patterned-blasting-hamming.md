Verified the load-bearing corrections against decompiled source (`ClickWithSelectedAbilityHandler.cs:217` anchors at `GetDesiredPosition`; `VirtualPositionController.cs:65-72` falls back to `entity.Position`; `OrientedPatternData.cs:57` `ApplicationNode` is `[CanBeNull]`, `:77-88` builds with `m_NodesExtraData=null`). Final plan below.

---

# B3 v2 — Accessible AoE / Point-Cell Aim Preview — Implementation Plan

Codename target file: `docs/plans/phased-skirmishing-liskov.md` (B3 v2 slice). Buildable against shipped B3 v1 (`Exploration/Targeting.cs`, `Exploration/AbilityTargeting.cs`, `Accessibility/TileExplorer.cs`, `Accessibility/HitPredictor.cs`) and the verified engine APIs below.

**Review resolution summary.** The v1-draft's central invariant — "preview == commit because the caster anchor is `Caster.Position` in mouse mode" — was **wrong** and is fixed here: the game's commit anchors the cast at `Game.Instance.VirtualPositionController.GetDesiredPosition(Ability.Caster)` (`ClickWithSelectedAbilityHandler.cs:217`), which is **not** always `Caster.Position` — `m_VirtualPosition` is a plain settable field (`VirtualPositionController.cs:22-39,65-72`) that `UnitPredictionManager` populates for predicted-caster-position abilities (`AbilityCasterDesiredPositionFromSelectedTarget`), no mouse involved. So the preview must anchor at the **same `GetDesiredPosition(caster)` expression the commit uses**, not at the raw actual cell (§6, blocker). The proposed HitPredictor "fix to actual cell" is dropped as a reticle-parity regression (§6, major). Four minor edge fixes folded into §5 (Custom-pattern NRE, out-of-range ApplicationNode, null-Pattern⇒all-area narrowing, zero-vector facing).

---

## 0. What exists today (the seam we extend)

- `Targeting.cs:24` `Aiming => Ability.Active`; `Tick()` (`:78`) announces the null→armed edge via `ArmAnnounce()` (`:93`); `PredictLine()` (`:56`) is the per-enemy to-hit tail the scanner appends.
- `AbilityTargeting.cs:33` `CommitAt(unit, point)` → `Handler.OnClick(go, point, 0)`; multi-target is a bare `aim.target_added` (`:41`); `Cancel()` (`:47`) → `ClickEventsController.ClearPointerMode()`.
- `TileExplorer.Describe()` (`:283`) builds the cursor-tile line and **already appends a mode tail** — `DeploymentMode.CursorTail(MapCursor.Node)` when deploying (`:288-292`). This is the exact composition slot for the AoE tail.
- `MapCursor` exposes `Node` (`MapCursor.cs:26`), `Position` (`:32`), `Has` (`:29`) — the single shared review/movement cursor (D4). Arrow keys in `TileExplorer` move it and call `Announce()`→`Describe()`.

The v2 work is a **new pure-read describer** (`AoEPreview`) plus **three small edits** to compose it into `Describe()`, `ArmAnnounce()`, and the multi-target branch of `CommitAt`. No new `InputCategory`, no new keys except one optional rotate/toggle.

---

## 1. The AoE aim-preview read

Fires as a **tail on the existing cursor-tile readout** every time the shared cursor moves while an AoE ability is armed. Order: `<tile line>. <shape+size>. <center/offset/range>. <tiles + units caught>`. Terse by default (fast stepping clips at the headline via `interrupt:true`); the full units list is on the re-announce key (O) and the scanner cycle.

### Speech shape (examples)

- Blast, in range: `"Frag grenade. Blast, 2-cell radius. Centre 4 cells out, in range. 13 tiles, catches 3 enemies."`
- Cone, ally caught: `"Flamer. Cone, length 6, 90 degrees, facing north-east. In range. 9 tiles, catches 2 enemies, 1 ally. Warning: Cassia caught."`
- Line: `"Line, length 8, facing east. In range. 8 tiles, catches 1 enemy."`
- Out of range (previewed at the clamped landing, mirroring the reticle): `"Blast, 2-cell radius. Out of range, lands 3 cells short. 13 tiles, catches 0 enemies."`
- Whole-area effect (`BlueprintAbilityAreaEffect.IsAllArea`): `"Affects the whole area."`
- Scatter (fuzzy, flagged): `"Scatter shot, spread 5. In range. Roughly 6 tiles, may catch 2 enemies."`

### Loc.T keys to add (`assets/locale/enGB/ui.json`, `aim.*` block after line 482)

```
"aim.shape_blast":       "Blast, {radius}-cell radius",
"aim.shape_cone":        "Cone, length {radius}, {angle} degrees",
"aim.shape_line":        "Line, length {radius}",
"aim.shape_sector":      "Sector, radius {radius}, {angle} degrees",
"aim.shape_special":     "Special pattern",
"aim.shape_scatter":     "Scatter shot, spread {radius}",
"aim.whole_area":        "Affects the whole area.",
"aim.facing":            "facing {dir}",
"aim.centre_offset":     "Centre {cells} cells out",
"aim.centre_here":       "Centred on you",
"aim.in_range":          "in range",
"aim.lands_short":       "out of range, lands {cells} short",
"aim.tiles":             "{count} tiles",
"aim.tile_one":          "1 tile",
"aim.catches_enemies":   "catches {count} enemies",
"aim.catches_enemy_one": "catches 1 enemy",
"aim.and_allies":        "{count} allies",
"aim.and_ally_one":      "1 ally",
"aim.ff_warning":        "Warning: {names} caught",
"aim.scatter_maybe":     "roughly {count} tiles, may catch {enemies} enemies",
"aim.no_targets":        "catches nothing"
```

Pluralization uses the `*_one` keys the same way `aim.range_one` already does (`Targeting.cs:102`). 8-way direction words reuse `Geo.cs` bearing words if present (else add `aim.dir_n` … `aim.dir_nw`). No `aim.rotate_hint` — rotate is deferred (§3).

---

## 2. Hook-in (rides the D4 unified cursor; no new category)

**Where the read fires.** In `TileExplorer.Describe()` (`:283`), add a second tail branch, mutually exclusive with the deployment tail:

```csharp
if (RTAccess.Exploration.DeploymentMode.Active)
    line += ". " + RTAccess.Exploration.DeploymentMode.CursorTail(MapCursor.Node);
else if (RTAccess.Exploration.Targeting.Aiming)
{
    var tail = RTAccess.Exploration.AoEPreview.CursorTail(MapCursor.Node);   // null for single-target abilities
    if (!string.IsNullOrWhiteSpace(tail)) line += ". " + tail;
}
```

Arrow-move already routes through `Announce()`→`Describe()` with `interrupt:true`, so the preview is **keypress-driven and pure** by construction — no per-frame `Tick`, no engine churn. `AoEPreview.CursorTail` returns `null` when the armed ability is single-target (`GetPatternSettings()==null`), so single-target aim keeps v1 behavior untouched. `DeploymentMode.Active` and `Targeting.Aiming` are mutually exclusive combat phases; the `else if` makes it explicit. The V-key vantage read (`ReadVantage`, `:241`) is a different key and unaffected.

**Compose with PredictLine.** `PredictLine` (`Targeting.cs:56`, consumed at `Scanner.cs:283/555`) stays the **per-enemy** to-hit tail on the *scanner* cycle. `AoEPreview` is the **area** tail on the *cursor* readout. Orthogonal: cursor movement → area shape/count; scanner cycle (period/comma) → the named unit + its hit%. No double-speak.

**Arm-edge announce.** Extend `ArmAnnounce()` (`Targeting.cs:93`): for an AoE ability append `AoEPreview.ShapeLine(ability)` once at arm time (so the player hears "Cone, length 6" before moving). `aim.point_help` already covers the "move cursor + Enter" control hint.

---

## 3. Orientation (mouse-less directional patterns)

**Default = caster→point facing, and it is correct.** `AoEPattern.GetCastDirection` (`AoEPattern.cs:246`) derives cone/line/sector direction purely from `casterNode`/`castNode`/`targetNode` geometry — it never reads `TargetWrapper.Orientation`. The commit path `GetTarget` (`ClickWithSelectedAbilityHandler.cs:199-204`) sets orientation to `LookRotation(castPoint - casterNode)` (identity when the vector is zero). So **moving the cursor around the caster re-aims the cone automatically at both preview and commit — no rotate control required.**

**Audible steering.** For directional patterns only (Cone/Ray/Sector/Scatter), append an 8-way compass word to the shape line. **Compute it from the same node the engine uses, not the caster center**, to avoid a large-caster axis mismatch: derive the direction vector from `ApplicationNode.XZ - casterNode.XZ` of the *previewed* `OrientedPatternData` (or, cheaply, `cursorNode.XZ - casterNode.XZ` for a 1-cell caster). **When the vector is zero** (cursor planted on the caster's own tile → `Quaternion.identity`/north at commit), **suppress the facing word and emit `aim.centre_here`** instead of an undefined direction. For 1-cell casters (the common case) the center-based bearing is exact; the ApplicationNode-based form is the robust general path.

**Explicit rotate: deferred.** `OnClick` takes only `(go, point, button)` — no rotation param — so any rotate control is a *preview-only synthetic-cell* affordance that the commit ignores (it fires at the real cursor cell), risking preview≠commit desync. **Ship v2 without rotate.** Default facing is faithful to the mouse model. Gate a future `[` / `]` synthetic-rotate behind maintainer ear-test; if added, it must move the *actual cursor cell* (not a phantom aim point) so commit still matches.

---

## 4. Multi-target ("target k of n" + cancel cleanup)

**Do NOT cache n once at arm time.** Some `IAbilityMultiTarget` implementations make the remaining-target budget a function of prior picks (`AbilityMultiTargetSelectionHandler.GetAbilityForNextTarget` probes `TryGetNextTargetAbility(rootAbility, m_Targets.Count, …)`, `:57-72`). Compute the total **each time the ability re-arms between targets** (cheap, and correct for variable-budget abilities):

```csharp
var mt = Handler.MultiTargetHandler;                       // ClickWithSelectedAbilityHandler.cs:66
if (mt?.AbilityMultiTarget == null) _n = 1;
else { _n = mt.Targets.Count; while (mt.AbilityMultiTarget.TryGetNextTargetAbility(Handler.RootAbility, _n, out _)) _n++; }
```

Recompute in `Targeting.Tick()` on the null→armed edge **and** whenever `Handler.Ability` transitions non-null after a commit (the re-arm for target k+1). Reset `_n` on the armed→null edge. (`RootAbility` at `ClickWithSelectedAbilityHandler.cs:62`; `Targets` at `AbilityMultiTargetSelectionHandler.cs:15`.)

**Replace the bare `aim.target_added`** in `AbilityTargeting.CommitAt` (`:41`): when `h.Ability != null` after `OnClick` (more targets wanted), announce `k = Handler.MultiTargetHandler.Targets.Count` (post-commit count = the index just filled):

```
"aim.target_k_of_n": "Target {k} of {n} chosen, pick the next."
```

If `_n` is unknown/degenerate (`_n <= k`), fall back to a countless `aim.target_added` ("Target chosen, pick the next.") rather than announce a stale "of n".

**Cancel cleanup is already correct and drains multi-target state.** `Cancel()` (`AbilityTargeting.cs:49`) → `ClearPointerMode()` → `PointerController` sees `Mode==Ability` → `DropAbility()` → `OnRootAbilitySelected(null)` → `m_Targets.Clear()` (`AbilityMultiTargetSelectionHandler.cs:45-48`), even mid-accumulation. No change needed; just reset our cached `_n` on the armed→null edge in `Tick()`.

---

## 5. Exact APIs, files, keys

### New file: `RTAccess/Exploration/AoEPreview.cs`
Pure-read static describer. Public surface: `string ShapeLine(AbilityData)` (arm edge) and `string CursorTail(CustomGridNodeBase cursorNode)` (per-move). Algorithm:

1. **Gate** — `var ab = Game.Instance?.SelectedAbilityHandler?.Ability; var prov = ab?.GetPatternSettings();` → null ⇒ return null (single-target). (`AbilityData.cs:2126`)
2. **Whole-area guard** — narrow to the concrete area-effect: `if (prov is BlueprintAbilityAreaEffect { IsAllArea: true } || prov.Pattern == null) ⇒ aim.whole_area`. (`BlueprintAbilityAreaEffect.cs:95-105` returns null `Pattern` iff `IsAllArea`; `IAbilityAoEPatternProvider.Pattern` is `[CanBeNull]` with no general null⇔all-area contract, so gate on the concrete type first and treat a null `Pattern` from any other provider as all-area only as a labeled fallback.)
3. **Shape/size — branch on `pat.Type` FIRST, then read only the fields that shape consumes.** `var pat = prov.Pattern;` switch `pat.Type` (`AoEPattern.cs:41`): Circle→`aim.shape_blast` (read `pat.Radius` `:88`), Cone→`aim.shape_cone` (read `pat.Radius`,`pat.Angle` `:78`), Ray→`aim.shape_line` (`pat.Radius`), Sector→`aim.shape_sector` (`pat.Radius`,`pat.Angle`), Custom→`aim.shape_special` (**read neither** `Radius` nor `Angle`). Rationale: `Radius`/`Angle` dereference `Blueprint = m_Blueprint?.Get()` (`AoEPattern.cs:76,83,96`) which is null for a Custom pattern with an unset blueprint ref → NRE. `ab.IsScatter` (`AbilityData.cs:510`) → `aim.shape_scatter` (`pat.Radius`).
4. **Caster anchor — take the SAME expression the commit uses** (§6): `var casterPos = Game.Instance.VirtualPositionController?.GetDesiredPosition(ab.Caster) ?? ab.Caster.Position; var casterNode = AoEPatternHelper.GetGridNode(casterPos);` (`AoEPatternHelper.cs:112`). Do **not** hard-code `ab.Caster.Position`.
5. **Range/offset** — `ab.CanTargetFromNode(casterNode, null, new TargetWrapper(cursorNode.Vector3Position), out int cells, out _, out var reason)` (the explicit-cell overload HitPredictor uses, `AbilityData.cs:1622`, `HitPredictor.cs:49`). `reason==TargetTooFar/TargetTooClose` (`:94-95`) ⇒ compute short-by from `AoEPatternHelper.GetActualCastPosition(ab.Caster, casterNode, cursorPos, ab.MinRangeCells, ab.RangeCells)` (`:47`) → `aim.lands_short`; else `aim.in_range`. Offset = `cells` → `aim.centre_offset` (0 ⇒ `aim.centre_here`).
6. **Affected set (pure)** — `var pattern = ab.GetPattern(new TargetWrapper(cursorNode.Vector3Position), casterPos);` (`AbilityData.cs:2113`, **explicit `casterPosition` arg = the §4 anchor**). `if (pattern.IsEmpty)` (`OrientedPatternData.cs:63`) ⇒ `aim.no_targets`. Tile count = enumerate `pattern.Nodes` (`:59`, skips off-graph → equals red-highlight count).
7. **Units caught + role** — iterate the WorldModel combat-unit registry; for each `u`, `AoEPatternHelper.WouldTargetEntity(pattern, u)` (`:100`, handles multi-cell footprints). Bucket via `caster.IsEnemy(u)` (enemy) vs `!caster.IsEnemy(u) && u != caster && caster.GetCombatGroupOptional()?.IsAlly(u)==true` (ally). **Fog-gate**: only name/count units with `u.IsVisibleForPlayer` (visual parity). Enemies → count; allies → count + names for `aim.ff_warning`.
8. **Primary vs splash (optional, terse-off by default)** — primary = `pattern.ApplicationNode?.GetUnit()` (`OrientedPatternData.cs:57`, `[CanBeNull]`), **not** `cursorNode.GetUnit()`: out of range, `GetOrientedPattern` passes the range-clamped `actualCastNode` (`AoEPatternHelper.cs:129`) — or `outerNodeNearestToTarget` for directional/scatter (`:140`) — as `applicationNode`, so `ApplicationNode != cursorNode` when the aim is clamped. **Do NOT rely on `PatternCellData.MainCell`** — the `GetPattern` path builds via the `ReadonlyList` ctor with `m_NodesExtraData==null` (`OrientedPatternData.cs:77-88`, verified), so `MainCell` reads false everywhere. Use the `ApplicationNode` occupant.

### Edits
- `Accessibility/TileExplorer.cs:288` — the `else if (Targeting.Aiming)` AoE tail branch (§2).
- `Exploration/Targeting.cs:93` `ArmAnnounce()` — append `AoEPreview.ShapeLine(ability)` for AoE; `Tick()` (`:78`) — recompute `_n` on arm edge **and** post-commit re-arm, reset on disarm (§4).
- `Exploration/AbilityTargeting.cs:41` — replace `aim.target_added` with `aim.target_k_of_n` (+ countless fallback, §4).
- `assets/locale/enGB/ui.json` — add the `aim.*` keys from §1 and §4 after line 482.
- `Accessibility/HitPredictor.cs` — **no change** (§6 major).

---

## 6. casterPosition-drift discipline (the one-explicit-cell rule, corrected)

The commit anchors the cast at **`Game.Instance.VirtualPositionController.GetDesiredPosition(Ability.Caster)`** (`ClickWithSelectedAbilityHandler.cs:217`, fed into `GetTarget`'s `casterPosition` at `:218`). `GetDesiredPosition` returns `entity.Position` **only when** `m_VirtualPosition` is unset (`VirtualPositionController.cs:65-72`) — but `m_VirtualPosition` is a plain settable field (`:22-39`) that `UnitPredictionManager` populates for predicted-caster-position abilities (`AbilityCasterDesiredPositionFromSelectedTarget`), no mouse involved. So `Caster.Position` is **not** a safe stand-in in general.

**Rule: the preview anchors at the exact same call the commit uses.** One caster point, resolved once per preview via `GetDesiredPosition(caster)`, threaded through every call:

```
casterPos  = Game.Instance.VirtualPositionController?.GetDesiredPosition(ab.Caster) ?? ab.Caster.Position;
casterNode = AoEPatternHelper.GetGridNode(casterPos);            // AoEPatternHelper.cs:112
cursorNode = MapCursor.Node;   cursorPos = cursorNode.Vector3Position;   // the shared D4 cursor
```
- **Validate**: `ab.CanTargetFromNode(casterNode, null, tw(cursorPos), …)` — explicit-cell overload (`AbilityData.cs:1622`), **not** `CanTargetFromDesiredPosition`.
- **Pattern**: `ab.GetPattern(tw(cursorPos), casterPos)` — explicit `casterPosition` arg (`:2113`).
- **Range clamp**: `AoEPatternHelper.GetActualCastPosition(ab.Caster, casterNode, cursorPos, MinRangeCells, RangeCells)` (`:47`).
- **Commit**: unchanged `Handler.OnClick(go, cursorPos, 0)` — internally re-derives `GetDesiredPosition(Caster)` (`:217`), the **same value** the preview used ⇒ preview == commit by construction, including predicted-position/charge casters.

**The correct forbidden.** The single-explicit-cell rule applies to the **AIM POINT** (the target): never let `GetDesiredPosition` or `GetBestShootingPositionForDesiredPosition` supply the aim point — those are engine-dead in mouse mode and must stay the explicit cursor cell. It does **NOT** apply to the **CASTER anchor**: for the caster you must *use* `GetDesiredPosition(caster)` to match the commit. (The v1 draft inverted this and would have silently drifted on any predicted-position ability.)

**HitPredictor stays as-is (dropped v1 "fix").** `HitPredictor.Describe` (`HitPredictor.cs:44-54`) resolves its shoot node via `GetDesiredPosition` + `GetBestShootingPositionForDesiredPosition` **on purpose** — that is exactly the frame the on-screen reticle / `UnitPredictionManager.GetBestShootingPosition` evaluates, so its spoken hit% matches the reticle. Forcing it to the actual cell would make it announce a *different* number than the sighted reticle for predicted-position abilities — a parity regression. Both surfaces already share the reticle's frame because `AoEPreview` now also anchors at `GetDesiredPosition(caster)`. No signature change.

**Purity**: `GetPattern` allocates pooled temp collections but mutates no world/VirtualPosition state; the WorldModel-registry walk + `WouldTargetEntity` are pure reads. Prefer this over `GatherAffectedTargetsData` — the latter genuinely mutates engine state (calls `CreateExecutionContext` `AbilityDataHelper.cs:573` and writes `AbilityTargetUIDataCache.Instance.AddOrReplace` `:592,606,614`) and clones the ability per target. `GetDesiredPosition` is a pure read (no set). Only fall back to `GatherAffectedTargetsData` if the pure walk is found to miss redirect/additional targets (verify in §7).

**Provenance**: every `AoEPreview` line is emitted from an arrow/enter keypress → `interrupt:true` (already how `TileExplorer.Announce` speaks).

---

## 7. Risks + live-verification (dev harness `/eval`, real TB fight)

Each row is an `/eval` one-liner against an armed AoE in a live turn-based fight (arm a grenade/flamer from the action bar first, plant the cursor).

| # | Risk | Live-verify via `/eval` |
|---|------|------------------------|
| R1 | **Preview ≠ commit** (drift) | Cursor planted: dump `ab.GetPattern(tw(cursorPos), caster_anchor).Nodes.Count()` (with `caster_anchor = VirtualPositionController.GetDesiredPosition(caster)`) and `ApplicationNode`; then actually `OnClick` and confirm the red-highlight tile count matches. Equal ⇒ no drift. |
| R1b | **Predicted-position drift** (the blocker case) | Arm a multi-target AoE with `AbilityCasterDesiredPositionFromSelectedTarget`; commit target 1; then dump `GetDesiredPosition(caster) != caster.Position` (expect true) and confirm the preview's `casterPos` **follows** `GetDesiredPosition`, and the resulting pattern matches the commit for target 2. |
| R2 | **`GetPatternSettings()==null` for a real AoE** | For each `IsAOE` ability of the acting unit print `GetPatternSettings()==null`. Any AoE returning null ⇒ check the `WarhammerAbilityAttackDelivery.PatternProvider`/spawn-area path. |
| R2b | **Custom-pattern NRE** | Arm any `PatternType.Custom` ability; confirm `AoEPreview` returns `aim.shape_special` **without** throwing (i.e. `Radius`/`Angle` are never touched on the Custom branch). |
| R3 | **Scatter over/understated** | On `IsScatter`, compare `GetPattern` node count vs what actually gets hit across casts; confirm `aim.scatter_maybe` "roughly/may" wording is honest. |
| R4 | **Cone facing wrong** | Arm a cone; move cursor N/E/S/W of caster; dump `pattern.Nodes` centroids + spoken 8-way word; confirm the cone points at the cursor and the word matches `ApplicationNode-casterNode`. Cursor on caster's own tile ⇒ `aim.centre_here`, no facing word. |
| R5 | **Ally not flagged** | Put a companion inside a blast; confirm the ally bucket catches them and `aim.ff_warning` names them; cross-check `caster.IsEnemy(ally)==false && IsAlly==true`. |
| R6 | **Fog leak** | Aim a blast over an unseen tile with a hidden enemy; confirm caught-count excludes `!IsVisibleForPlayer` units. |
| R7 | **Multi-target n wrong / variable budget** | On a multi-target ability print the `TryGetNextTargetAbility` probe for `_n` at each re-arm; commit one target and confirm `Targets.Count` gives the right k; verify `_n` **recomputes** between targets (not stale); cancel mid-accumulation and confirm `Targets.Count==0` after `ClearPointerMode`. |
| R8 | **Perf churn** | Confirm `CursorTail` is only reached from `Describe()` (keypress), never a per-frame `Tick`; large-pattern step latency stays imperceptible. |
| R9 | **Whole-area / Custom label** | Arm an `IsAllArea` effect ⇒ `aim.whole_area`, no tile math; a `Custom` pattern ⇒ `aim.shape_special` + a valid node count; confirm a non-area provider with null `Pattern` (if any exists) is not mislabeled beyond the fallback note. |

Manual (shared-harness rule — if another session is driving the game, hand the user these steps instead): arm a frag grenade, arrow the cursor toward a cluster, confirm the spoken tail matches the on-screen red template's tile count and the enemies in it, then Enter and confirm the same units are hit.

---

## Open risks / must-verify-live

- **R1b (predicted-position parity) is the load-bearing unknown.** The fix (anchor preview at `GetDesiredPosition(caster)`) is proven correct against `ClickWithSelectedAbilityHandler.cs:217` + `VirtualPositionController.cs:65-72`, but whether any *base-game* AoE actually carries `AbilityCasterDesiredPositionFromSelectedTarget` (vs DLC-only) is unverified. Even if none does today, the anchor-at-`GetDesiredPosition` discipline is the robust default and costs nothing. Must confirm no live drift once such an ability is available.
- **`GetBestShootingNode` shift for large casters** — `GetPattern` (`AbilityData.cs:2122`) may re-derive the effective cast node via `GetBestShootingNode` for a non-Medium caster even when the anchor point matches. R1 covers the common (Medium) case; large-caster AoE (rare) needs a spot check that preview tiles still equal commit tiles.
- **`IsAllArea` fallback generality** — the null-`Pattern`⇒all-area path is gated on the concrete `BlueprintAbilityAreaEffect` type first (§5.2); if a non-area provider with a null `Pattern` exists, it falls through to the labeled all-area fallback. Verify no such provider is mislabeled (R9).
- **Rotate affordance** — shipped without `[`/`]` synthetic rotate (§3). If ear-test shows blind users want in-place cone rotation, it must move the *actual cursor cell* (never a phantom aim point) to keep commit==preview; revisit only after maintainer ear-test.
- **8-way bearing vs true engine axis for large casters** — spoken facing is `ApplicationNode-casterNode`-based, which matches the engine for 1-cell casters; a multi-cell caster's real cone axis can differ by up to a cell (`AoEPatternHelper.cs:130-141` uses inner/outer-nearest nodes). Acceptable approximation; note in code.

---

## AI Usage Disclosure

> This change was developed with assistance from Claude (Anthropic). All code was reviewed and tested by the author before submission.