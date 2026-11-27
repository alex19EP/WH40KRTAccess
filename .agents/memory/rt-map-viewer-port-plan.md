---
name: rt-map-viewer-port-plan
description: "WrathAccess exploration \"map viewer\" port to RT (cursor + overlays + scanner + soundscape) — 3 verified engine corrections + RT-specific API gotchas"
metadata: 
  node_type: memory
  type: project
  originSessionId: 3974a527-6ebb-4ad1-91d1-b6d843a5a78c
  modified: 2026-10-18T17:33:24.598Z
---

Port of the WotR-access exploration "map viewer" (movement/audio-camera cursor + spatial-audio overlay
stack + categorized scanner + act-on-map targeting) to RT. Full plan: `docs/plans/echoing-charting-lovelace.md`.
Supersedes steal-report §4–5; builds on [[rt-tile-explorer]] + [[ui-paradigm-pivot]].

**Status:** Phases A–D done (MapCursor spine + Geo + ModSettings; self-driven scan parity + Inspect-from-cursor;
footprint-aware ScanBounds + ProxyAreaEffect Hazards/BuffZones + persistent `WorldModel.Tick` diff registry +
party-visibility fix; ability targeting from cursor/scanner via `ClickWithSelectedAbilityHandler.OnClick` +
`PathInfo.Preview` path-aware move preview). **Phase E (overlay framework) intentionally NOT built** — reading
WA source showed it is composition/play-mode scaffolding whose only non-audio consumer is PathInfo (already shipped
as `Exploration/PathInfo.cs`); build the framework WITH its real audio consumers (F–I) when the audio-tuning pass
begins. Do NOT re-attempt Phase E without a fresh maintainer go-ahead. E + F–I deferred to the audio-tuning pass.

Two RT-native deviations from the plan (§7): PathInfo reachability reads the game's OWN cached
`UnitMovableAreaController.CurrentUnitMovableArea` (no `HandleSetUnitMovableArea` subscription, no MP-formula
duplication); targeting reuses the game's `OnClick` verbatim.

**Three verified engine corrections (non-obvious, cost real rework if forgotten):**
1. **No positional fog API.** `FogOfWarController.IsInFogOfWar(Vector3)` does NOT exist — no `FogOfWarController`
   class at all. Fog is entity-scoped only (`Entity.IsInFogOfWar`, argument-less, `Entity.cs:253`). Fog cue must
   track entities entering/leaving fog, not the cursor tile. (Texture fallback: `FogOfWarControllerData.GetFogOfWarFeature()`.)
2. **ListenerAnchor is a no-op for the mod's audio.** RT's `AudioMixer`/`Earcons` are NAudio with pan computed in
   code from MapCursor+fixed-north; they never route through Wwise's `DefaultListener`. Relocating it changes only
   the GAME's own 3D audio, not the soundscape. Optional Phase J (standalone `[DefaultExecutionOrder(10000)]`
   LateUpdate). Single-listener is guaranteed (`DefaultListener.OnEnabled` destroys others).
   **SHIPPED 2026-10-18** (as a Harmony prefix, not a LateUpdate): the camera-relative game audio was the
   tester's "left/right reversed" — see [[rt-map-frame-listener]].
3. **Persistent `WorldModel.Tick()` diff is a hard prerequisite for cues** (not "later only if looping"): ObjectCue
   detects enter/exit by cross-frame proxy reference equality, so per-call `Snapshot()` makes every frame look like
   a transition. Sonar is a staggered one-shot re-snapshot sweep (no held voice). Build the diff in Phase C.

**RT-specific API gotchas baked into the plan:**
- `ModSettings.Initialize` is defined but never called in `Main.Load` — wire it (global prereq for any persisted toggle).
- Wall/sonar walls rebuilt on grid via `GetNeighbourAlongDirection` + `!Walkable` (NOT `IsConnectionCutAlongDirection`
  — that's the cover/fence signal; reuse `LosCalculations.GetCellCoverStatus` for cover, don't re-derive 1500/500 thresholds).
- Path cost: `FindPathRT*` is async/callback-only (no sync cost API) — cache the push-only
  `IUnitMovableAreaHandler.HandleSetUnitMovableArea` set for reachability/MP.
- Footprint distance: `EntityHelper.DistanceToInCells(Vector3, IntRect)`, not the delta-based `WarhammerGeometryUtils` overload.
- AP = `ActionPointsYellow` (int), MP = `ActionPointsBlue` (float).
- Drop: ElevationSystem/SpatialSystem (planar maps), ToneEngine (no consumer).
- ContinuousGlide is DEFERRED-not-dropped: RT *exploration* is real-time CONTINUOUS (the 1.35 m `CustomGridGraph` is
  only the walkability substrate, NOT the movement model), but WA's glide is audio-only (silent on move; feedback =
  sonar/wall tones), so it's mute until the Phase F–H soundscape ships — reconsider then as an audio-driven mode
  (`node.Walkable` gives a `TraceAlongNavmesh` analog). Tile-stepping stays the speech-first default.
- Never use `SurfaceMainInputLayer` (engine-dead in mouse mode, [[rt-mouse-mode-engine-gate]]).

**Deferred phase roadmap:** E overlay framework → F audio engine fill (parallel after C) → G fog+objectcue+sonar →
H walltones → I logsystem (reconcile vs existing bark reader first) → J (RoomMap, star-map, Wwise, positional speech,
AreaDetails, CameraControls). All audio ships gated OFF for maintainer ear-tuning.
