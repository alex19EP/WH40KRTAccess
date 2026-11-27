---
name: rt-visual-parity
description: "Design law — never reveal to blind players what a sighted player can't see (esp. unexplored map layout); gate spatial readouts on fog"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: a9cba596-a20b-48c6-9a57-8eb6599d6732
---

The mod's guiding principle is **visual parity**: a blind player should hear only what a sighted player *perceives*, never MORE. Confirmed explicitly by the maintainer (2026-07-02) for the unexplored-map case.

**Why:** RT loads the entire level at area-load — navmesh (corridors/rooms as walkable geometry), walls, and even placed entities all exist in memory from the start; **fog is only a reveal overlay, not progressive generation** (verified live: walkable navmesh nodes and fog-flagged entities exist in never-seen areas). So the mod *technically can* read the whole unexplored layout — but doing so is X-ray vision a sighted player lacks: it kills exploration/ambush/discovery and breaks fairness. Aim for parity, not advantage.

**How to apply:** gate any spatial/layout/occupant readout on fog state via `FogProbe.Classify(worldPos)` (see [[rt-scanner-explorer-audit]]), mirroring the sighted local map's three tiers — **NeverSeen** → nothing but "unexplored"; **Explored** (seen before, not currently visible) → static layout (walls/doors/containers) but NOT live creatures; **Visible** (or fog-off/off-map) → full detail. The player's own cursor position is never fog-hidden (they drive it), so offset/bearing is always fine to speak. The scanner already gates entities on `CurrentlySeen`; `InteractableDescriber.DescribeTile` (the tile explorer readout) now gates too. If ease-over-parity is ever wanted, expose an OPTIONAL X-ray toggle, default OFF — never make X-ray the default.
