---
name: rt-map-frame-listener
description: "RT spatial frame facts — camera rig looks BACK (screen-up = rigYaw-180), map north = world yaw LocalMapRotationDeg (NOT WA's LMR-180); MapFrame + ListenerAnchor fix the tester's \"left/right reversed\""
metadata:
  node_type: memory
  type: project
  originSessionId: c40b9dac-e95d-4bf6-b29e-ff1042404481
  modified: 2026-10-18T17:33:16.620Z
---

Tester report 2026-10-18 "left and right completely reversed in the whole mod". No sign bug in the mod's own
math — the mod was world-fixed (+X = east = right) while the GAME's 3D audio follows the camera, whose yaw is
per area (`BlueprintAreaPart.DefaultCameraRotation`, Q/E, reset = rig 90°). Saves showed rig yaws 0/90/180/270/
arbitrary. Fixed by porting WrathAccess's MapFrame + ListenerAnchor/ListenerSourcePatch (listener facing /
head-rotation NOT ported — RT's Q/E are the game's camera rotate keys).

**Live-measured facts (VoidshipOfficersDeck, Navigator part):**
- Camera rig is mounted looking BACK: rig yaw 78.75 → screen-up = world yaw -101.25 (= rigYaw - 180). So at rig
  yaw 0 screen-right = world -X: exactly mirrored vs a world-fixed mod frame.
- Game Wwise listener (`DefaultListener`, snapped by `AudioListenerPositionController.LateUpdate`) faces the
  camera: its right ear = screen-right.
- **Map north = world yaw `CurrentlyLoadedAreaPart.LocalMapRotationDeg`** (enum 0/90/180/270; map texture is
  world-aligned, the view rotates it CCW by LMR). Verified with marker screen positions at LMR 0 AND a temporary
  in-memory LMR 90. This is NOT WA's `LocalMapRotation - 180` (WotR's map renders flipped) — don't copy that.
- LMR is per AREA PART, not per area. Off-surface (space) the frame is world (offset 0).

**Where it lives:** `Exploration/MapFrame.cs` (world↔map conversion, `WorldGridDir` for grid edges), applied at
the edges: `Geo.CompassSector`/`RegionWord`, `InteractableDescriber` tile offset/AxisOffset/cover sides,
`CoverModel`, `Spatializer.Cue`/`Pan` (callers pass WORLD deltas), Sonar sort, WallTones trace dirs, TileExplorer
`StepTile`, CursorGlide, LocalMapCursor. `Audio/ListenerAnchor.cs` = Harmony prefix on the controller's LateUpdate:
ears at `MapCursor.ListenPosition` + `audio.listener_height` (default 3 m), facing map north; setting
`audio.listener` cursor/party/camera (camera = vanilla); off in cutscene/dialog/game over/space.

**Why:** one spatial frame for spoken bearings, keys, mod pans and the game's own sounds, matching the game's map.
**How to apply:** any new direction word, pan or arrow mapping must go through MapFrame (pass world deltas to
Geo/Spatializer; rotate key vectors with `MapFrame.ToWorld`). Supersedes [[rt-map-viewer-port-plan]] item 2
("ListenerAnchor is a no-op"). Related: [[rt-audio-pass]], [[rt-tile-explorer]], [[wotr-access-prior-art]].
