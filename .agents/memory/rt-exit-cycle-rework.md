---
name: rt-exit-cycle-rework
description: "V exit-cycle in WA shape — doors+transitions merged with bare openings, selection-integrated, distance-sorted, no cursor auto-plant"
metadata: 
  node_type: memory
  type: project
  originSessionId: 655dd0ad-b9a9-4fde-8dec-76538512c16b
---

Reworked `Scanner.CycleExit` (the V exit-cycle) to WA shape after a diff showed RT's was structurally weaker. Old
shape: private `_exitIndex` walk over geometric `room.Exits` only, arbitrary dictionary order, `MapCursor.Set` on
EVERY press (silently hijacked `ScanFrom` for all later scanner ops), doors absent entirely (closed doors cut the grid
at build time → no Exit record ever — map builds once per area part).

New shape (mirrors WA `DoCycleRoomExits`, Scanner.cs):
- Merges visible door/transition WorldModel items in-or-adjacent to the room (±1.5 m cardinal probes — closed-door
  cells may resolve to no room) with bare `ProxyRoomExit` openings NOT covered by such an item within 2.5 m
  (`ExitCoverSq`). A doorway with a door cycles as the DOOR (namable, openable via I, travelable).
- Drives the shared selection (`_selectedKey = item.Key`) — O/I/Home/Slash all work on the V target; NO auto-plant.
  `ResolveSelected` has a `RoomMap.Exit` branch (liveness via `RoomMap.ContainsExit`).
- Room from `ScanFrom()` (cursor when planted); distance-sorted each press; continuation matches openings BY POSITION
  (two sides of one threshold are distinct Exit objects at the same point).
- Door/transition line appends "leads to {room}" via `ExitDestination` (covered opening else probe), fog-gated to
  "leads to unexplored room" ([[rt-visual-parity]]).
- `RoomMap.Exit` now keeps `Boundary` (threshold cell midpoints); `ProxyRoomExit` reads bearing to the nearest part
  via `ScanBounds.Cloud` (wide openings read by near edge).

Status: in-game verified by user (2026-07-12), committed 56777e0 (with [[rt-unexplored-frontier]]). Tuning watch
stays open: `InOrAdjacentTo` ±1.5 m on top of RoomAt's 2-cell ring is loose on RT's 1.35 m cells — doors from just
beyond a thin wall may leak into the cycle.
