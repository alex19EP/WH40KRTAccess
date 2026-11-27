---
name: rt-cover-los
description: "RT walkability/walls/cover are fully queryable: node flags + fences (cut connections w/ height) for cover; DestructibleEntities to enumerate walls; LosCalculations for LOS"
metadata: 
  node_type: memory
  type: reference
  originSessionId: d6beca97-d0c6-42a4-9e6e-4dc69c9b1576
---

How to know walkable tiles, WHY a tile is blocked, and enumerate walls/cover (for combat a11y).
Builds on [[rt-world-grid]] (square 1.35m grid, CustomGridGraph).

**Walkability:** get graph via `AstarPath.active` / `CustomGridNode.GetGridGraph(0)` → `nodes` (CustomGridNode[]),
`width`/`depth`/`GetNode(x,z)`. Per node: `Walkable`/`StaticWalkable` (baked geometry; Walkable=post-erosion),
`Vector3Position`, `XCoordinateInGrid`/`ZCoordinateInGrid`, `NodeIndex`. NOTE: a unit standing on a cell is NOT
in `Walkable` — tracked separately in `WarhammerBlockManager.NodeContainsAny(node)` / `node.GetUnit()`.

**Reason a tile is blocked is NOT stored on the node** (node = bool + tag + penalty + 8 conn bits + 8 fence
heights). Recover by category (baked in `CustomGridGraph.RecalculateCell`): no-floor/steep-slope (raycast vs
maxSlope/maxClimb); solid prop → `DestructibleEntity.FindByNode(node)` (`DestructibleEntity.cs:127`); designer
navmesh-cut (`GridCuts`/`NavmeshMasks`) = bare geometry, no entity; dynamic unit → `node.GetUnit()`.

**THIN COVER = FENCES, not unwalkable tiles (key)** — solid room-dividing walls are UNWALKABLE cells instead (the room-segmentation substrate; see [[rt-room-classifier]]). Thin walls/cover/railings: both adjacent cells stay walkable;
the EDGE between them carries a height. Heights stored x1000 (mm). `node.HasFenceWithNode(nbr, out h)` /
`node.IsConnectionCutAlongDirection(dir, out h)`. Thresholds (`LosCalculations`): >1500 = Full cover, >500 = Half.
Dir indices: 0=S 1=E 2=N 3=W 4=SE 5=NE 6=NW 7=SW. Fences come from `ThinCover` (GridNavmeshModifier) at bake.
Solid walls/buildings = unwalkable nodes + occupying `DestructibleEntity`/`CoverEntity` (or bare baked geometry).

**Enumerate all walls/cover for an area:** `Game.Instance.State.DestructibleEntities` (EntityPool<DestructibleEntity>,
`PersistentState.cs:36`) → filter `.OfType<CoverEntity>()` / `.OfType<ThinCoverEntity>()`; each has `GetOccupiedNodes()`,
ThinCover has `IsCoverBetween(n1,n2)` + static `FindThinCover(node,dir)`. Plus sweep node fences for cover edges that
aren't entities. Forced (non-geometry) cover: `Game.Instance.ForcedCoversController.TryGetCover(node, entity, out type)` (there is no arg-less `TryGetCoverType` — see the display-gate note below).

**LOS/cover API** (`Kingmaker.View.Covers.LosCalculations`, static): `CoverType {None,Half,Full,Invisible}` (line 15);
`GetCellCoverStatus(node, dir)` → LosDescription per edge; `GetWarhammerLos(from, to)` → LosDescription whose
`ObstacleEntity` resolves the blocking wall/cover/unit. EyeShift = 1.5m up. Distance helper
`WarhammerGeometryUtils.DistanceToInCells(...)`.

**GAME'S OWN REFERENCE** = `Kingmaker.UI.CoverVisualiserSystem.CoverVisualizer`: caches DestructibleEntities by
occupied node, then for a 5x5 block around cursor cell calls `GetCellCoverStatus(node, dir)` on the 4 cardinal edges
(N/E/S/W = dirs 2/1/0/3) and shows per-edge None/Half/Full indicator. Directly reusable for a "walls/cover around me"
spoken readout. See [[rt-a11y-architecture-verdict]] (combat = bespoke spatial navigator).

**COVER-MESH DISPLAY GATE (verified 2026-07-05, load-bearing — the old DescribeTile comment got this WRONG).**
`CoverVisualizer.IsNodeCoverVisible` shows a cell's cover iff `m_IsPlayerTurn && (inMovableArea || KeyboardAccess.IsCtrlHold())
&& SelectedAbility==null`, **OR** `m_IsDeploymentPhase`; cell must be `Walkable`; suppressed on a cell whose unit itself
provides cover (`unit.Features.ProvidesFullCover/HalfCover`). So cover is **NOT reachability-gated** — Ctrl-hold reveals it
on nearby UNREACHABLE tiles, and the whole deployment zone shows it with no CurrentUnit. It paints with **`ForcedCoverCheckType.BySource`**
(entity = `SelectionCharacter.SelectedUnit.Value`), NOT the `GetCellCoverStatus` default `ByTarget` (entity = `node.GetUnit()`, null on
an empty tile). Only 4 cardinal edges — no diagonal cover (`GetCoverType` loops i<4).
**Forced/dynamic cover** = `IDynamicCoverProvider` registered per-node in `ForcedCoversController.TryGetCover(node, entity, out type)`
(NOT the arg-less `TryGetCoverType` an older note claimed): `MapObjectProvidesCoverPart.CanUseCover`⇒always true (barricades);
`PartProvidesFullCover.CanUseCover`⇒`exclusiveUsers.Count==0 ? true : Contains(entity)` — so `AddForcedCoverUser`-granted cover
needs BySource(=the granted ally) or it reads None. Target-relative overtip = `OvertipCoverBlockVM.UpdateCover` (best-shooting-pos
→ GetWarhammerLos; enemy-only, player-turn, no ability gate → shows Invisible/"blocked" too). The mod's tile readout
(`InteractableDescriber.DescribeTile`) was fixed to match all of this (commit on `overnight-pathfinder-port`).
