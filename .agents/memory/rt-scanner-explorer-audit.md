---
name: rt-scanner-explorer-audit
description: "WA→RT scanner/explorer parity audit (RT at parity+) + the FogProbe fog-read engine facts (FogOfWarArea.FogOfWarMapRT, 1x1 ReadPixels, red=visible/green=explored)"
metadata: 
  node_type: memory
  type: project
  originSessionId: a9cba596-a20b-48c6-9a57-8eb6599d6732
---

Multi-agent WA (WrathAccess/Pathfinder) → RT scanner+explorer comparison: 137 capabilities classified,
adversarially verified against RT source. Verdict: RT's in-area, single-level, real-time scanner+explorer is at
WA **parity or ahead** (persistent WorldModel identity, always-announce stepping, state-aware nodes, dedicated `Z`
zone cycle). Targeting has no gaps.

**High-value gaps** are all tracked in [[rt-map-viewer-port-plan]] `echoing-charting-lovelace.md`, 4 buckets:
(a) spatial audio (Sonar/PlayReview→G, WallTones→H; see [[rt-audio-pass]]), (b) room orientation
(RoomMap/RoomAt/room-change→J), (c) whole void-sector world map unported (location list/enter panel/travel-resume→J),
(d) curated AreaDetails art layer→J. **N/A:** rest-camp targeting, vertical/multi-floor (planar grid), Wwise listener,
continuous-glide (deferred post-audio, not dropped).

**6 medium/low gaps (Appendix B in echoing-charting-lovelace.md):**
- B1 map-object loot subtypes + trapped/locked word (loot-subtype word + "trapped"/"locked" in `ProxyMapObject.Detail`).
- B2 map-object footprint/nearest-edge: `ProxyMapObject.Footprint` uses the collider NEAREST `Position`, guarded
  (reject if >1.5 m off → point; clamp radius ≤2.75 m). The trap it fixes: most objects have one body collider on
  Position, but Warp_Obj's tightest collider was 12 m off → "reads here across the room".
- B3 TB acting-unit scan origin: `Scanner.Anchor` → TB acting unit when `IsDirectlyControllable` (was keyed on
  selection, not `TurnController.CurrentUnit`).
- B4 where-am-I indoors + fog/unexplored branch: "indoors" via reflected `BlueprintAreaPart.m_IndoorType`; fog branch
  via FogProbe (below).
- B5 world-tracking moving-source spatial voices (H, optional).
- B6 navmesh-probe unification: `Exploration/NavmeshProbe.cs` (NodeAt/Neighbour/OnMesh) adopted by
  MapCursor/TileExplorer/Geo, behaviour-identical; `WallTones.cs:127` left inline (audio session's Phase H file — don't touch).

**FogProbe — the fog-read engine facts (`Exploration/FogProbe.cs`, high value):**
- Reads ONE texel of the LIVE, **public** `FogOfWarArea.Active.FogOfWarMapRT` RenderTexture (`R8G8B8A8_UNorm`) with a
  **synchronous 1×1 `ReadPixels`**; world-XZ→texel via `area.GetWorldBounds()` (≡ shader `_FogOfWarMask_ST`, no
  reflection, no TextureDensity math); threshold 128 on the linear read: **red=currently-visible, green=ever-explored,
  neither=never-seen**. Verified in-game: NO Y-flip, `ReadPixels` works on the `RTHandle`, explored green round-trips
  through save/load.
- `Scanner.WhereAmI` appends "unexplored" for the oriented tile (planted `MapCursor` else anchor-live pos; the party is
  never fogged, so it only fires for a planted cursor).
- `FogOfWarArea` lives in **`Owlcat.Runtime.Visual.dll`** (ns `Owlcat.Runtime.Visual.FogOfWar`) — added to justfile `support`.
- **Wrong path to avoid** (an intermediate sketch): `WarhammerLocalMapRenderer.m_LocalMapTexture` / async `RequestData()`
  / `FogOfWarBounds`+`LocalMapFowScaleOffset` — `RequestData` reads the whole ≤2048² RT as `R8G8B8_SRGB` (threshold
  ~188) and only runs at save/area-transition — wrong for a per-keypress query.
- **Green=explored is HIGH-confidence in C#** (coop `FogOfWarAreaCompressor` transfers green-only;
  `RestoreFogOfWarMask`+history-copy persist green across save/load); the final blend is compiled HLSL (verified empirically).
- Fallbacks (documented, unused): Blit-target-texel-to-scratch, or a WrathAccess `FogExplored` accumulation grid;
  threshold+`FlipRow` behind named consts.
- There is **NO CPU per-cell reveal grid** anywhere (the culling system writes per-ENTITY visibility only).
- Even the ship officers' deck has an active `FogOfWarArea` (`FOWDefault`) — "ships have no fog" is not universal; the
  `NoFow` guard covers areas that DO disable it.
- Sector/world-map exploration is a clean CPU bool (`IsExplored` per planet/POI → Phase J); per-entity
  `Entity.IsInFogOfWar` already used.
