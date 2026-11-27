---
name: rt-scanner-consistency
description: "Scanner \"shows items not in M-cycle / tile\" divergence + the WA DetectableFrom fix + the Geo.SameArea navmesh-island interact trap"
metadata: 
  node_type: memory
  type: project
  originSessionId: abf1c10a-b098-494d-b402-d1371ab4897c
  modified: 2026-08-03T07:54:07.427Z
---

Playtester reported the scanner shows items that tile-exploration can't find and the M object-cycle doesn't list.
**Root cause: the three scanner surfaces gate visibility differently** (`RTAccess/Exploration/Scanner.cs` +
`InteractableDescriber.cs`):

- **Category browse** (Ctrl+PageUp/Down, `CategoryList`): `it.IsVisible` only — reveal-latched (`obj.IsInGame &&
  IsRevealed && IsAwarenessCheckPassed`; unit `IsPlayerFaction || IsVisibleForPlayer`). NOT fog-gated.
- **Review cycles** (M / , / . / N, `GroupList`): `IsVisible && CurrentlySeen` — adds `!IsInFogOfWar`.
- **Tile exploration** (arrows+Enter, `InteractablesAt`): proximity to the cursor tile + engine `HasAvailableInteractions`;
  not reveal-latched.

So a revealed-but-currently-fogged object shows in the category browse, vanishes from the M-cycle, and tile-walk only
finds it if you can reach its tile. WA reference has a **unified** gate RT lacked:
`DetectableFrom(cursor) = IsVisible && (CurrentlySeen || clear-LOS-from-cursor)` (`wotr-access ScanItem.cs:54`);
WA review cycles use it, RT's used blunt `CurrentlySeen`.

**Fix wired:** `ScanItem.DetectableFrom(cursor) = IsVisible && (CurrentlySeen ||
!LineOfSightGeometry.HasObstacle(cursor, nearest, 0.2))` into `GroupList`. LOS type =
`Kingmaker.Controllers.FogOfWar.LineOfSight.LineOfSightGeometry`.
**SUPERSEDED 2026-08-03 — that hand-rolled ray was WA's *retired* version and was silently admitting almost
everything.** See [[rt-visibility-model]] for the corrected rule (`PartVision.HasLOS`, cursor-as-observer) and for
the engine fact that settles the whole latch debate: units NEVER latch, statics do, and the engine hard-codes it.
Plus: empty-category skip
(`Scanner.NextNonEmptyCategory` + `scan.nothing_to_scan` locale, ports WA `NextCategoryIndex`); `ProxyMapObject.IsScannable`
folded into `IsVisible`; removed the Scenery browse category; `Scanner.MarkerList` perception-gates LOOT pins on
`m.IsVisible()` (objective/POI/important stay ungated); `ProxyMapObject.Footprint` collider-nearest-edge override.
`InteractableDescriber.IsActionable` stays the original bark-inclusive `HasAvailableInteractions || AreaTransitionPart`.

**User decisions:** (Q1, revised) surface a map object ONLY if it has a live ENABLED interaction (any part `Enabled`,
or a door that `IsOpen`), area exit, or local-map marker — the disabled-part check alone drops script zones (no parts),
secret doors, and emptied containers (parts disabled). (Q2) browse stays reveal-latched, M-cycle goes LOS-smart.
**BARKS ARE KEPT** (`InteractionBarkPart`, `Bark_*` play volumes ARE genuine clickable interactions for sighted
players — verified in decompiled `InteractionBarkPart`/`InteractionPart`/`ClickMapObjectHandler`: `CanInteract`/`OnInteract`,
`UIInteractionType.Info` "examine"; the gate does NOT special-case them, an enabled bark part passes like any other).

Live `/eval` dump of the repro save (Theodora area): 11/16 browse items were phantoms — invisible script/trigger
volumes (`*_SZ`, `LevelUpReminder` — 0 renderers, NO interaction parts), SECRET doors (`imperial_voidship_door_secret_*`,
door part disabled → spoiler), looted/inactive containers (`GoodLoot*`, `InteractionLootPart(off)`). Only the crime-scene
skill check was a legit reveal-latched interactable.

**Diagnostics** (per [[rt-visual-parity]]): `Diagnostics/ScannerDump.cs` (F11 dev key → `Scanner_visibility_dump.json`
in ModDir + Player.log, or `/eval DevApi.DumpScanner()`) flags phantom = `IsVisible && !DetectableFrom(origin)` (the
REAL M-cycle gate — re-admits fogged-but-clear-LOS items, not bare `!CurrentlySeen` which over-counts) and tags each
UNIT vs OBJECT (a fogged UNIT in the reveal-latched browse breaks visual parity → tighten; a fogged OBJECT is legit
local-map memory → keep). `Scanner.DebugInteract()` (F8 dev key / `DevApi.DebugScannerInteract()`) dumps selection +
co-located objects per-part (Enabled vs CanInteract() vs SelectUnit + turn state).

**I-key vs tile-Enter interact divergence** (same bug family): some objects are M-selectable but pressing **I** says
"can't interact", yet `Home` (plant cursor on selection) → step to its tile → `Enter` DOES interact. Two causes found:
- The actionability GATE (`ProxyMapObject.CanInteract` / `InteractableDescriber.IsActionable` = `HasAvailableInteractions`
  = ANY part `CanInteract()`) is LOOSER than what `ClickMapObjectHandler.Interact` can FIRE (a part fires only when
  `TryInteract` succeeds = `SelectUnit(units) != null` && `!IsPreparationTurn`; `InteractionType` has ONLY
  `Direct`+`Approach` so the Type gate never rejects; barks default to `Direct` so they DO fire). So an object passes
  the gate but `Interact()` returns false. Fix: `Scanner.Interact()`, when the selection's own interaction doesn't fire
  (or it's a unit/non-actionable), falls through to a proximity resolve **at the selection's tile**
  (`Activation.TryCursorObject(Vector3)` / `InteractableDescriber.InteractablesAt(Vector3)` overloads, reach≈2m) —
  automating the Home+Enter workaround; common case unchanged → no regression.
- **The actual culprit was the `Geo.SameArea` cross-area pre-guard, not the interact-fire gate.** The refusal
  ("Can't reach …" = `scan.cant_reach_area`) was spoken by `if (!Geo.SameArea(anchor.Position, sel.Position)) …` BEFORE
  any interact attempt. `Geo.SameArea` compares navmesh CONNECTED COMPONENTS (`ObstacleAnalyzer.GetArea(a)==GetArea(b)`),
  so a solid object like a pedestal (`PostamentsObsidian`) whose `Position` snaps to a disconnected navmesh island the
  party can't stand ON tests as a different "area" → bogus refusal. Enter interacted it fine precisely because
  `Activation.TryCursorObject` has NO such guard. **Fix:** removed the `Geo.SameArea` guard from BOTH `Scanner.Interact()`
  and `Scanner.TryActivateSelection()` — reachability is now left to the game's own `ApproachAndInteract` (walks a unit
  to the object), exactly what Enter relies on. The guard was dead weight: a cross-area selection already resolves to
  null in `ResolveSelected` (`WorldModel.Find` is per-area). `Geo.SameArea` + its `NoArea` const + `Geo.OnNavmesh`
  (zero-caller wrapper over `NavmeshProbe.OnMesh`) + the `scan.cant_reach_area` locale key were all removed; `Geo` now
  holds only `Live`/`Distance`/`SnapToWalkable`/`RegionWord`.

Open caveat: the `Geo.SameArea` removal needs a full game RESTART to load (UMM loads mods at process start; a save
reload is NOT enough) — final in-game confirm on `PostamentsObsidian` still pending.

Report: `docs/scanner-vs-wrathaccess-parity.md`. Extends [[rt-scanner-explorer-audit]]; reference
[[wotr-access-prior-art]]; compile trick [[rt-compile-check]].
