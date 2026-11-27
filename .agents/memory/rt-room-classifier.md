---
name: rt-room-classifier
description: "RT engine has NO room notion; rooms come from a mod-owned segmenter over CustomGridGraph. Passability MUST come from the engine's own per-edge connection bit (HasConnectionInDirection), NOT from Walkable+HasFenceWithNode; and on a 1.35 m raster clearance depth cannot decide where a room ends — opening NARROWNESS does"
metadata:
  node_type: memory
  type: project
  originSessionId: 5efc1140-abd4-41af-9f78-1885eeb05307
  modified: 2026-07-24T23:18:40.182Z
---

**Engine has NO room concept.** `StaticRoom`/`StaticRoomsController`/… are empty editor-time batching scaffolding
with zero runtime consumers. `BlueprintAreaPart` is far coarser (already spoken by `Scanner.WhereAmI`);
`ScriptZone` is unnamed/sparse; `node.Area` merges rooms across doorways. So rooms are mod-invented, ported from
WrathAccess's persistence-watershed `RoomMap` (`wotr-access/src/Exploration/RoomMap.cs`).

Split across two files: **`RoomSegmenter.cs` is BCL-pure** (clearance transform, watershed, merges, boundary scan)
and compiles by link into `tests/RTAccess.Tests.csproj` — keep it Unity/Kingmaker-free or the test project stops
building. `RoomMap.cs` keeps the engine side (grid read, classification, exits, fog, speech, lifecycle).

## The two laws (both were violated by the original port; fixed 2026-07-25)

**1. Passability comes from the ENGINE's per-edge connection bit, never from geometry.**
`CustomGridNodeBase.HasConnectionInDirection(dir)` is a single gridFlags bit written by
`CustomGridGraph.CalculateConnections` → `IsValidConnection`, which already folds in walkability on BOTH sides,
`IsConnectionCut` (**any** fence, cardinal *and* diagonal), the graph's `maxClimb`, and the no-corner-cutting rule
(a diagonal needs both flanking cardinals). Read it for all 8 directions per cell; no neighbour lookups needed.
- **`HasFenceWithNode` is a TRAP**: it returns `fenceHeight > position.y` — only cuts *above the querying cell's
  floor*, one-sided. It is a cover-height query, not a wall query. `IsConnectionCut` is "is there any cut".
- Engine direction ids: **S=0 E=1 N=2 W=3 SE=4 NE=5 NW=6 SW=7** (offsets transcribed in `RoomSegmenter.EngineDx/Dz`
  from `CustomGridGraph.cs:812-827`; the mapping from our own order is `EngineDir` and is unit-tested).
- `GraphNode.Walkable` = `StaticWalkable && DynamicBlocked == 0`. Units do NOT set it (they use the separate
  `WarhammerBlockManager` occupancy map), but `GraphUpdateObject` does.
- Corollary: `WallTones.cs` already used `GetNeighbourAlongDirection(checkConnectivity:true)`. Any two subsystems
  disagreeing about where walls are is a bug in the one not asking the engine.

**2. On a 1.35 m raster, clearance DEPTH cannot decide where a room ends — opening NARROWNESS does.**
Clearance = chamfer 3/4 scaled by `cell/3`, so the quantum is a third of a cell and one step off a ridge costs a
whole cell. A doorway gap is exactly one cell deep, so **no** persistence threshold both keeps a crated hall whole
(needs high) and separates a 4 m cabin from its corridor (needs low) — verified by sweep. WrathAccess got away
with a metric 0.7 m because its raster was **0.25 m** cells (`wotr .../RoomMap.cs:30`); copying that constant onto
1.35 m cells made it inert, which is why one crate split its room and the seam was announced as a doorway.
So: `PersistCells = 1` deliberately **over-proposes**, then `MergeWideOpenings` takes back any pair joined by
something wider than a doorway — `MaxOpeningCells = 3` absolute, plus `MaxOpeningFraction = 0.6` of the narrower
region's own width (2·peak−1 cells) to catch a 2-cell pinch in a 3-cell corridor. Widest **contiguous run**, so two
doors stay two doors.

## Other hard-won details
- **The watershed must flood level-by-level, breadth-first from the ground above** — not sweep in index order.
  Whole rooms are one flat clearance plateau at this raster; an index sweep let one room reach through a corner
  doorway and claim its neighbour's entire edge row, putting the "boundary" somewhere arbitrary inside it.
- **Furniture islands must BRIDGE, not just skip casting a shadow.** An enclosed crate's cells are still
  unwalkable and still sever the clearance ridge. But "enclosed by floor" is NOT proof of walk-around — a prop
  against a railing or astride an over-climb step has floor on both sides of a barrier. Bridging is granted only
  when the island's whole walkable ring is ONE connected component (`Components()`).
- No hardcoded height gate: `maxClimb` is already in the connection bit. (The old `DyGate = 0.6 m` both split
  rooms across a walkable dais lip and suppressed the exit over it.) `RoomMap.LevelGap` is a separate query-point
  guard and stays.
- Rebuild on **`GraphParamsMechanicsCache.GraphVersionIndex`** change (throttled), not only area-part change —
  otherwise a door opening after the build never becomes an exit. Room ids are centroid-sort positions, so a
  rebuild can renumber; the announce latch is re-taken silently within a short window.
- `CutFloorCells` is deliberately inert here: minimum walkable clearance is exactly one cell, and raising the floor
  enough to bite would dissolve real 4 m cabins into their corridor.
- Probes that must straddle a closed door (whose cells can be cut from the grid) seed from a raw cell square and
  only then expand along connectivity — `RoomMap.RoomsNear(seedRadius, steps)`. Seed radius is exactly the wall
  thickness at which a neighbouring room starts leaking in: 1 for "is this door mine", 2 for "where does it lead".

Related: [[rt-cover-los]], [[rt-world-grid]], [[rt-tile-explorer]], [[rt-exit-cycle-rework]],
[[rt-scanner-explorer-audit]], [[rt-unexplored-frontier]], [[rt-visual-parity]].
