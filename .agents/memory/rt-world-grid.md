---
name: rt-world-grid
description: RT surface/area world is a SQUARE tile grid (1.35m cells); units occupy IntRect footprints by Size; one solid unit per tile
metadata: 
  node_type: memory
  type: reference
  originSessionId: d6beca97-d0c6-42a4-9e6e-4dc69c9b1576
---

The walkable surface of every area (exploration AND ground combat) is baked onto ONE
`CustomGridGraph` (`Kingmaker.Pathfinding`) — Owlcat's fork of the A* Pathfinding Project.
Combat just *renders* these nodes as visible tiles; exploration uses the same nodes for click-to-move.

- **Square tiles, not hex.** `NumNeighbours { Four, Eight, Six }` (Six=hex, supported but unused);
  RT inits to `Eight` (`CustomGridGraph.cs:173`) = 4 orthogonal + 4 diagonal. Node centers at
  `(x+0.5, z+0.5)` (`GraphPointToWorld`), drawn as Quads; helper literally `GetCornerDegreesSquareUnits`
  with corners `{-135,-45,45,135}`.
- **Cell size = 1.35 m** (`GraphParamsMechanicsCache.DefaultGridCellSize = 1.35f`; runtime reads baked
  `nodeSize`). Distances counted in whole `Cells` struct (`Cells.cs`): `Meters = Value * 1.35`. So ability
  "range 8" = 8 tiles ≈ 10.8 m. Useful: `WarhammerGeometryUtils.DistanceToInCells(...)`.
- **Multi-cell footprints by Size** (`SizePathfindingHelper.GetRectForSize`, returns `IntRect` of cells):
  Fine..Medium = 1x1 (0,0,0,0); Large = 2x2; Huge = 3x3; Gargantuan = 4x4; Colossal = 5x5.
  Starships: Raider 1x1, Frigate 1x2, Cruiser 2x4, GrandCruiser 3x6. Footprint ROTATES with `Forward`
  (passed into `GridAreaHelper.GetNodes(node, sizeRect, direction)`).
- **Occupancy = effectively one solid unit per tile.** `WarhammerBlockManager.CanUnitStandOnNode` rejects
  a dest if any footprint cell is `NodeContainsAny` (blocked) & not self-occupied. Manager tracks blockers
  per node in 3 buckets `Enemies/Friends/Invisible` (+ "soft units") for transient overlap/invisibles.
  `MechanicEntity.SizeRect` + `GetOccupiedNodes()` / `WarhammerSingleNodeBlocker`.
- **Interactables are NOT slotted one-per-tile.** Doors/loot/decor are free world-space `MapObjectView`+
  `InteractionPart` objects; grid only knows them as walkability (solid prop → `StaticWalkable=false`;
  floor loot blocks nothing). Multiple interactables can overlap a tile. That's why [[rt-tile-explorer]]
  picks nearest by distance, not by tile contents — there's no per-tile object index.

NOT tiled: local map/minimap (2D overlay), sector/warp map (points+routes). Space combat reuses this grid
(rectangular ship footprints above). LOS/cover model = `LosCalculations.GetWarhammerLos` (for combat work).
See [[rt-a11y-architecture-verdict]] (combat = bespoke spatial navigator over command/targeting API).

**CORRECTION — exploration is REAL-TIME with CONTINUOUS positions, NOT tile-based (verified in-harness 2026-07-01,
save VoidshipOfficersDeck, out of combat).** `combat=False tb=False mode=Default` — `TurnController.TurnBasedModeActive`
is FALSE out of combat; turn-based + cell-quantized movement is COMBAT-only. The grid above IS the pathfinding /
occupancy / combat substrate in exploration too (a unit's `CurrentUnwalkableNode.Graph` is `CustomGridGraph`,
queryable for walkability/cover/occupant), BUT `UnitCommandsRunner.MoveSelectedUnitsToPoint(Vector3)` walks the
party to an ARBITRARY world point: commanded to a deliberately off-centre point, the unit stopped **0.61 m off the
nearest cell centre** (offset −0.36 E / +0.49 N) — it does NOT snap to tiles. So OCCUPANCY/blocking is
cell-discretised (footprint of `CurrentUnwalkableNode`) while the actual world POSITION is continuous. (A unit can
read offset 0 simply because its saved spawn happens to be cell-centred — a single sample proves nothing; the move
test is decisive.) Implication: the tile cursor ([[rt-tile-explorer]]) is a
grid-SCAN OVERLAY (cover/walkability/occupant probe + scanner measure-origin + combat positioning), NOT how
exploration movement works; the continuous tools (scanner by metres+bearing, LandmarkNav markers, move-to-a-point)
are the real exploration traversal.
