---
name: rt-unexplored-frontier
description: "WA FrontierModel port — \"Unexplored space\" scanner category (fog-edge blobs) + per-room % unexplored in RoomMap.Describe"
metadata: 
  node_type: memory
  type: project
  originSessionId: 655dd0ad-b9a9-4fde-8dec-76538512c16b
---

Port of WrathAccess's unexplored-frontier feature, as a **scanner category only** (no dedicated hotkeys — rides
Ctrl+PageUp/Down + PageUp/Down).

- `Exploration/FogExplored.cs` — on-demand 256² blit+readback snapshot of the fog RT's explored channel (2 s TTL,
  `Ensure()` before bursts; never per-frame — RT perf discipline, unlike WA's 0.5 s tick). Linear scratch RT keeps
  [[rt-fog-cue]]'s FogProbe-verified threshold 128.
- `Exploration/FrontierModel.cs` — walkable+unexplored+adjacent-to-explored cells flood-filled into blobs;
  **MinCells = 2** (not WA's 3 — RT's 1.35 m cells are coarser; 3 would eat single-door openings). Blob identity
  survives recomputes (AddBlob overlap matching) so the selection holds.
- `Exploration/ProxyFrontier.cs` — ScanItem keyed on the Blob; room named only when the room centroid is not
  NeverSeen (CycleExit precedent, [[rt-visual-parity]]).
- Scanner: `Marker` bool → `Source {Registry, Markers, Frontier}` enum; "taxonomy.unexplored" is the LAST category;
  `FrontierList` sources it (NOT WorldModel — like MarkerList); ResolveSelected has a Blob branch. Sonar untouched
  (reads WorldModel only).
- RoomMap ([[rt-room-classifier]]): now stores `_wx/_wz/_cell`, exposes `TryGetGrid`/`CellCenter`/`CellSize`;
  `Describe` appends "{pct}% unexplored" (rounded to tens, silent at 0, bare word at 100) — so WhereAmI (X), exit
  cycle (V), and room-change announces all gained it; lifecycle: `DropDerived()` clears wx/wz + pct cache +
  `FrontierModel.Invalidate()` on every grid drop.

Status: in-game verified by user (2026-07-12), committed 56777e0 (together with [[rt-exit-cycle-rework]]; only
the mod's own files staged — another session's inventory/cargo edits shared ui.json and stayed uncommitted).
