# RT Map Viewer — Porting Plan (WrathAccess exploration/audio subsystem → RTAccess)

Plan name suggestion: `docs/plans/echoing-charting-lovelace.md` (sibling to `abundant-mixing-diffie.md`). This plan supersedes the "Exploration core" / "Spatial overlays & cues" rows of `docs/wotr-access-steal-report.md` §4–5 and builds on the shipped TileExplorer (`abundant-mixing-diffie.md`) and the mouse-mode pivot (`mirrored-surfacing-engelbart.md`).

---

## 0. Executive summary

This plan ports WrathAccess's (WA) "feel the room" exploration layer onto RT's square-grid, turn-based engine: a single **shared world cursor** that the scanner, move-to orders, and spatial cues all agree on; a near-verbatim **overlay framework**; an upgraded **categorized scanner** (two-level taxonomy, footprint-aware distance, typed verbosity, live AoE/hazard zones); the **act-on-the-map** half WA shipped that the draft missed (**targeting-from-cursor**, inspect, path/MP readout); and an **ambient soundscape** (sonar, wall-proximity tones, fog and object cues) riding NAudio. Three load-bearing corrections from verification reshape the audio work: the fog-cue API (`FogOfWarController.IsInFogOfWar(Vector3)`) **does not exist** (fog is entity-scoped only), the **ListenerAnchor is a category error** (the mod's NAudio panning is computed in code and does not route through Wwise's `DefaultListener`, so it is decoupled from the soundscape and demoted to an optional/deferred feature), and the **persistent `WorldModel.Tick()` diff is a hard prerequisite** for the cues (not a "later, only if looping" option, because ObjectCue relies on cross-frame proxy identity). Speech-only systems (scanner, targeting, path-info, log) are the high-value early wins and are fully harness-verifiable; every *audio* system ships gated OFF for a maintainer ear-tuning pass, since pan/pitch/mix quality is un-self-verifiable.

## 0.1 Phase overview

| Phase | Deliverable | Effort | Depends-on | Harness verify |
|---|---|---|---|---|
| **A** | `ModSettings.Initialize` wiring + shared `MapCursor` spine (two input slots) + Geo helpers + "where am I" | S | — | `/eval MapCursor.Position`; `/speech` where-am-I (area + region + indoor); cursor/scanner read same point |
| **B** | Re-gate to `ExplorationActive`; self-driven interactable scan parity; Inspect-from-cursor | S–M | A | `/input` fires scan cycle; `/speech` nearby objects; Inspect window opens for cursor unit |
| **C** | Scanner upgrade: persistent `WorldModel.Tick()`, taxonomy tree, `ScanBounds`, typed announce pipeline, `ProxyAreaEffect`, `ScanSounds` settings layer | M–L | A, B | `/input` two-level browse; `/eval` flips a verbosity leaf → spoken line changes; AoE/footprint bearing correct |
| **D** | Targeting-from-cursor + PathInfo/MP readout (both speech-only, RT-native) | M | A, C | `/eval` arm ability → commit at cursor → assert cast issued / refusal spoken; `/speech` "Path, N tiles / out of MP" |
| **E** | Overlay framework + `OverlayManager` + settings registry + tile-grid cursor wrapper | M | A, C | `/input` cycle overlays over `/speech`; `/eval OverlayManager.Active`; no-op test system composes |
| **F** | Audio engine capability fill (WAV cache, `PlayOneShot`, resident looping voice) | M | — (parallel after C) | `/eval` panned one-shot hard-L/R; sustained looping voice starts/stops |
| **G** | Fog cue (entity-scoped) + ObjectCue + Sonar (+ `ScanSounds.Resolve`) | M | C, E, F | `/eval` steps cursor, inspect computed pan/vol numbers; cue enter/exit fires once per transition |
| **H** | WallTones (grid raycast, looping wav voices) | M | E, F | `/eval` walks cursor toward known wall, per-direction distance shrinks |
| **I** | LogSystem (granular combat-log narration) — reconcile with existing bark reader first | M | E | `/eval` emits a log line → spoken with per-type toggle honored; no double-speech vs barks |
| **J** | Deferred: RoomMap, world/void star-map, Wwise backend, positional speech, AreaDetails, CameraControls, optional Wwise-listener relocation | — | A–I shipped | re-scope after maintainer audio tuning |

All of **F/G/H/I audio** ships `Enabled=false` / mode `Off` by default pending a maintainer tuning pass.

---

## 1. Goal & scope

**What "map viewer" means here.** A blind player's substitute for *looking at the tactical map*, plus the ability to *act on it*. Today RT gives a blind player a tile-by-tile cursor that speaks one tile per keypress (`TileExplorer`, Ctrl+T + arrows) and a categorized review browser (`Exploration/Scanner`, PageUp/Dn). Missing are (a) the **spatial, ambient "feel the room"** layer (a shared world cursor an audio frame follows; always-on sonar pings, cardinal wall-proximity tones, fog/object cues; a richer footprint-aware scanner with typed verbosity) and (b) the **act-on-the-map** layer (aim an ability at the cursor/scanner item and commit; inspect; ask "can I reach that tile this turn").

**Blind-player experience target:**
- Move one cursor around the map and *hear* where walls, doorways, cover, units, loot, and hazard/buff zones are — not just read one tile per keypress.
- Keep a stable compass frame ("north is always +Z") so a ping panned hard-left always means west, regardless of camera.
- Ask "where am I / what's around me / can I reach that tile this turn" and get a fast spoken answer.
- **Aim an action-bar ability at the cursor or scanner item and commit it**, reusing the game's own validation/refusal/cast path.
- One cursor everyone agrees on: scanner selection (for sorting), audio frame, move-to order, fog/object cues, and targeting all read the *same* point.

**IN scope:**
- A **shared movement cursor** (the spine WA has, RT lacks), built on `TileExplorer`'s grid node, with **two input slots** (Primary plain-arrows / Secondary Shift+arrows) so it survives HUD arrow-key shadowing.
- An **overlay framework** (WA's `Overlay`/`OverlaySystem`/`OverlayManager` seam) — engine-generic, ports near-verbatim.
- The **spatial-audio engine gaps** in `AudioMixer` (WAV decode/cache, looping resident-handle voices, `PlayOneShot(pos,vol,pan)`) and the systems that ride them: **Sonar**, **WallTones** (grid-rebuilt), **Fog cue** (entity-scoped — see §4), **ObjectCue**.
- **Geo helpers** (`Live`/`Bearing`/`Vertical`/`Relative`) and **where-am-I / region** readout.
- A **scanner upgrade**: full two-level `ScanTaxonomy` tree, `ScanBounds` footprint model, the typed `ScanAnnounce*` verbosity pipeline, persistent `WorldModel.Tick()` diff, **`ProxyAreaEffect`** (live AoE/hazard/buff zones), the **`ScanSounds`** per-node sonar-identity settings layer, and the **two-cursor discipline** (review selection vs shared movement cursor).
- **Targeting-from-cursor** (`Targeting`/`ITargetingMode`/`AbilityTargeting`/`RestTargeting`/`CursorTarget`) — aim + commit + cancel routed through the game's `ClickWithSelectedAbilityHandler.OnClick`.
- **Inspect-from-cursor** (game Inspect window, gated by `InspectUnitsHelper.IsInspectAllow`).
- **Turn-based path/MP info** on the cursor (RT-native; cached `IUnitMovableAreaHandler` set + AP/MP).
- **LogSystem** — granular combat-log narration (subject to reconciliation with RT's existing bark/combat-event reader).

**DEFERRED (with rationale):**
- **ListenerAnchor / Wwise-listener relocation** — **decoupled from the soundscape and demoted** (see §3.4/§4): the mod's NAudio panning is computed in code from `MapCursor` + fixed north and *never* reads the engine `DefaultListener`, so relocating it has zero effect on sonar/wall/earcon panning. Moving it only changes where the *game's own* Wwise 3D audio (ambient/footsteps/voiced barks) is heard from — an optional, potentially disorienting feature that fights `AudioListenerPositionController.LateUpdate`. Optional Phase J only.
- **ToneEngine** — **dropped** (no consumer on RT): its only WA consumers were the shelved `ElevationSystem` and the global-map glide; WA WallTones use file-based looping wav voices, not `ToneEngine`. Do not port speculatively.
- **RoomMap watershed segmentation** — XL; doorway detection at 1.35 m tile resolution is unsolved (§4). Re-evaluate after the soundscape ships.
- **World/void sector star-map** — RT's global map is a structurally different model; `Game.Instance.State.SectorMapObjects` exists but `GlobalMapSonarSystem`'s data source has no RT binding. Needs its own decompile pass.
- **Wwise backend** (`WwiseAudio`/`WwiseEngine`/`WwiseBank`) — every bank constant is reverse-engineered WOTR content; NAudio covers v1. Keep the `IAudioEngine` seam so Wwise can land later.
- **Positional/overlapping speech** (`PositionalSpeech`/`PlayPcm`) — hard-blocked: `PrismSpeech` exposes no PCM render path. Serial screen-reader queuing only.
- **AreaDetails / ProxyDetail** (curated scene-art & puzzle hints from per-area JSON) — content-driven (offline asset extraction); the only way to solve art-encoded puzzles for blind players, but needs an asset pipeline. Explicitly deferred, not dropped.
- **CameraControls** (low-vision pan/rotate/follow) — small optional add; verify `PartySelection` is covered by shipped action-bar a11y first. Deferred unless a low-vision user requests it.
- **Continuous glide cursor** — **deferred, reconsider once the Phase F–H soundscape ships** (not permanently dropped). Rationale correction: the earlier "no sub-tile value on a 1.35 m grid" framing conflated combat with exploration — RT *exploration* movement is real-time **continuous**, and the `CustomGridGraph` is only the walkability substrate beneath it, so continuity is actually an argument *for* glide, not against. The real blocker is that WA's `ContinuousGlide` is **audio-only** (`AnnouncesOnMove => false`; all feedback is sonar + wall tones), so ported today it would be **mute and useless** — the tile cursor is the only **speech-first** cursor that works before the (unbuilt, gated-off) audio layer exists. Once Phases F–H land, revisit continuous glide as an audio-driven mode; RT's per-node `Walkable` gives a direct `TraceAlongNavmesh` analog for wall-blocking. Tiled stepping stays the default regardless.
- **Elevation→pitch tone / SpatialSystem** — RT surface maps are planar; redundant with the tile cursor. Drop both.
- **Audio *quality* tuning** — un-self-verifiable; everything audio ships **gated OFF by default** for maintainer review (§4).

---

## 2. What RT already has (the reuse foundation)

**Live and solid (build on these):**
- **`Accessibility/TileExplorer.cs`** — the grid-cursor groundwork. `CustomGridNodeBase _cursor`, `Move(dx,dz)` via `(_cursor.Graph as CustomGridGraph).GetNode(x+dx,z+dz)`, anchor = `SelectionCharacter.SelectedUnit ?? Player.MainCharacterEntity`, `MoveToCursor()` with the TB/exploration move split, camera follow via `CameraRig.Instance.ScrollTo`. WA's two hardest navmesh pieces collapse into this. **Gaps:** the cursor is a *private* field (not shared); no held-repeat; no diagonals; **no Primary/Secondary input-slot split** (needed for HUD arrow coexistence).
- **`Accessibility/InteractableDescriber.cs`** — `DescribeTile(node, anchor)`, `DescribeMarker`, `DirectionAndDistance` (planar metres, +Z north/+X east map-relative compass), `ResolveName`/`Verb`, cover via `LosCalculations.GetCellCoverStatus(node, dir)` (N/E/S/W = 2/1/0/3). RT's `GridSystem` + part of `Geo`. **Reuse `GetCellCoverStatus` directly for cover — do not re-derive thresholds (§4).**
- **`Exploration/{Scanner,ScanItem,ScanTaxonomy,WorldModel,ProxyUnit,ProxyMapObject,Geo}.cs`** — a v1 of the whole scanner/world-model, but *flattened*: flat const taxonomy (no tree/`ScanClass`/sounds), per-call `WorldModel.Snapshot()` (no persistent diff), centre-only distance (no `ScanBounds`), flat-string `Detail` (no typed announce pipeline), **no shared cursor / two-cursor discipline**. `Geo.cs` has planar `Distance`/`SameArea`/`OnNavmesh`/`RegionWord`; the TB move fork (`TryCreateMoveCommandTB`) is RT-correct. `ProxyMapObject.cs:105` already uses a `GetType().Name.IndexOf("Trap")` heuristic — confirmed there is no single concrete trap part type.
- **`Audio/AudioMixer.cs`** — one `MixingSampleProvider` → one `WaveOutEvent`; constant-power-pan one-shot computed **in code** (`:69-76`). A strict subset of WA's `NAudioEngine`. NAudio 1.10.0 deployed beside `prism.dll`. **Missing:** WAV decode+cache, looping/resident mutable-handle voice, `Remove(handle)`. (No `PlayPcm` — blocked; no `ToneEngine` — dropped.)
- **`Audio/Earcons.cs`** — synthesized one-shot chimes, `Enabled=false`. Discrete-cue layer; not where sonar/walls go.
- **Grid/cover/camera backbone (all public, all already exercised in shipped code — verified):** `CustomGridGraph.GetNode(x,z)`/`width`/`depth`/`nodes`; `CustomGridNodeBase.{Walkable, Vector3Position, XCoordinateInGrid/ZCoordinateInGrid, GetNeighbourAlongDirection(int,bool), HasFenceWithNode(node,out), IsConnectionCutAlongDirection(int,out int fenceHeight)}`; `node.GetUnit()`; `LosCalculations.GetCellCoverStatus(node,dir,ForcedCoverCheckType=ByTarget) → LosDescription.CoverType`; `DestructibleEntity.FindByNode`; `EntityBoundsHelper.FindEntitiesInRange`; `GraphParamsMechanicsCache.GridCellSize (1.35f)`; `CameraRig.Instance.ScrollTo(Vector3)`; `MechanicEntity.{CurrentUnwalkableNode, GetOccupiedNodes(), SizeRect (virtual IntRect)}`; AP/MP via `unit.CombatState` (`PartUnitCombatState`) `.ActionPointsYellow (int = AP)` / `.ActionPointsBlue (float = MP/movement)`; reachable set via `IUnitMovableAreaHandler.HandleSetUnitMovableArea(List<GraphNode>)` (**push-only `ISubscriber` callback — must cache, §4**). Footprint-aware tile distance: **use `EntityHelper.DistanceToInCells(Vector3 point, IntRect targetSize, …)`**, *not* `WarhammerGeometryUtils.DistanceToInCells(Vector2Int, IntRect, IntRect)` which is delta-based (§4).
- **`Game.Instance.State.{AllBaseUnits, MapObjects, DestructibleEntities, AreaEffects, SectorMapObjects}`** — all `EntityPool<>` on `PersistentState.cs`. `AreaEffects` backs the new `ProxyAreaEffect`.
- **Framework wiring:** `Main.OnUpdate` tick board (append `*.Update()`/`*.Tick(dt)` after `Scanner.Update()`); `InputManager`/`InputBindings`/`InputCategory.Exploration`; `Screens/InGameScreen.ExplorationActive` (THE gate: `Current=="ctx.ingame" && CurrentMode==GameModeType.Default`); `Settings/*` tree + `ModSettingsRegistry`; `Tts.Speak`/`Speaker.Speak`; dev harness (`/eval /speech /gui /input /screenshot /loadsave`).
- **`ObjectRegistry<DefaultListener>` / `Kingmaker.Sound.DefaultListener` / `AudioListenerPositionController`** — exist; **single-listener is guaranteed** (`DefaultListener.OnEnabled` destroys every other instance, so even in space combat there is exactly one). Relevant only to the deferred Wwise-listener feature (§3.4).

**Dead / needs re-gating after the mouse-mode pivot:**
- **`ExplorationNav.cs`** — hard-disabled. The engine interactable ring (`SurfaceMainInputLayer.m_InteractableObjects`) is empty in mouse mode (`OnUpdate` gated `!IsControllerMouse`, `:107`; populated only under that gate, `:435`). **Do not build on `SurfaceMainInputLayer`.** Live replacement is `Exploration/Scanner` (self-driven). Stays dead; do not revive.
- **`ExplorationEvents.cs`** — chosen-interactable half (`ISurroundingInteractableObjectsCountHandler`) inert in mouse mode; area/loading half is live, keep it.
- **The gate flip:** every helper's old `ControllerMode == Gamepad` → `InGameScreen.ExplorationActive`. New code rides this gate, **never** the engine interactable layer.
- **`ModSettings.Initialize` is never called in `Main.Load`** (`Settings/ModSettings.cs:26` defines it; `Main.cs` calls `LocalizationManager.Initialize`/`ScreenManager.Initialize`/`Speaker.Initialize` but not this). **A global prerequisite for any persisted toggle — wired in Phase A.**

---

## 3. Architecture for RT

### 3.1 The shared cursor spine (the missing prerequisite)

Introduce **`Exploration/Cursor.cs`** — a tiny static backed by **both** a `CustomGridNodeBase` (grid truth) and its `Vector3` (`node.Vector3Position`):

```
static class MapCursor {
  static CustomGridNodeBase Node { get; private set; }
  static Vector3 Position => Node != null ? Node.Vector3Position : PlayerPosition;
  static bool Has;
  static void Set(CustomGridNodeBase); static void Clear();
  static Vector3 PlayerPosition => Geo.Live(Anchor());   // view transform; TB acting unit is the common path
}
```

`TileExplorer` stops owning its private `_cursor` and reads/writes `MapCursor`. Now the scanner's Home-plant, the audio frame, fog/object cues, the path-info system, targeting, and move-to-cursor all agree on one point. This is the single behavioral gap blocking everything spatial.

**Two input slots (keep WA's distinction).** Port the Primary/Secondary slot idea from `Overlays/CursorKeys.cs`/`MovementMode.cs`/`TileStep.cs`: Primary = plain arrows, Secondary = Shift+arrows. The point (per WA's `TileStep` comment) is that **when the HUD owns the plain arrows, the secondary slot keeps moving the cursor** (a shadow-immune category). On RT, where the HUD action bar grabs arrows in combat, this is exactly the coexistence problem; keep the two-slot split even though ContinuousGlide is dropped.

**Two-cursor discipline** (restore from WA): `MapCursor` is the *movement* cursor; the **scanner keeps its own review selection** that never moves the party. `ScanFrom = MapCursor.Has ? MapCursor.Position : Anchor().Position` is the sort/measure origin. Scanner `Home` plants `MapCursor` on the review selection (the only coupling). *Verification of this split lands in Phase C* (it needs the upgraded scanner to be meaningful); Phase A only proves the cursor exists and the scanner reads its position.

### 3.2 Overlay framework — mirror WA's seam (near-verbatim)

Port these seven engine-generic files into **`Exploration/Overlays/`** (dependencies all exist):

| WA file | RT file | Adaptation |
|---|---|---|
| `Overlay.cs` | `Overlays/Overlay.cs` | `Tts`→`Tts.Speak`; `ControlState` exists |
| `OverlaySystem.cs` | `Overlays/OverlaySystem.cs` | settings types exist |
| `OverlayContext.cs` | `Overlays/OverlayContext.cs` | none |
| `OverlayEnums.cs` | `Overlays/OverlayEnums.cs` | **rename the overlay `AnnouncementContext`** (collides with `UI/Announcements/AnnouncementContext.cs`); drop `WorldMap` scope (`CurrentScope` collapses to `InArea`) |
| `OverlayAnnouncement.cs` | `Overlays/OverlayAnnouncement.cs` | `Message` exists |
| `AudioSystem.cs` | `Overlays/AudioSystem.cs` | `EffectiveVolume` reads RT settings |
| `OverlayAudio.cs` | `Overlays/OverlayAudio.cs` | `Dir`→RT mod dir; assets at mod root, **not** under `Assemblies/` |

The framework treats the cursor as an opaque `Vector3`-yielding object honoring `Position`/`MovementKeysHeld`/`Tick`/`Recenter`. RT supplies a tile-grid cursor wrapper (on `MapCursor`+`TileExplorer`, exposing both input slots) implementing that contract. Composition axes: **Overlay = (a Cursor with movement) × (a set of one-per-type OverlaySystems)**. Systems sense and speak/sound; they never move the cursor. Tri-state `OverlayMode` (Off/WhenMoving/Continuous) + `ForceHeld` is the whole play-gate (`ShouldPlay`); `WhenMoving` keys off a ported **`MotionTracker.cs`** (pure, copy verbatim).

**`OverlayManager`** (adapt): gate = `InGameScreen.ExplorationActive`. Owns Ctrl+O (cycle overlay), Ctrl+F1/F2 (cycle sonar/walltones mode), Shift+F1/F2 (hold-to-force) — register through `InputManager`/`InputCategory.Exploration` for `/input` drivability and chord-shadow deconfliction. Append `OverlayManager.Tick(dt)` to `Main.OnUpdate` after `Scanner.Update()`.

**`OverlaySettingsRegistry`** (adapt pattern, rebuild content): keep shared `defaults.<key>` tunables + per-overlay `overlays.<id>` composition (`mode`+`customized`+`custom`) + whole-subtree `Customize/ResetSystem`. **Drop** both migration methods, all `worldmap_*` settings, the `wwise` engine choice (until the bus is wired). **Re-author** units to metres/tiles (1.35 m); `DefaultOn` to the RT system set (all *audio* off). **Requires** `ModSettings.Initialize` (Phase A).

### 3.3 Audio engine capability gaps (in `AudioMixer`, not a new class)

Add to **`Audio/AudioMixer.cs`** (all pure NAudio, zero game coupling):
1. **WAV decode + sample cache** — `AudioFileReader → WdlResamplingSampleProvider(44100) → MonoToStereoSampleProvider`, cached `Dictionary<string,float[]>`. Enables file-based stems (sonar/wall tone sets). Synthesized `Earcons` pings remain an asset-free fallback for v1.
2. **`PlayOneShot(stem, file, Vector3 worldPos, vol, pan)`** wrapper — computes vol/pan **in code** from the `MapCursor` frame + fixed north exactly as WA Sonar L94-95; routes to the existing `OneShot`. (This in-code computation *is* the compass-stable frame — no engine listener involved; see §3.4.)
3. **Resident, mutable-handle voice** — an `ISampleProvider` that always returns `count` (never auto-removes), with `volatile` freq/pan/gain set on the main thread, plus `Play`-returning-handle + `Remove(handle)`. This is what looping wall-tone wav voices and a held sonar voice need.

Add the **`IAudioEngine`/`IWallTones` seam** (`Audio/IAudioEngine.cs`) now, NAudio-only, with `WwiseEngine` as `Available => false`. It is the abstraction wall-tones and a future Wwise plug into.

**Do not port `ToneEngine`** — it has no consumer on RT (its WA consumers were the dropped `ElevationSystem` and global-map glide; WA WallTones build file-based looping voices via `engine.CreateWallTones(toneSet)`, not `ToneEngine`).

### 3.4 The audio frame & Geo helpers — and why ListenerAnchor is demoted

**Audio frame = in-code, no engine listener.** All spatial panning (sonar/walls/fog/earcons) is computed in code from `MapCursor.Position` + a **fixed north (+Z)**, inside `AudioMixer.PlayOneShot`/the resident voice. This is self-contained and compass-stable by construction.

**`ListenerAnchor` is demoted to optional/deferred (category error in the draft).** The mod's NAudio output does **not** route through Wwise's `DefaultListener`; relocating `DefaultListener.transform` has **zero effect** on the mod's panning. It only changes where the *game's own* Wwise 3D audio (ambient/footsteps/voiced barks) is heard from — a distinct, optional, potentially disorienting feature that fights `AudioListenerPositionController.LateUpdate` (`:23-26`, re-snaps every frame). The single-listener assumption is safe (`DefaultListener.OnEnabled` destroys others; resolve via `ObjectRegistry<DefaultListener>.Instance.MaybeSingle`, exactly as `AudioListenerPositionController.cs:14` does). If the maintainer later wants the game's own 3D audio to follow the cursor, it ships as a standalone Phase J `[DefaultExecutionOrder(10000)]` LateUpdate component — **not** a soundscape prerequisite.

**`Exploration/Geo.cs`** (extend existing): add `Live(EntityDataBase)` (read `e.View.transform.position`, not lagging `entity.Position`), `Bearing`/`Vertical`/`IsHere`, and `Relative(...)`'s measure-to-nearest-edge vs report-to-centre split. Keep planar metric `Distance` (drop all WA feet/miles). `Bearing` convention 0°=N=+Z, 90°=E=+X must match the audio frame's fixed north and `InteractableDescriber`'s compass.

### 3.5 Scanner + taxonomy upgrade

- **Persistent `WorldModel.Tick()` is now a hard prerequisite (moved up to Phase C).** The draft's "keep `Snapshot()`; port the diff only when looping sonar needs it" is wrong: (a) Sonar is a **staggered one-shot** sweep that re-snapshots every cycle and never holds a per-item voice, so "looping sonar" never arrives; (b) **ObjectCue detects enter/exit by reference equality across frames** (`if (inside != _inside)`) and idle-hover tracks `_spoken` the same way — per-call `Snapshot()` makes fresh proxies each frame, so every frame looks like enter/exit and the cue is unusable. `Tick()` is also what surfaces `ProxyAreaEffect`/`ProxyMarker`, drives `Added`/`Removed`, and is the allocation-free path. Port the persistent diff registry in Phase C and delete the "only if looping" gate.
- **`Exploration/ScanBounds.cs`** — pure XZ math, **port verbatim** (Point/Circle/Rect/Polyline/Segments/Cloud + non-alloc `NearestOnCircleXZ`/`NearestInQuadXZ`/`ClosestOnSegment`/`InConvexXZ`). Especially relevant on RT's `IntRect` multi-tile footprints. Add `Footprint`/`Bounds`/`NearestPoint(from)`/`Contains` to `ScanItem` (footprint from `GetOccupiedNodes()`/`SizeRect`).
- **`Exploration/ProxyAreaEffect.cs`** (port, new on RT) — surfaces `Game.Instance.State.AreaEffects` (spell/psychic AoEs, terrain hazards, buff zones), classified harmful/beneficial, with the **real runtime shape** (`ScriptZoneCylinder` → circle, `ScriptZoneBox` → rotated rectangle) feeding `ScanBounds` so distance/bearing report the nearest *edge* you're about to step into. A first-class `ScanItem` subclass, not just a taxonomy rename. High value in TB combat.
- **`Exploration/ScanTaxonomy.cs`** — rebuild as the full `Node` tree (`Cat`/`Sub`, `ScanClass {Unit,Object,Marker,AreaEffect}`, dotted keys, default sound stems). Drop PF hazard spell/terrain flavor; remap to 40K (psychic AoE vs environmental, cogitator/vox, servitors). RT settings infra backs it.
- **`Exploration/ScanSounds.cs`** (port, call out as its own subsystem) — the settings layer that makes the sonar *legible*: one sound-stem dropdown **per taxonomy node**, with an Inherit chain (child → parent → silent), plus the `review_sound` choice. Sonar is type-deaf without it (`SonarSystem` calls `ScanSounds.Resolve(item.Primary)` to decide whether/what to ping). Assets at `<modroot>/assets/audio/interactables/*.wav` (or synthesized fallback for v1).
- **`Exploration/Announce/{ScanAnnounceComposer,ScanAnnouncement,ScanAnnounceRegistry,ScanParts}.cs`** — typed per-part verbosity pipeline; rides RT's existing `UI/Announcements/*` + `Message` + `NullableBoolSetting`. `SpatialPart` calls `InteractableDescriber` for the compass string + `ScanBounds.NearestPoint`. Replaces the flat `ScanItem.Detail` string.
- **`Scanner.cs`** — restore the two-level taxonomy browse (skip-empty with index-0 "Everything" anchor), `ScanFrom` from `MapCursor`, Home-plant, sonar `PlayReview` ping (gated on `DetectableFrom(cursor)` LoS so remembered items don't ping through walls). Keep the RT-correct TB/exploration move fork.

### 3.6 The spatial / act-on-map systems

| System | RT file | Sensing | Output |
|---|---|---|---|
| **Sonar** | `Overlays/SonarSystem.cs` | `WorldModel.Items` within max-dist, `DetectableFrom` LoS gate; sort L→R; **staggered one-shot** sweep; pan/vol math **verbatim** (world XZ, grid-agnostic, in-code) | `AudioMixer.PlayOneShot(pos,vol,pan)` via `ScanSounds.Resolve` |
| **WallTones** | `Overlays/WallToneSystem.cs` | **rebuilt grid raycast** from cursor node along N/E/S/W via `GetNeighbourAlongDirection`; stop on `!neighbour.Walkable` (wall) or missing connection; dist = cells×1.35; cover via `LosCalculations.GetCellCoverStatus(node,dir)` directly — **do not re-derive 1500/500 thresholds (§4)** | 4 looping resident wav voices, fixed compass pan |
| **Fog cue** | `Overlays/FogSystem.cs` | **entity-scoped** (`entity.IsInFogOfWar`) — there is no `Vector3` fog query (§4). Cue the nearest tracked entity transitioning into/out of fog, not the cursor tile | 2D cue or `Earcons` |
| **ObjectCue** | `Overlays/ObjectCueSystem.cs` | footprint enter/exit (`Contains`) + idle-hover `DescribeInPlace`; **requires persistent `WorldModel.Tick()` proxy identity**; gate on `Navigation.HasFocus` | 2D cue + `Tts.Speak` |
| **PathInfo** | `Overlays/PathInfoSystem.cs` | RT-native: **cache the last `IUnitMovableAreaHandler.HandleSetUnitMovableArea` push** (it's a callback, not a pull); reachability + MP-remaining from that set; AP/MP from `unit.CombatState`; report tiles/MP not feet | speech only |
| **Targeting** | `Exploration/Targeting.cs` + `ITargetingMode.cs`/`AbilityTargeting.cs`/`RestTargeting.cs`/`CursorTarget.cs` | aim the selected action-bar ability; commit at world cursor (Enter) or scanner item (I), cancel Backspace/Escape; `CursorTarget.Inside()` resolves "what the cursor is on" from `WorldModel` + footprint `Contains` (not screen-ray) | route through the game's `ClickWithSelectedAbilityHandler.OnClick` (reuses all validation/refusal/cast) |
| **Inspect** | `Exploration/Inspect.cs` | open the game Inspect window for the cursor unit or scanner item; gate via `InspectUnitsHelper.IsInspectAllow` | game window |
| **Log** | `Overlays/LogSystem.cs` + `LogFeed.cs` | one Harmony postfix on `LogThreadBase.AddMessage`; per-type toggle taxonomy over ~50 log-thread types; `Scope=Both`, drains a feed (not cursor-driven) | `Tts.Speak`; **reconcile with RT's existing EventBus bark/combat-log reader to avoid double-speech (§6)** |

`GridSystem` ≈ RT's existing `InteractableDescriber.DescribeTile` — don't port WA's `NavmeshProbe`; reuse the fragment-toggle compose pattern only. SpatialSystem/ElevationSystem/GlobalMapSonar/ToneEngine — drop.

---

## 4. The hard RT-specific decisions

1. **Square-grid rebuild of every navmesh driver.** WA's three navmesh dependencies have no RT equivalent and are rebuilt on `CustomGridGraph`:
   - *Wall tones / sonar walls:* WA `NavmeshBase.Linecast` cardinal traces → **step cell-by-cell** via `GetNeighbourAlongDirection`, stopping on `!neighbour.Walkable` (a wall) or a missing connection. **Do not** use `IsConnectionCutAlongDirection`'s boolean as a wall test — it returns true whenever an edge *fence record exists* (`fenceHeight > int.MinValue`, `CustomGridNodeBase.cs:364-368`), which is the *cover* signal, not impassability. And **do not** compare raw `fenceHeight` to 1500/500: `LosCalculations.cs:111-118` compares `fenceHeight − node.position.y` (height above the node floor). For cover, **reuse `LosCalculations.GetCellCoverStatus(node, dir)` directly** rather than re-deriving thresholds. Cardinal indices N/E/S/W = 2/1/0/3 confirmed (matches `IsConnectionCut` map + `InteractableDescriber.AppendCover`).
   - *Path info:* WA `CombatMode.TryPathInfo` → RT `IUnitMovableAreaHandler.HandleSetUnitMovableArea`, which is **push-only** (an `ISubscriber` callback; you cannot pull it). **Subscribe and cache the last-pushed set** for reachability + MP-remaining. Every `FindPathRT*` is async (`FindPathRT`(callback)/`FindPathRT_Delayed`/`FindPathRTAsync → Task<ForcedPath>`); **no synchronous cost API exists**, so exact cost-to-one-tile cannot back a synchronous spoken readout — report reachable/MP-remaining from the cached set, and only await/cache exact cost if a precise figure is later required.
   - *Tile readout/probe:* WA `NavmeshProbe.Sample` → RT discrete `GetNode(x,z)` + `.Vector3Position.y` (already in `TileExplorer`). Footprint-aware distance via `EntityHelper.DistanceToInCells(Vector3 point, IntRect targetSize, …)`, **not** the delta-based `WarhammerGeometryUtils.DistanceToInCells`.
2. **Mouse-mode engine gate — self-driven, never the engine layer.** `SurfaceMainInputLayer.m_InteractableObjects` is empty in mouse mode. Every scan/overlay self-drives off `Game.Instance.State.{AllBaseUnits,MapObjects,DestructibleEntities,AreaEffects}` + `EntityBoundsHelper`, gated on `InGameScreen.ExplorationActive`. `ExplorationNav` stays dead.
3. **Turn-based path / MP cost.** RT is TB-only; drop WA's real-time/TB fork and `InTurnBased` gate. PathInfo reports tiles + MP (`ActionPointsBlue`) / AP (`ActionPointsYellow`), not feet/standard-action. Move-to-tile already exists (`TileExplorer.MoveToCursor`).
4. **Fog has no *synchronous gameplay-layer* positional query — but the positional data does exist (correction 2026-07-02).** There is **no** `FogOfWarController.IsInFogOfWar(Vector3)` (no `FogOfWarController` class exists; only static `FogOfWarControllerData` with no position query), and `IsInFogOfWar` is an argument-less entity property (`Entity.cs:253`) plus group wrappers. So the cheap path — and what the Fog *cue* uses — is **entity-scoped**: cue entities transitioning into/out of fog (`entity.IsInFogOfWar`), not the cursor tile. **However, the per-tile "painted" reveal mask is real, CPU-reachable data, not GPU-only-and-lost — and reads *synchronously*:** the live mask is `FogOfWarArea.Active.FogOfWarMapRT`, a **public** `RenderTexture` (`R8G8B8A8_UNorm`, in the freshly-decompiled `Owlcat.Runtime.Visual.dll`). A per-tile "is this floor explored?" test is a **1×1 `ReadPixels`**, not a blocked task and not the async whole-RT `RequestData()`: map world XZ → texel via `area.GetWorldBounds()` (≡ shader `_FogOfWarMask_ST`) and threshold **green≥128 = ever-explored, red≥128 = currently-visible** on the linear read. This is now shipped as `Exploration/FogProbe.cs` (see §B4). Authoritative (it *is* what the shader paints, so it already respects walls/LOS). NB the persisted `SavedFogMasks` `.fog` `byte[]` and `RequestData()` are the *same* mask but `R8G8B8_SRGB` (threshold ~188) and only run at save/area-transition — do not use them for per-keypress queries. This supersedes both the earlier "a per-tile fog test would not compile / B4-fog is blocked" claim **and** the intermediate `m_LocalMapTexture`/`RequestData`/`FogOfWarBounds` sketch.
5. **Planar maps — drop elevation/slope.** RT surface maps are planar square grids; `ElevationSystem`/`SpatialSystem` add nothing over the tile cursor. Drop both. `ToneEngine` has no remaining consumer — drop it too. `Vertical`/floor-above-below collapse to no-ops unless a multi-floor surface is found (open question).
6. **The audio frame is in-code; the Wwise listener is unrelated.** Spatial panning is computed in `AudioMixer` from `MapCursor` + fixed north. Relocating Wwise's `DefaultListener` does **nothing** to it (§3.4); `ListenerAnchor` is therefore not a soundscape prerequisite and is deferred to optional Phase J.
7. **Audio quality is un-self-verifiable → port + gate OFF for the maintainer's ears.** The harness can confirm a sonar *fires* (`/eval`, `/speech` for spoken parts, `/gui`) and can inspect computed pan/vol numbers, but cannot judge pitch/pan/volume/mix. **Every audio system ships `Enabled=false`/mode `Off` by default**, auditioned via `/eval`, and flagged for a maintainer tuning pass before any default-on flip. `ModSettings.Initialize` + `BoolSetting` toggles are the persisted on-switch (wired in Phase A).

---

## 5. Phased build order

Each phase is independently shippable and harness-verifiable. Effort: S/M/L. Order: cheapest-highest-value speech systems first (orientation, scanner, act-on-map), then the audio engine + soundscape.

### Phase A — Prerequisites + Geo + shared cursor spine + where-am-I (S)
**Why first:** the `ModSettings.Initialize` wiring is a global prerequisite for *every* later persisted toggle; the `MapCursor` spine unblocks everything spatial.
- **First (global prerequisite):** add `ModSettings.Initialize(settingsDir)` to `Main.Load` (+ `Reindex`) so all later persisted prefs work; confirm persist path (`persistentDataPath/RTAccess/`).
- **Add:** `Exploration/Cursor.cs` (`MapCursor`, two input slots Primary/Secondary). **Edit:** `Geo.cs` (add `Live`/`Bearing`/`Vertical`/`Relative`), `TileExplorer.cs` (back its cursor with `MapCursor`; add Secondary Shift+arrow slot), `Scanner.cs` (`ScanFrom` reads `MapCursor`).
- **Add** a "where am I" verb (area name + `RegionWord` 3×3 + indoor flag) on a free key (or extend Scanner's Home).
- **APIs:** `MechanicEntity.CurrentUnwalkableNode`, `Geo.SameArea`/`RegionWord`, `CameraRig.ScrollTo`, view-transform read, `ModSettings.Initialize`.
- **Verify:** `/eval MapCursor.Position`; `/speech` after where-am-I shows area + region + indoor; `/eval` shows cursor and scanner read the same point; a persisted pref survives `/loadsave`. (Two-cursor *discipline* verification deferred to Phase C.)

### Phase B — Re-gate + self-driven scan parity + Inspect-from-cursor (S–M)
**Why:** closes the one acknowledged exploration regression (interactable cycling dead in mouse mode) using the already-live `Scanner`; Inspect is a cheap high-value pairing.
- **Edit:** confirm `Scanner` covers the old `ExplorationNav` cycling (PageUp/Dn + categories); formally retire `ExplorationNav`'s dead body; keep `ExplorationEvents` area/loading half, chosen-interactable half off. Register exploration/scanner keys through `InputManager`/`InputCategory.Exploration` for `/input` drivability.
- **Add:** `Exploration/Inspect.cs` (open the game Inspect window for the cursor unit/scanner item; gate `InspectUnitsHelper.IsInspectAllow`).
- **APIs:** `Game.Instance.State.*`, `EntityBoundsHelper`, `InGameScreen.ExplorationActive`, `InspectUnitsHelper.IsInspectAllow`.
- **Verify:** `/input` fires scanner cycle; `/speech` reads nearby objects; Inspect window opens for the cursor unit; `/gui` HUD intact; no double-speech.

### Phase C — Scanner upgrade: persistent WorldModel + taxonomy + ScanBounds + AoE + ScanSounds + typed announce (M–L)
**Why:** high payoff, pure-logic, no audio risk; the persistent `WorldModel.Tick()` and `ScanBounds`/`ScanSounds` are hard prerequisites for the entire cue/sonar group.
- **Add:** `ScanBounds.cs` (verbatim), `ProxyAreaEffect.cs`, `ScanSounds.cs` (settings layer), `Announce/{ScanAnnounceComposer,ScanAnnouncement,ScanAnnounceRegistry,ScanParts}.cs`. **Rebuild:** `ScanTaxonomy.cs` (full tree, 40K remap, `AreaEffect` class). **Cut over:** `WorldModel.cs` to the persistent diff `Tick()` (Added/Removed, stable proxy identity). **Edit:** `ScanItem.cs` (Footprint/Bounds/NearestPoint/Contains/DetectableFrom), `Scanner.cs` (two-level browse skip-empty, Home-plant, `ScanFrom`, review ping gated on LoS), `ProxyUnit`/`ProxyMapObject` (corpse-loot many-to-many, footprint, loot-subtype split; keep the `GetType().Name` trap heuristic — no concrete trap part exists).
- **APIs:** `GetOccupiedNodes`, `SizeRect`, `EntityHelper.DistanceToInCells`, `Game.Instance.State.AreaEffects`, `ScriptZoneCylinder`/`ScriptZoneBox` shapes, `UI/Announcements/*`, `NullableBoolSetting`.
- **Verify:** `/input` two-level browse over `/speech`; `/eval` flips a verbosity leaf → spoken line changes; footprint-aware bearing reads correctly for a multi-tile unit/wide door and an AoE edge; two-cursor discipline (review selection vs `MapCursor`) holds via `/eval`.

### Phase D — Targeting-from-cursor + PathInfo (M)
**Why:** the act-on-the-map payoff of the shared cursor and arguably higher value on TB-combat RT than half the audio overlays; both are speech-only and fully harness-verifiable.
- **Add:** `Exploration/Targeting.cs` + `ITargetingMode.cs`/`AbilityTargeting.cs`/`RestTargeting.cs`/`CursorTarget.cs`; `Overlays/PathInfoSystem.cs` (subscribe + cache `HandleSetUnitMovableArea`; arm-on-cursor-stop, fire-once, threshold phrasing).
- Aim the selected action-bar ability; commit at world cursor (Enter) or scanner item (I), cancel Backspace/Escape; route through `ClickWithSelectedAbilityHandler.OnClick`. `RestTargeting` reuses the aim/commit/cancel plumbing for camp placement.
- **PathInfo (speech-only) can default ON.**
- **APIs:** `ClickWithSelectedAbilityHandler.OnClick`, `IUnitMovableAreaHandler.HandleSetUnitMovableArea` (cached), `unit.CombatState.{ActionPointsBlue,Yellow}`, `ScanItem.Contains`/`NearestPoint`.
- **Verify:** `/eval` arms an ability, commits at the cursor → assert the cast command issued (or the game's refusal is spoken); in a loaded combat save `/speech` reads "Path, N tiles / out of MP / No path" matching the cached movable-area set.

### Phase E — Overlay framework + manager + settings registry (M)
**Why:** the load-bearing seam every spatial system rides; engine-generic, near-verbatim.
- **Add:** the 7 framework files + `MotionTracker.cs` + `OverlayManager.cs` + `OverlaySettingsRegistry.cs` (content rebuilt) + the tile-grid `Cursor` wrapper (exposing both input slots) honoring the framework contract.
- **Edit:** `Main.OnUpdate` (append `OverlayManager.Tick(dt)`), `InputBindings` (Ctrl+O/F1/F2, Shift+F1/F2 via `InputManager`).
- **Verify:** `/input` cycle of overlays over `/speech` ("overlay X"); `/eval OverlayManager.Active`; a no-op speech-only test system's `Announce` composes correctly; `/gui` HUD intact.

### Phase F — Audio engine capability fill (M)
**Why:** the one primitive set blocking the whole sound group. Can run in parallel after C.
- **Edit:** `AudioMixer.cs` (WAV cache, `PlayOneShot(pos,vol,pan)` in-code frame, resident mutable-handle voice, `Remove`). **Add:** `Audio/IAudioEngine.cs` (+ stub `WwiseEngine` `Available => false`). **No `ToneEngine`. No `ListenerAnchor`** (deferred §3.4).
- **All gated OFF / for maintainer audition.**
- **APIs:** NAudio (`AudioFileReader`, `WdlResamplingSampleProvider`, `MonoToStereoSampleProvider`, `MixingSampleProvider`).
- **Verify:** `/eval` plays a panned one-shot hard-left and hard-right; `/eval` starts a sustained looping resident voice and `Remove`s it cleanly. **Maintainer:** audition pan.

### Phase G — Fog (entity-scoped) + ObjectCue + Sonar (M)
**Why:** the first real soundscape; sonar is highest-value/lowest-risk (math ports verbatim, in-code frame).
- **Add:** `Overlays/{FogSystem,ObjectCueSystem,SonarSystem}.cs`. Feed sonar from `WorldModel`/`Scanner` results via `ScanSounds.Resolve`; `PlayReview` ping on scanner landing, gated by `DetectableFrom`. Fog cues **entities** (`entity.IsInFogOfWar`), not tiles. ObjectCue relies on the persistent `WorldModel.Tick()` proxy identity from Phase C.
- **All gated OFF by default.**
- **Verify:** `/eval` enables sonar, steps the cursor; `/speech` (review parts) + `/eval` confirm pings fire for the right items at the right computed pan/vol values; ObjectCue enter/exit fires once per real transition (not every frame). **Maintainer:** confirm the soundscape is legible.

### Phase H — WallTones (grid raycast, looping wav voices) (M)
**Why:** the grid-rebuilt cardinal driver; the last core audio overlay.
- **Add:** `Overlays/WallToneSystem.cs`. Grid cardinal raycast via `GetNeighbourAlongDirection` → stop on `!neighbour.Walkable`; 4 looping resident wav voices (fixed compass pan) on the Phase-F voice; cover read via `LosCalculations.GetCellCoverStatus` directly.
- **Gated OFF by default.**
- **APIs:** `GetNeighbourAlongDirection`, `node.Walkable`, `LosCalculations.GetCellCoverStatus`, `DestructibleEntity`.
- **Verify:** `/eval` walks the cursor toward a known wall and confirms the per-direction distance shrinks and the correct cardinal voice changes. **Maintainer:** audition wall tones.

### Phase I — LogSystem (granular combat-log narration) (M)
**Why:** a major combat capability if RT's current reader only covers barks/subtitles; must be reconciled first to avoid double-speech.
- **First:** verify coverage against RT's existing EventBus bark/combat-log reader (memory: barks + combat-log-bark). If granular log threads (attack rolls, damage, saves, skill checks, etc.) are already read, scope this down or skip; if not, port it.
- **Add (if warranted):** `Overlays/LogSystem.cs` + `LogFeed.cs` — one Harmony postfix on `LogThreadBase.AddMessage`, per-type toggle taxonomy, `Scope=Both`, drains a feed (not cursor-driven).
- **Verify:** `/eval` emits a log line → spoken with the per-type toggle honored; no duplication against the existing bark path.

### Phase J (deferred, future) — RoomMap / world-map / Wwise / positional speech / AreaDetails / CameraControls / Wwise-listener
Re-scope after A–I ship and the maintainer has tuned audio. RoomMap needs a doorway-detection solution at 1.35 m (sub-cell supersample or wall-edge data); world/void star-map needs the `SectorMapObjects` decompile; Wwise needs bank-constant re-extraction; positional speech needs a `PrismSpeech` PCM render path; AreaDetails needs an offline per-area JSON/asset pipeline; CameraControls is a small low-vision add (verify `PartySelection` coverage first); the optional Wwise-listener relocation (game's own 3D audio following the cursor) ships as a standalone `[DefaultExecutionOrder(10000)]` LateUpdate component if the maintainer wants it.

---

## 6. Risks & open questions

**Needs in-game verification (via harness or maintainer):**
- **RT trap part type** — `ProxyMapObject` uses a `GetType().Name contains "Trap"` heuristic; confirmed there is no single concrete trap part to bind. Find the real armed flag in `decompiled` if a typed part is wanted.
- **Async path cost** — `FindPathRT*` is callback/`Task`-only; the cached `HandleSetUnitMovableArea` push backs the synchronous "N tiles / out of MP" readout. Confirm the push fires often enough (on selection/turn change) to stay fresh; design an await/cache if exact per-tile cost is ever needed.
- **`entity.Position` lag** — verify whether RT's logical position lags the view mid-move; if so `Geo.Live` (view transform) is needed for accurate bearings (cheap to add regardless).
- **Multi-floor surfaces** — do any RT surface maps stack floors? If not, all vertical/elevation logic stays no-op (current assumption).
- **AreaEffect shape coverage** — confirm `ScriptZoneCylinder`/`ScriptZoneBox` are the only runtime shapes backing `Game.Instance.State.AreaEffects`; handle a fallback (centre + radius) for any other zone type.
- **LogSystem overlap** — confirm exactly which log threads RT's existing bark/combat-event reader already speaks before porting (avoid double-speech).
- **Key collisions** — Ctrl+O / Ctrl+F1-F2 / Shift+F1-F2 and the Secondary Shift+arrow cursor slot vs the HUD and game binds; confirm via `/input` and the deconflicted key-ownership map (Report 7).
- **Fog feature fallback** — if entity-scoped fog proves insufficient, confirm `FogOfWarControllerData.GetFogOfWarFeature()` exposes a queryable texture before attempting a positional fog test.

**Confirmed (no further action):**
- **Single `DefaultListener`** — guaranteed (`DefaultListener.OnEnabled` destroys others); relevant only to the deferred Wwise-listener feature.
- **`ModSettings.Initialize`** is defined but never called in `Main.Load` — wired in Phase A.
- Grid/cover/AP-MP/state-pool APIs in §2 are all public and already exercised in shipped code.

**Maintainer decisions:**
- **Ship WAV assets vs synthesize?** File-stem sonar/wall tones need an `assets/audio` tree at mod root; synthesized `Earcons` pings avoid shipping assets in v1. Recommend synth-first, file stems later; `ScanSounds` Inherit-chain supports both.
- **Default-on policy** — PathInfo (speech-only) and (if ported) LogSystem are safe default-on; all *audio* systems default-off pending the tuning pass (pitch/pan/vol/mix, which overlays, which `DefaultOn`).
- **Wwise-listener feature** — decide in Phase J whether the game's own 3D audio should follow the cursor (optional, can be disorienting).
- **LogSystem scope** — port granular log narration or rely on the existing bark reader, per the reconciliation in Phase I.

---

## 7. Implementation progress

> Living tracker. Each phase is **build-verified** (`dotnet build RTAccess.slnx -c Debug` → 0/0) as it lands;
> **in-game** harness verification is batched (per the maintainer's call) and flagged below where still pending.

### Phase A — shared cursor spine + Geo + settings prereq — **CODE-COMPLETE (build 0/0), in-game verify PENDING**
- NEW `RTAccess/Exploration/MapCursor.cs` — the shared world cursor (grid node + derived `Vector3`; `Has`/`Node`/
  `Position`/`Set`/`Clear`; falls back to the anchor's live view position when unplanted).
- `Exploration/Geo.cs` — added `Live(MechanicEntity)` (reads `View.ViewTransform.position`, the interpolated
  position, falling back to logical `Position`). *(Bearing/Vertical/Relative deferred to their consuming phase —
  no Phase-A consumer; `InteractableDescriber` already owns the compass.)*
- `Accessibility/TileExplorer.cs` — cursor state moved to `MapCursor`; added the two input slots (primary
  plain-arrows when HUD unfocused, **secondary `Shift+arrows`** shadow-immune); clears the shared cursor on
  deactivate/area-reset.
- `Exploration/Scanner.cs` — browse/review origin is now `ScanFrom()` = `MapCursor` when planted, else anchor's
  live position (two-cursor discipline). `WhereAmI` (Home) already existed; left as-is.
- `Main.cs` — wired `ModSettings.Initialize(persistentDataPath/RTAccess)` (was defined, never called).

### Phase B — re-gate + self-driven scan parity + Inspect-from-cursor — **COMPLETE (build 0/0), in-game verified**
- **Retired `ExplorationNav`** — deleted the dead file (engine-ring cycler, `EngineScanEnabled=false`, inert in
  mouse mode); removed its `Main.OnUpdate` call; fixed the 4 doc `<see cref>`s that pointed at it. The area
  scanner covers its function (PageUp/Down browse + categories + nearest-X review cycles + interact).
- **Migrated the scanner to registered `InputCategory.Exploration` actions** (15 actions in `InputBindings`):
  `scan.item_prev/next`, `scan.cat_prev/next`, `scan.review_{party,enemies,neutrals,objects}(_back)`,
  `scan.interact`, `scan.move_to`, `scan.where_am_i`, `scan.party`. `Scanner.Update()`'s raw polling replaced by
  internal safe entry points; removed from `Main.OnUpdate`. Now **`/input`-drivable** and the framework's chord
  shadowing (InGameScreen's `FocusedCats`↔`UnfocusedCats`) owns the Home/End HUD-vs-exploration split. Gating is
  now the Exploration category's liveness (`ControlState.HasControl` → dead in windows/dialogue/cutscene).
- **Behavior changes to verify:** (a) scanner-only chords (PageUp/Down, comma/period/N/M, Insert, P) now fire
  even while the HUD is focused (framework intent); (b) scanner is now live while *paused* (HasControl true in
  Pause). Both judged acceptable/positive; confirm in-game.
- **Inspect-from-cursor — DONE.** NEW `RTAccess/Exploration/Inspect.cs` + `scan.inspect` (**K**, `InputCategory.Exploration`).
  Resolves the target as the scanner's current unit selection (`Scanner.SelectedUnit()` = `_selectedKey as
  BaseUnitEntity`) else the tile cursor's occupant (`MapCursor.Node?.GetUnit()`); gates on
  `InspectUnitsHelper.IsInspectAllow` (the game's own affordance gate); then raises the **same** event the console
  inspect button raises — `EventBus.RaiseEvent<IUnitClickUIHandler>(h => h.HandleUnitConsoleInvoke(unit))` — which
  the live `InGameInspectVM` turns into the inspect template (synchronously, surface path) + a visible panel; finally
  reads `…SurfaceVM.StaticPartVM.SurfaceHUDVM.InspectVM.Tooltip.Value` aloud via `TooltipReader.GetFull`.
  *Key finding that simplified this:* in RT **opening Inspect IS the knowledge reveal** — both
  `TooltipTemplateUnitInspect` ctors call `InspectUnitsManager.ForceRevealUnitInfo` (the 2-arg chains the 1-arg;
  `ForceRevealUnitInfo` does `info.SetCheck(100,…)`, a *persistent* full reveal), so there is no knowledge to
  "respect" or cheat past — we do exactly what the sighted panel does, giving byte-for-byte parity. `InspectVM` is
  the **only** `IUnitClickUIHandler` subscriber, so raising the event has no other side effects (no selection/
  camera/command). *(v1 leaves the visual panel open after reading — matches the game's inspect toggle; a sighted
  helper sees it. `ShowInspect` stays true until dismissed; harmless to keyboard play.)*
- **Deferred within B:** *TileExplorer input-registration migration* — left on Phase-A raw polling (its keys don't
  collide with the scanner); migrate for consistency/`/input` later.

**In-game verification (harness session, save = Верхние палубы):**
- ✅ Scanner migration: `/input scan.where_am_i` → "Верхние палубы, east"; `scan.item_next`/`scan.cat_next`
  (Ctrl+PageDown) cycle categories ("Doors, 3. Дверь, activate, 16 metres, east, 1 of 3"; "Search points, 4…");
  `scan.review_objects` steps 1→2 of 4 and `scan.review_objects_back` (Shift+M) reverses to 1 — confirms handlers
  fire, exact-modifier bindings (Ctrl/Shift) are distinct, and the browse reads names/verbs/distances/bearings/
  counts/indices. One line per press (no double-fire).
- ✅ Phase A `Geo.Live`/`ScanFrom` (unplanted fallback): distances correct ("16 metres, east"; "Багардор, 0 metres").
- ✅ `Geo.RegionWord` where-am-i: "Верхние палубы, east".
- ✅ `ModSettings.Initialize`: `…/RTAccess/settings.json` written on load (`{}` — empty tree, correct for now).
- ✅ Mod loads clean: Prism (NVDA) backend; "Entering Верхние палубы." area announce fired.
- ✅ **Inspect-from-cursor** (re-verified on save = VoidshipOfficersDeck): `scan.review_party` selected the lead
  ("Багардор, ally, 22 of 22 HP"), then `scan.inspect` (K) read the **full** inspect panel aloud — "Здоровье: 22/22.
  Отражение: 0. Броня: 0%. Уклонение: 65%. Очки передвижения: 0. Эффекты… Оружие. Способности. Активные способности:
  Ни шагу назад!, Рывок, Полевая переподготовка. Пассивные способности: Мир смерти, Инстинкт выживания, Комиссар,
  Табельное оружие, Темные знаки, Блистательная слава, Воин." (wounds/defenses/move + active & passive abilities =
  byte-for-byte the sighted inspect). Gate confirmed: `scan.inspect` on a non-combatant neutral (Распорядитель
  наемников) → "Can't inspect …" because `IsInspectAllow` is false for ambient NPCs — exactly the game's affordance.
  *(Enemy path not exercised — no enemies in this peaceful save — but the logic is identical and `IsInspectAllow`
  returns true for enemies. Cursor-occupant fallback logic-verified, not runtime-tested — no "clear selection" verb
  to force the fallback yet.)*
- **NOT headless-verifiable (need real keypresses — `/input` force-invokes handlers, bypassing category liveness;
  and the tile cursor toggle is raw-polled, not registered):**
  - *Category-liveness gating* (scanner dead in a window/dialogue/cutscene). The mechanism is the EXISTING
    framework (`InGameScreen` drops Exploration via `ControlState.HasControl` — `ClickEventsController` null for
    full-screen UI) that already gates ui.*/Windows; scan.* simply joins that proven gate. Confirm by opening a
    service window and pressing PageUp → expect silence. *(A service-window open via the VM didn't take here —
    likely the post-load blocked-UI residue; ClickEventsController stayed non-null.)*
  - *MapCursor planted path* (scanner measures from the tile cursor): press **Ctrl+T** → "Tile explorer on" +
    tile readout, arrow to a spot, then PageUp → distances should be relative to the cursor, not the unit.
- **Pre-existing (not a regression):** the "Party" scan category reads empty while `scan.party` reads the roster —
  `ProxyUnit` doesn't classify the player's own party as scan items. Noted for the Phase C scanner rebuild.

### Phase C — scanner upgrade — **IN PROGRESS** (sliced; each slice build 0/0)
Phase C is being landed in slices so each is independently build- + harness-verifiable.

- **Slice 1 — footprint foundation (ScanBounds + nearest-edge distance) — DONE (build 0/0, in-game verified).** `5cf868a`.
  - NEW `Exploration/ScanBounds.cs` — verbatim port from WA (pure XZ geometry: Point/Circle/Rect/Polyline/Segments/
    Cloud + shared non-allocating `NearestOnCircleXZ`/`NearestInQuadXZ`/`ClosestOnSegment`/`InConvexXZ`).
  - `Exploration/ScanItem.cs` — added `Footprint`/`Bounds`/`NearestPoint(from)`/`Contains`; `DistanceTo` and
    `Describe` now measure to `NearestPoint` (the thing's nearest EDGE, not its centre). `Exploration/ProxyUnit.cs`
    — `Footprint => _unit.Corpulence`.
  - Point items (`Footprint=0`) resolve `NearestPoint` to `Position` exactly → markers/objects unchanged.
  - **Verified (save = VoidshipOfficersDeck, in combat):** geometry eval-confirmed (nearest enemy centre 6.75 m →
    edge 6.45 m at 0.30 m corpulence); live footprint path ran on a listed party member (planted cursor 6 tiles east
    → "Багардор … 8 metres, west" = 8.1 m centre − 0.30 edge, bearing consistent); exits/objects still read normally
    (point footprint, no regression). *A 0.30 m corpulence is a poor visual demonstrator (only shifts the rounded
    distance inside a narrow band below each X.5); the DRAMATIC multi-tile-unit / wide-door / AoE-edge readout is
    batched to the Phase C exit criterion below.*

- **Slice 2 — ProxyAreaEffect (live AoE / hazard zones) — CODE-COMPLETE (build 0/0); live AoE-edge readout DEFERRED
  to Phase C exit (batched).**
  - NEW `Exploration/ProxyAreaEffect.cs` — a first-class `ScanItem` over `Game.Instance.State.AreaEffects`. Reads the
    REAL runtime shape for footprint/bearing: `ScriptZoneCylinder` → circle of its `Radius`; `ScriptZoneBox` (a wall)
    → its rotated rectangle (four world corners via the shape's own transform); any other shape → circle of
    `GetBounds()` extent. Classified once: a zone that can catch enemies (`CanTargetEnemies` / not ally-exclusive) →
    **hazard**; ally-exclusive → **buff zone** (RT's `BlueprintBuff` has no harmful flag, so the target set is the
    reliable signal — WA's own fallback). `Name` = casting power/grenade name else the kind word; visible while
    in-game, not-ended, not-fogged.
  - `Exploration/ScanTaxonomy.cs` — added `Hazards`/`BuffZones` nodes. `Exploration/WorldModel.cs` — surfaces
    `state.AreaEffects`. `Exploration/Scanner.cs` — new `Group.Zones` (all AoEs, distance-sorted) + "Hazards" /
    "Buff zones" browse categories + `ReviewZones`. `Input/InputBindings.cs` — `scan.review_zones` (**Z**,
    Shift reverses), grouped "scanner".
  - **RT-specific facts (cross-checked vs decompiled):** `AreaEffects` holds `AreaEffectEntity` (not WOTR's
    `…Data`); `AreaEffectView.Shape` is `IScriptZoneShape` (has `GetBounds()`/`Center()`); `ScriptZoneCylinder.Radius`
    is an `int` in **world metres** (`ContainsPoint` compares raw XZ magnitude); `ScriptZoneBox` lives in a DIFFERENT
    namespace (`…View.Mechanics.ScriptZones`) than the cylinder (`…View.MapObjects.SriptZones`); RT's
    `BlueprintAbilityAreaEffect` has NO `Size` (WOTR did) → footprint fallback uses `Shape.GetBounds()`.
  - **Verified:** build 0/0; the empty path is live and clean (`/input scan.review_zones` → "Zones, none in sight."
    with `AreaEffects.Count()==0`); surfacing + Group.Zones + categories + Z registration all wired.
  - **DEFERRED (batched to the Phase C exit criterion):** the live AoE-edge readout on a real cylinder/box. This
    save has zero area effects and the spawn paths (`AreaEffectsController.Spawn`) need a blueprint GUID not to hand;
    loading blindly from the TOC risks a main-thread hang. Will be confirmed with a NATURAL combat AoE (thrown
    grenade / psychic template) alongside the multi-tile-unit and wide-door checks the plan groups together.

- **Slice 3 — persistent `WorldModel.Tick()` diff registry — DONE (build 0/0, in-game verified).**
  - `Exploration/WorldModel.cs` — rewritten from the per-call `Snapshot()` enumerator to a persistent registry: a
    `Dictionary<entity, ScanItem>` diffed every frame against the live pools (`AllBaseUnits` + `MapObjects` +
    placed `AreaEffects`, skipping on-unit auras via `AreaEffectView.OnUnit`). Keeps ONE proxy instance per entity
    STABLE across frames and raises `Added`/`Removed`; a genuinely-new entity is the only allocation (ContainsKey
    guard). New `Items` (live view) + `Find(key)` (O(1) selection re-find). This is the hard prerequisite for the
    Phase G cue/sonar cross-frame identity (a per-call snapshot makes fresh proxies each frame → every frame looks
    like an enter/exit).
  - `Main.cs` — `WorldModel.Tick()` wired into `OnUpdate` BEFORE the input tick (handlers read a current-frame
    registry; guarded so it never throws out of the loop). `Scanner.cs` — the three `Snapshot()` call sites now read
    `WorldModel.Items`; `ResolveSelected` uses `Find` (O(1), still marker-aware for the Exits/Poi direct path).
  - **Design note:** local-map markers (Exits/Poi) stay on their direct `LocalMapModel.Markers` path for now —
    folding them into the registry waits on Slice 4's taxonomy rebuild (today's `ProxyMarker.Primary` is always
    `Exits`, which would leak POI markers into the Exits browse category).
  - **Verified (save = VoidshipOfficersDeck):** transparent swap — `scan.review_objects` steps "WallMechanismSet02
    … 1 of 3" → "WallMechanismSet01 … 2 of 3"; `scan.announce_selection` (O) re-announced the SAME item (proves
    `Find` resolved the stable proxy across frames); category browse intact; no `Tick()` errors (objects listed →
    registry populated every frame). `Added`/`Removed` fire structurally (first real consumer lands in Phase G).

- **Slice 4 — taxonomy tree / ScanSounds / typed announce — RE-SCOPED after RT findings.** The full WA Slice-4
  port (Node tree + two-level browse + `ScanSounds` + the `ScanAnnounce*` verbosity pipeline) is **DEFERRED**; only
  the one concrete gap it would have closed is fixed now.
  - **DONE — party always visible in the scanner (build 0/0, in-game verified).** `Exploration/ProxyUnit.cs`:
    player-faction units are now always `IsVisible`/`CurrentlySeen`. RT's engine reports `IsVisibleForPlayer=false`
    for your OWN units when they aren't the current "spotlight" (out of combat / not the acting unit) — verified via
    `/eval` (Багардор: in `AllBaseUnits`, `IsPlayerFaction=true`, `IsVisibleForPlayer=false`) — which had left the
    "Party" browse category and the `review_party` cycle empty out of combat. **Verified:** `scan.review_party` →
    "Багардор, ally, 22 of 22 HP … 1 of 1" (was "Party, none in sight"). Closes the Phase-B pre-existing note.
  - **DEFERRED — Node tree + two-level browse.** RT's `ProxyMapObject` classifies into COARSE flat buckets
    (Containers / Doors / SearchPoints / Traps / Mechanisms / Exits / Scenery) with NO subtypes — WA's two-level
    browse existed because WA had rich container/unit subtypes (chest/corpse/environment/…), which RT can't detect.
    A category→subcategory browse on RT would present empty subcategories: navigation depth for no content. The flat
    `Categories` browse fits RT's granularity; keep it until RT classification gains subtypes.
  - **DEFERRED — `ScanSounds`.** It depends on `OverlayAudio.Dir` (Phase E, unported) and its only consumer is the
    Sonar system (Phase G). All audio ships gated-off pending the maintainer tuning pass — porting the sound-stem
    settings layer before either endpoint exists is premature. Lands with Phase F/G.
  - **DEFERRED — typed `ScanAnnounce*` verbosity pipeline.** Pure refinement (per-part on/off + the spatial
    sub-toggles) over today's working flat `ScanItem.Detail`. WA itself shipped the tree BEFORE the announce
    pipeline ("Announcements move onto it next"); layering it on later is clean and blocks nothing. Revisit once the
    taxonomy tree lands (if it does) or a verbosity request arrives.
  - **DEFERRED — folding markers into `WorldModel`.** No consumer needs cross-frame marker identity until the Phase G
    cues; markers work today via the direct `LocalMapModel.Markers` path (Exits/Poi cycles verified). Fold in with
    the marker exit/poi classification when Phase G lands.

**Phase C exit verify (batched, still pending):** footprint-aware bearing on a multi-tile unit, a wide door, and an
AoE edge; two-cursor discipline (review selection vs `MapCursor`) via `/eval`. **Phase C is functionally complete**
for RT (footprint, live AoE surfacing, persistent registry, party visibility); the deferred items above are audio
infra (F/G) or refinements with no current RT payoff. Next high-value work is **Phase D (targeting-from-cursor)** —
the user-named "act on the map" capability, which depends only on Slices 1+3 (both shipped).

### Phase D — targeting-from-cursor + PathInfo — **DONE (build 0/0, both halves in-combat verified)**

The "act on the map" half of exploration: once an action-bar ability arms (the game enters `PointerMode.Ability` via
`SetAbility`), a blind player had no mouse to click a target with and dead-ended. Phase D supplies the missing commit
by routing a keyboard-chosen target through the game's own `ClickWithSelectedAbilityHandler.OnClick`, so ALL of the
game's validation, target restrictions, refusal messaging (spoken by the warning reader), multi-target accumulation,
and the actual cast command are reused verbatim — we add zero combat rules.

- **Targeting commit/cancel bridge — DONE (build 0/0, verified end-to-end in live combat).**
  - `Exploration/AbilityTargeting.cs` (new) — the commit/cancel half. `Active` reads LIVE from the handler
    (`Game.Instance.SelectedAbilityHandler.Ability != null`), so cancelling elsewhere (right-click, re-toggle, mode
    switch) clears us too — no stale aim state. `CommitAt(unit, point)` calls `OnClick(unit?.View.gameObject, point,
    0)`; on refusal `OnClick` returns false and the game already spoke the reason, so we stay silent; on success we
    distinguish "one more target added" (handler still armed → multi-target) from the cast ("Firing on X." / "Ability
    used.") by whether `Ability != null` afterwards. `Cancel()` → `ClickEventsController.ClearPointerMode()`.
  - `Exploration/CursorTarget.cs` (new) — `Inside()` resolves what the world cursor is *on*: the nearest visible
    `IsUnit` registry item whose `Contains(cursor)` footprint holds `MapCursor.Position` (LevelGap 3 m rejects other
    floors). Units only — abilities aim at units or ground points, so off-a-unit falls back to the point.
  - `Exploration/Targeting.cs` (new) — the coordinator. `Aiming => Ability.Active` gates the exploration act keys:
    while aiming, **Enter** commits at the cursor (`CommitAtCursor` → `CursorTarget.Inside()`), **I** commits on the
    scanner review selection (`CommitOnSelection`), **Backspace** cancels. `Tick()` blurs the HUD the instant aiming
    begins (arming from the focused action bar would otherwise strand the player inside the HUD).
  - `Exploration/ScanItem.cs` + `ProxyUnit.cs` — added the `IsUnit` / `TargetUnit` virtuals the cursor resolver and
    commit path filter on (`ProxyUnit` overrides both; everything else stays the point-fallback default).
  - Wiring: `Scanner.InteractSelected` (I), `TileExplorer.InteractAtCursor` (Enter) + `MoveToCursor` (Backspace) all
    gain a leading `if (Targeting.Aiming) …` guard that redirects to commit/cancel; `Main.OnUpdate` ticks
    `Targeting.Tick()` right after `WorldModel.Tick()`. No new keybinds — the aim state repurposes the existing
    exploration act keys, whose normal jobs resume the instant aiming ends.
  - **Verified (live combat, save = combat encounter):** armed Pistol Shot from the action bar → HUD auto-blurred;
    **commit-on-selection (I)** on the reviewed servitor → "Firing on Взбесившийся сервитор." with a running
    `PlayerUseAbility` command, and the servitor's HP dropped 27→20 (the cast landed — the earlier stalled-animation
    read while unfocused was cosmetic, not a failed dispatch); **commit-at-cursor (Enter)** after planting the cursor
    on the servitor (Home) → `CursorTarget.Inside()` resolved it → "Firing on Взбесившийся сервитор."; **cancel
    (Backspace)** → "Targeting cancelled." + pointer mode cleared. All three commit/cancel paths confirmed.
- **PathInfo (the reachability half) — DONE (build 0/0, verified end-to-end in live combat).**
  - `Exploration/PathInfo.cs` (new) — the speech-only "can I move there, and how far" readout for the tile cursor in
    turn-based combat, where movement is the limited resource. `Preview(unit, dest, out canMove)` returns a spoken
    phrase and sets `canMove` only when the tile is a legal, reachable, stand-on-able destination this turn.
  - **Reachability is authoritative, not re-derived.** Instead of WA's non-existent `CombatMode.TryPathInfo` (or
    subscribing to the `IUnitMovableAreaHandler.HandleSetUnitMovableArea` push), it reads the game's OWN cached
    reachable set — `UnitMovableAreaController.CurrentUnitMovableArea` (public; the controller recomputes it on every
    movement-point change with the real CantMove/pet/action-points-blue formula). Null/empty set → "Out of movement."
    for free; `!set.Contains(dest)` → "Out of range." (walkable) / "Blocked." (not). This is strictly better than the
    plan's "cache the push": no subscription, no duplicated MP formula, and it can never diverge from the coloured
    tiles a sighted player sees.
  - **Cost/tile-count from our own pathfind.** The controller keeps only its set's node *keys* and discards the
    per-cell `WarhammerPathPlayerCell` costs, so `PathInfo` runs one `PathfindingService.FindAllReachableTiles_Blocking`
    over the same unit (the identical call the game itself makes on every point change) to price the cursor node:
    `IsCanStand` gates "Occupied, can't stop there.", and the exact step count comes from walking the cell
    `ParentNode` chain back to the origin. One pathfind per deliberate move-arming key press — not per frame.
  - `TileExplorer.MoveToCursor` — the combat first (arming) press now speaks `PathInfo.Preview` instead of the naive
    Chebyshev `TilesAway` (removed): a real path-aware preview at the exact moment the player is about to commit a
    move. The arm stays UNCONDITIONAL — the engine remains authoritative on commit — so a preview that reads "out of
    range" can never wrongly block a move the engine would allow (a determined second press still defers to the real
    move command, which speaks its own refusal).
  - **Verified (live combat, Багардор, MP restored via `SetActionPoints` cheat → 59-tile reachable area):** own tile →
    "You are here."; 2–3 tiles east (in range) → "Reachable, N tiles. Press again to move." with the exact path step
    count; 10 tiles east (walkable, beyond 6 MP) → "Out of range."; MP zeroed → "Out of movement."; the two-press
    arm→commit still fires the move ("Moving." — `TryCreateMoveCommandTB` returned a valid command, unchanged flow).
  - **Placement note:** lives in `Exploration/` beside the other Phase D files rather than the plan's future
    `Overlays/PathInfoSystem.cs` — the overlay framework is Phase E. When it lands, its arm-on-cursor-stop
    auto-announce simply calls `PathInfo.Preview` behind a toggle; the computation/phrasing is already the reusable
    half. **Deferred to that pass:** the MP-remaining nuance ("uses all your movement") — tiles alone is an
    unambiguous v1, and a reachable tile is affordable by definition.

### C–E thread status — **CONCLUDED at C+D (maintainer decision, 2026-07-01). Phase E deferred to the audio pass.**

The exploration scanner/targeting thread (Phases C–E) delivered its high-value, live capabilities in C and D:
**Phase C** (footprint-aware bearing, live AoE/hazard zones, persistent `WorldModel` registry, party visibility) and
**Phase D** (ability targeting from the cursor/scanner, path-aware move preview). **Phase E — the overlay framework
(`Overlay`/`OverlaySystem`/`OverlayManager` + the ~525-line settings registry + `MovementMode`/`Cursor` wrapper +
`AudioSystem`/`OverlayAudio`) — is deferred to the audio-tuning pass**, together with the F–I systems it hosts
(Sonar/WallTones/Fog/ObjectCue).

Rationale (from reading the WA source in full): the framework is a composition-and-play-mode abstraction (tri-state
Off/WhenMoving/Continuous modes, `ShouldPlay`, per-overlay customization subtrees) whose machinery earns its keep only
with **multiple** overlay systems — and every one of them except `PathInfoSystem` is a Phase F–I **audio** system that
this plan already defers pending the maintainer's audio-tuning pass. `PathInfoSystem` itself uses only Off/Continuous
(not the WhenMoving audio path), fires once on cursor-stop, and its RT-native computation already ships as
`PathInfo.Preview` (used live by the move-to preview). So porting ~700–900 lines of near-verbatim scaffolding now would
serve a single fire-once speech system that already works. Building the framework **with** the audio systems (F–I),
against real consumers, is the better sequencing. The PathInfo auto-announce-on-stop overlay is the natural first
consumer to add when that pass begins — it wraps `PathInfo.Preview` behind an `overlays.path` mode toggle.

---

## The audio pass (Phases E–I) — STARTED 2026-07-02 (maintainer ear-testing by hand)

Kicked off the deferred audio-tuning pass. Sequencing: **F (engine primitives) → E (overlay framework, with its
consumers) → G (Sonar/Fog/ObjectCue) → H (WallTones) → I (LogSystem)**. Everything audio ships gated OFF; the
maintainer auditions each system by ear before any default-on flip (harness can confirm a sound *fires* + inspect
computed pan/vol, but can't judge mix quality).

### Phase F — audio engine capability fill — **CODE-COMPLETE (compile: 0 CS errors / 0 CS warnings), maintainer ear-verify PENDING**

The primitive set the whole sound group rides. Pure NAudio, zero game coupling except the one cursor-frame read in
`Spatializer.PlayAt`. Synth-first (no WAV assets shipped yet — the plan's recommended default; the WAV path is built
and cached but exercised only once stems land).

- NEW `RTAccess/Audio/Spatializer.cs` — the compass-stable audio frame. `Pan(dxEast, dzNorth, panWidth)` (constant-
  power lateral pan, `Clamp(dxEast / Max(dist, panWidth), -1, 1)` — verbatim from WA `Spatializer.Cue`/Sonar),
  `VolumeFor(dist, refDist, minVol)` (`refDist/(refDist+dist)` floored — WA Sonar curve), and `PlayAt(worldPos, buf,
  vol, panWidth)` which reads `MapCursor.Position` + fixed north (+Z) and fires a panned one-shot. RT map space is
  +Z north / +X east (matches `Geo`/`InteractableDescriber`). The ITD + front/back low-pass richness of WA's
  `PositionalEmitter` is DEFERRED (pan-only for v1) — the ear pass can request it if the flat pan reads poorly.
- `RTAccess/Audio/AudioMixer.cs` — extended the lean one-shot mixer with the three Phase-F gaps: (1) **WAV decode +
  cache** (`LoadFile` → `AudioFileReader → WdlResamplingSampleProvider(44100) → MonoToStereoSampleProvider`, cached by
  path; a failed decode caches null so a missing stem doesn't re-hit disk/log every frame) + `PlayFile(path,vol,pan)`;
  (3) **resident, live-mutable looping voice** (`LoopVoice`: seamless wrap, volatile target L/R gains ramped per read
  block on the audio thread → click-free re-pan/re-gain) exposed as `PlayLoop(buf,vol,pan) → handle` / `SetVoice(handle,
  vol,pan)` / `Remove(handle)`, plus `Add`/`Remove(ISampleProvider)` internals. The existing `Play(buf,vol,pan)`
  synth one-shot is unchanged (Earcons still ride it).
- NEW `RTAccess/Audio/AudioProbe.cs` (DEBUG-only) — the ear-test bench, driven from `/eval`, bypassing every gate:
  `Left()`/`Right()`/`Centre()` (panned beep), `Sweep()` (one tone travelling L→R — the pan-direction check),
  `LoopStart()`/`LoopPan(p)`/`LoopStop()` (sustained voice re-panned live), `Bearing(dxEast,dzNorth)` (logs + plays
  the `Spatializer` pan for a source offset). All tones synthesized (no assets).

- **Deviations from the plan, with rationale:**
  - **`PlayOneShot` in-code frame lives in `Spatializer.PlayAt`, not in `AudioMixer`.** The plan put the MapCursor-
    frame read inside `AudioMixer.PlayOneShot`; keeping `AudioMixer` pure (takes a precomputed pan) and isolating the
    one game read in `Spatializer` is cleaner layering (matches WA, where `NAudioEngine.PlayOneShot` takes a
    precomputed pan and Sonar owns the frame) and functionally identical.
  - **`IAudioEngine`/`WwiseEngine`/`AudioEngines` selector + `IWallTones`/`CreateWallTones` DEFERRED to Phase H.** The
    plan lists the seam in F, but RT has exactly one engine; the Wwise backend is Phase J; the abstraction earns
    nothing until either a second engine exists or the wall-tone voice is built (Phase H). The resident `LoopVoice`
    primitive that WallTones will ride IS shipped now, so Phase H is purely the grid-raycast driver + the 4-channel
    wall-tone voice + (if wanted) the seam. Matches the plan's own "don't port speculative scaffolding" rule (cf. the
    ToneEngine drop).
- **Not built (deferred to their consuming phase):** `AudioAssets.Dir` stem→path helper (Phase G/H, when a stem set
  is picked); the 4-channel WallTones voice (Phase H).
- **Compile:** `dotnet build RTAccess.slnx -c Debug` → 0 CS errors, 0 CS warnings. (The 4 MSB deploy-copy errors are
  the running game locking `prism.dll`/`nvdaControllerClient64.dll` — environmental, not code; a redeploy needs the
  game closed.)
- **Maintainer ear-test (after a clean redeploy):** `/eval RTAccess.Audio.AudioProbe.Sweep()` → a tone should travel
  left→right; `Left()`/`Right()` → hard L / hard R; `LoopStart()` then `LoopPan(-1)`/`LoopPan(1)` → a sustained voice
  gliding side to side, click-free; `LoopStop()`. Judge: is the stereo image convincing, is the pan direction correct
  (left = west), does the loop wrap seamlessly / re-pan without clicks?

### Phase G — Sonar (first spatial-audio system) — **CODE-COMPLETE (compile 0 CS / 0 warnings), maintainer ear-verify PENDING; built LEAN, ahead of the Phase E framework**

The first real consumer of the Phase F primitives — and the reason to build Sonar first: it turns "read one tile per
keypress" into an ambient "feel the room". Rather than wait on the ~900-line Phase E overlay framework (whose mode-
composition machinery only earns its keep with several coexisting audio systems, per the C–E rationale above), Sonar
ships **lean and direct-driven** now, so the maintainer's first ear-test is the actual soundscape (pings panning around
the cursor) instead of a bench tone. It folds into the framework as one `OverlaySystem` when H/I land.

- NEW `RTAccess/Exploration/Sonar.cs` — static, ticked from `Main.OnUpdate` right after `WorldModel.Tick()` (beside
  `WorldModel`/`Targeting`, the established direct-drive pattern). Ports WA `SonarSystem`'s **staggered sweep** in
  shape: snapshot the perceivable things within a sense radius of `MapCursor.Position`, sort left→right by lateral
  offset, fire ONE ping, wait `gap = clamp(K/count, GapMin, GapMax)` (spacious for a few, compressing as the crowd
  grows), rest between sweeps. Each ping is positioned via `Spatializer.PlayAt(item.NearestPoint(cursor), …)` — pan by
  bearing, volume by `Spatializer.VolumeFor(dist)`.
- `RTAccess/Main.cs` — registered `exploration.sonar` (ChoiceSetting Off / When moving / Continuous, **default Off**)
  + `exploration.sonar_volume` (0–100, default 60) before `ModSettings.Initialize`; wired `Sonar.Tick(dt)` into
  `OnUpdate`. Gated on `InGameScreen.ExplorationActive` + `ControlState.HasControl` (silent + sweep-reset in menus/
  cutscenes so it starts fresh on return); the When-moving mode tracks `MapCursor.Position` changes with a 1.25 s grace.
- **Reuses the shipped Phase C surface:** `WorldModel.Items`, `ScanItem.{CurrentlySeen,IsDead,Position,NearestPoint,
  Primary}`, `ScanTaxonomy.*`. No new engine APIs.
- **Deviations / stand-ins (all noted in code):**
  - **Perceivable-now gate uses `CurrentlySeen`, not WA's `DetectableFrom`** (which wasn't shipped in Phase C). This
    pings only things perceivable right now (living units your party sees, non-fogged objects) and never through walls
    — cleaner than remembered-under-fog-with-LoS for v1. Revisit if remembered-item pings are wanted.
  - **Per-type sounds are a built-in synth palette, standing in for the deferred `ScanSounds`** stem layer: distinct
    pitch/gain per role (enemies bright-high 880 Hz, neutrals mid, party low+soft, hazards low, buff zones soft-high,
    all other objects a plain mid click); unmapped `Primary` → not pinged. No WAV assets. `ScanSounds` (per-node stems
    + Inherit chain) lands with Phase E/F assets if the maintainer prefers file stems.
  - **Tuning knobs are consts** (MaxDist 25 m, RefDist 5 m, PanWidth 3 m, gaps/rest) — promoted to settings once the
    ear pass picks which to expose.
- **Compile:** 0 CS errors / 0 CS warnings (deploy blocked by the running game, as with F).
- **Maintainer ear-test (after redeploy, in a loaded area):** set `exploration.sonar` to Continuous (menu, or
  `/eval RTAccess.Settings.ModSettings.GetSetting[[ChoiceSetting]]("exploration.sonar").Set("continuous")`), then move
  the party / plant + step the tile cursor (Ctrl+T + arrows). Expect a repeating left→right sweep of pings —
  enemies/neutrals/objects panned to their bearing, quieter with distance, nothing pinged through fog/walls. Judge: is
  it legible, are the timbres distinguishable, is the sweep cadence comfortable, does the pan match where things are?
  Then try When moving (pings only while moving) and Off.

**Ear-test outcome (2026-07-02, maintainer at the keyboard, dev harness `/eval`):**
- **Phase F primitives VALIDATED by ear.** Pan sweep travelled left→right smoothly (direction correct, left = west); the
  resident looping voice held a steady centred tone and **glided left→right click-free** on live re-pan, and stopped
  cleanly. The whole F chain (pan + WAV/loop primitive + Spatializer) is confirmed working on the maintainer's hardware.
- **Lean Sonar REJECTED by ear ("shouldn't use sweep").** Pings were audible and panned, but the staggered synth-ping
  sweep is **not** the model wanted. A full study of WA's soundscape (workflow, 10 agents + adversarial critique)
  established WHY — and corrected a key misconception:
  - **WA's object sonar is ALSO a standing-still Continuous sweep by default** — the sweep cadence itself is *not* the
    problem. What makes WA read as "a place" and ours as "blips": (1) **per-type recorded WAV stems** (identity — a
    chest sounds like a chest; ~17 stems), (2) **live-tracked 3D sources** re-panned/re-attenuated every frame against
    the moving cursor + the object's nearest edge (so a wall's sound rides along beside you), (3) **real 3D** — ITD +
    front/back low-pass, not pan-only, and (4) it is **one thin layer under a spoken tiled walk + a continuous
    wall-tone bed**, not the whole signal. Our lean sonar had none of 1–4 and shipped naked.
  - **The biggest gap: RT had NO wall-tone bed** — the continuous geometry/negative-space layer that is the literal
    "room you can feel." Chosen as the next build.

### Phase H — WallTones (the "shape of the room" bed) — **CODE-COMPLETE + DEPLOYED (compile 0 CS / 0 warnings), maintainer ear-verify PENDING; ships ON**

The continuous ambient bed the WA study flagged as the biggest missing layer and the cheapest high-impact win. Built
directly (not yet via the Phase E framework), riding the ear-validated Phase F resident looping voice, using
**WrathAccess's own wall-tone WAV assets** (maintainer's call: "reuse sounds from pathfinder for now").

- NEW `RTAccess/Exploration/WallTones.cs` — four seamlessly-looping cardinal voices (N/S/E/W); only their VOLUMES
  change, each rising as a wall nears that direction from the shared `MapCursor`. Fixed compass pan (E hard-right, W
  hard-left, N/S centred, distinguished by their differing WAV timbres — verbatim WA layout). **Wall distance is a
  GRID raycast**, not WA's navmesh linecast: step cell-by-cell via `CustomGridNodeBase.GetNeighbourAlongDirection(dir,
  checkConnectivity:true)` — returns null on a CUT edge (wall/fence between tiles) or off-grid — stopping there or on a
  non-walkable tile; distance = `(cells − 0.5) × GridCellSize (1.35 m)`. Volume = WA's quadratic proximity curve
  `t = 1 − dist/Range; t*t` (Range 5 m), so it bites close in. Cardinals N/E/S/W = 2/1/0/3 (matches
  `InteractableDescriber.AppendCover` / CoverVisualizer). Muted (not disposed) in menus/cutscenes for seamless resume;
  released + rebuilt on area change (`ExplorationEvents.OnAreaActivated`).
- NEW `RTAccess/Audio/AudioAssets.cs` — `Dir`/`WallToneSet`/`Interactable`/`Cue` path helpers (`<modroot>/assets/audio`,
  mirrors WA `OverlayAudio.Dir`).
- **Assets:** copied WrathAccess's entire `assets/audio` tree into `RTAccess/assets/audio` — 2 wall-tone sets (4 WAVs
  each) now, plus the 17 interactable stems + earcons staged for the sonar rework. `RTAccess.csproj` gains an
  `assets\**\*.wav` deploy rule (was JSON-only).
- `RTAccess/Main.cs` — settings `exploration.walltones` (Off / When moving / **Continuous = default**),
  `walltones_volume` (0–100, default 50), `walltones_set` (1/2, default 1); `WallTones.Tick(dt)` wired after
  `Sonar.Tick`. **Ships ON** (Continuous) per the maintainer's "ship it on so you can feel it."
- **DEPLOYED** (game was closed): RTAccess.dll + the 8 wall-tone WAVs are in the UMM folder; relaunch to hear it.
- **Deviations noted in code:** grid raycast replaces WA's navmesh linecast (RT is a discrete square grid); rides 4
  `LoopVoice` handles instead of WA's dedicated 4-channel voice (same result, reuses the Phase F primitive); the
  "keys-held-into-a-wall counts as moving" nuance of WA's MotionTracker is deferred (only matters for When-moving mode;
  Continuous default is unaffected).
- **Maintainer ear-test (relaunch, then move in an explorable area — no /eval needed, it's ON):** a corridor should
  read as loud-left + loud-right + quiet ahead/behind; walking toward a dead-end swells the forward tone; an open room
  goes near-silent on all four; an East wall sits in the right ear, a West wall in the left; North vs South are both
  centred, told apart by timbre. Move the party or the tile cursor (Ctrl+T + arrows) to feel the walls shift. Judge:
  does it convey enclosure/corridor/openness? volume vs speech? try Set 2 and the volume slider.

---

## Appendix B — Untracked gaps logged from the WA→RT scanner/explorer audit (2026-07-02)

Provenance: a multi-agent comparison of the WrathAccess (Pathfinder) scanner+explorer against RTAccess
(9 capability inventories → per-area cross-match → adversarial verification against RT source, 137 capabilities
classified: 46 shipped, 75 gaps/partials, 16 N/A). The audit confirmed **every high-value gap is already tracked**
by a phase above (Sonar/PlayReview → G, WallTones → H, RoomMap/world-map/AreaDetails → J). The six items below are
the medium/low gaps it surfaced that had **no** phase entry; logged here so they aren't lost. Each names the phase
it folds into. None is access-critical; do them opportunistically when working the host phase.

| # | Gap | Target phase | Value | Effort |
|---|---|---|---|---|
| B1 | Map-object loot subtypes + trapped/locked state word | C (announce refinement) | med | S |
| B2 | Map-object footprint (nearest-edge distance) | C (extends Slice 1) | med | S |
| B3 | TB acting-unit scan origin | C / cursor | med | XS |
| B4 | Where-am-I: indoors flag + fog/unexplored branch | A (indoors) + here (fog) | med | S |
| B5 | World-tracking spatial voices (moving-source audio) | H | med | M |
| B6 | Navmesh-probe unification | E (prep) | low | S |

**Status (2026-07-02) — B1, B2, B3, B4-indoors, B4-fog, and B6(non-audio) SHIPPED. B4-fog now VERIFIED IN-GAME** (deployed + probed on `VoidshipOfficersDeck` via `FogProbe.Dump`: a grid around the party read the party tile `(255,255,0)`=Visible, an explored ring `(0,255,·)`=Explored, outer `(0,0,·)`=NeverSeen → **green=explored, red=visible, threshold 128, no Y-flip**; the explored tiles were restored from the save on load so **green round-trips save/load**; `ReadPixels` works on the `RTHandle`; and end-to-end `WhereAmI` spoke **"unexplored"** on a planted never-seen frontier node). Both live-probe items are settled. B1/B2/B3/B4-indoors/B6 remain compile-only (in-game verify still pending). B4-room stays deferred to Phase J. NB: even the ship officers' deck has an active `FogOfWarArea` (`FOWDefault`), so "ships have no fog" is not universal — the `NoFow` guard still covers any area that genuinely disables it.
- **B1 DONE.** `ProxyMapObject.Detail` now appends the container kind (`LootContainerType` → "chest" / "environment" / "stash" / "single slot" / "cargo"; DefaultLoot/Unit add nothing), "trapped" when a `DisableTrapInteractionPart` is detected+armed (`part.Enabled && Owner.TrapActive`), and "locked" when `VariativeInteractionVM.HasVariativeInteraction(view)` (the same signal `Interact()` routes to the choice screen, so label and behaviour never diverge). Announce-only, flat `Containers` node kept. All game APIs are public (`InteractionPart<T>.Settings`, `TrapObjectData.TrapActive`). Words hardcoded English, matching the existing "open".
- **B3 DONE.** `Scanner.Anchor` now returns `TurnController.CurrentUnit` in turn-based mode when it `IsDirectlyControllable` (falls back to selection → main-char, and to selection on an enemy's turn), so the unplanted scan origin follows the acting unit — matching `CombatSuffix`/`Summarize`.
- **B4 DONE + VERIFIED in-game (2026-07-02).** The **indoors** flag shipped (`Scanner.WhereAmI` → "indoors" from `BlueprintAreaPart.m_IndoorType != None`, cached `AccessTools.Field`, guarded → outdoors). The **fog "unexplored"** branch now ships via new **`Exploration/FogProbe.cs`**: `FogProbe.Classify(worldPos)` reads ONE texel of the LIVE GPU fog mask `FogOfWarArea.Active.FogOfWarMapRT` (a public `RenderTexture`, `R8G8B8A8_UNorm`) with a **synchronous 1×1 `ReadPixels`**, maps world-XZ→texel via the fog area's own `GetWorldBounds()` (≡ the shader's `_FogOfWarMask_ST`; no reflection, no `TextureDensity` math — `rt.width/height` are read straight off the RT), and thresholds channels at 128 on the linear read: **red≥128 = currently-visible, green≥128 = ever-explored, both<128 = never-seen**. `WhereAmI` appends "unexplored" only when the *oriented* tile (planted `MapCursor` if the tile explorer is active, else the anchor's live pos — the party is never fogged, so the word only fires for a planted cursor on never-seen ground) classifies `NeverSeen`; fog-off/off-map → treated revealed. All members are public (csproj already refs `Owlcat*.dll`, so no new reference). This **supersedes** the earlier `RequestData`/`WarhammerLocalMapRenderer.m_LocalMapTexture` sketch — that path reads the whole ≤2048² RT *async* and only runs at save/area-transition; the live-RT direct read is one hop, ~sub-ms, keypress-synchronous. **Green=explored** was HIGH-confidence from C# (the coop `FogOfWarAreaCompressor` transfers *only* the green byte; `RestoreFogOfWarMask` + the history-copy pass persist green across frames and save/load) and is now **CONFIRMED in-game** (2026-07-02, `FogProbe.Dump` grid on `VoidshipOfficersDeck`): party tile `(255,255,0)`=Visible, explored ring `(0,255,·)`=Explored, outer `(0,0,·)`=NeverSeen; threshold 128; no Y-flip; green restored-from-save (so round-trips); `ReadPixels` works on the `RTHandle`; and `WhereAmI` spoke "unexplored" on a planted never-seen node. Fallbacks (kept documented, not needed): `Graphics.Blit`-target-texel-to-1×1-scratch then read, or a WrathAccess-style `FogExplored` accumulation grid; the threshold + a `FlipRow` knob stay behind named consts. **Room name** stays deferred to Phase J (RoomMap).
- **B2 DONE (unblocked by a live probe; compile 0/0, in-game verify PENDING).** A read-only `/eval` probe of a live ship-deck area (19 map objects) settled the bounds question: most objects carry ONE non-trigger body collider centred on `Position` with a sane radius (9/13 in [0.2, 3] m, 0 huge), so a collider-derived footprint is viable — BUT `Warp_Obj` (60 colliders) had its tightest collider **~12 m off** `Position`, the exact "reads *here* across the room" hazard. So `ProxyMapObject.Footprint` now takes the collider NEAREST `Position`, and is **guarded**: reject it (→ point, today's behaviour) when its centre is > ~1 cell (`FootprintOffsetCap = 1.5 m`) from `Position`, and clamp the radius to ~2 cells (`FootprintRadiusCap = 2.75 m`). Worst case therefore equals the old point behaviour — the guard makes it safe to ship unverified. Cached (collider geometry is static; the proxy is stable per entity). In-game confirm still wanted: a wide door / large console should read a metre or two closer (nearest edge) than before, and `Warp_Obj` should be unchanged.
- **B6 DONE (non-audio call sites).** New `Exploration/NavmeshProbe.cs` centralises the three grid-node primitives — `NodeAt(worldPos)` (the one canonical `GetNearestNodeXZ`), `Neighbour(node, dx, dz)` (the `CustomGridGraph.GetNode` cardinal step), `OnMesh(p, out node)` (the ~2 m on-mesh tolerance). Refactored `MapCursor.Set(Vector3)`, `TileExplorer.Move`, and `Geo.OnNavmesh`/`SnapToWalkable` to call it (provably behaviour-identical: `GetNearestNodeXZ` already returns `CustomGridNodeBase`, so the old `as` cast was a no-op, and the two `SnapToWalkable` break conditions collapse exactly into `!OnMesh`). Compile 0/0. **One call site left inline on purpose:** `WallTones.cs:127` (the audio session's in-flight Phase H file) — it adopts `NavmeshProbe.NodeAt` when that work lands, not touched now to avoid colliding with the other session's uncommitted changes. No vertical/floor sampling (WA's navmesh `NavmeshProbe` half) — RT areas are planar.
- **Fog-parity gate on the tile explorer DONE (compile 0/0; policy decision — "visual parity").** Confirmed with the maintainer that the mod must not give blind players a view of *unexplored* layout that a sighted player can't see (fog hides it on the local map). RT loads the whole level (navmesh = corridors/rooms, walls, and even placed entities) at area-load and fog is only a reveal overlay — verified live: walkable navmesh nodes exist in never-seen fog, and `DescribeTile` was reading their occupant/walkability/cover with **no fog gate** (X-raying unexplored space). Fix: `InteractableDescriber.DescribeTile` now gates on `FogProbe.Classify(tile)` to mirror the sighted local map — **NeverSeen → just "unexplored"** (no layout/occupant); **Explored (seen, not currently visible) → static layout + persistent objects (walls/doors/containers) but NOT live creatures** (`hideUnits`); **Visible / fog-off / off-map → full detail** (unchanged). The cursor's own offset is always spoken (the player drives the cursor; its position isn't fog-hidden). Cover left as-is (combat-only, reachable=lit tiles). An optional "reveal all / X-ray" toggle (default off) was offered but NOT built — trivial to add later (one `ModSettings` bool + a bypass in the gate) if a player wants ease over parity. Not yet re-verified in-game (game closed by maintainer after the B4 probe).

**B1 — Map-object loot subtypes + trapped/locked state flag.** `ProxyMapObject.NodeSet` (`ProxyMapObject.cs:97-117`)
collapses *every* `InteractionLootPart` into one `Containers` node, and `Detail` (`:52-73`) emits only the verb plus
"open" (for open doors) — no chest / environment / single-slot / player-chest distinction and no "trapped" / "locked" /
"restricted" state word. WA splits loot by `LootContainerType` and announces restricted/trapped. *Approach:* read the
loot part's container type (verify the RT enum name live) to pick a subtype word for `Detail` **only** — keep the flat
`Containers` browse node (a subtype *tier* would present empty subcategories, per the Slice-4 flat-taxonomy rationale);
add a "trapped"/"locked" flag from the trap/lock part state. Announce-only, no new node. (Corpse containers are already
separate: `ProxyUnit.LootableCorpse` → `Corpses`.)

**B2 — Map-object footprint / nearest-edge distance.** `ProxyMapObject` has **no `Footprint` override**, so it inherits
`ScanItem`'s point footprint (0 radius) and measures distance/bearing to the object's authored `Position`, not its
collider edge — while units (`ProxyUnit.Footprint = Corpulence`) and AoEs (`ProxyAreaEffect`, cylinder→circle /
box→rect) already ride `ScanBounds` (Slice 1). *Approach:* override `Footprint`/`Bounds` on `ProxyMapObject` from the
view's collider/bounds extent (→ `ScanBounds.Circle` or `Rect`), so a wide door / large console reads its nearest edge.
Doorway *portal-edge* segments (`ScanBounds.Segments`/`Cloud`) stay in Phase J (they need RoomMap doorway detection);
this is just the object's own collider.

**B3 — TB acting-unit scan origin.** `Scanner.Anchor` (`Scanner.cs:459-471`) = `SelectionCharacter.SelectedUnit ??
MainCharacterEntity`. In turn-based combat the reference unit is `TurnController.CurrentUnit` — combat *reads* already
resolve that (`ProxyUnit.CombatSuffix`, `Scanner.Summarize`), but the unplanted **scan origin** (`ScanFrom` fallback,
where-am-I, distance sort) still keys on the selection, so reviewing during a companion's turn *without selecting them*
measures from the wrong unit. *Approach:* in TB, resolve `Anchor` to `TurnController.CurrentUnit` when present (WA's
`CombatMode.ReferenceUnit`), falling back to selection → main-char. One-line change in `Anchor()`; the planted-cursor
`ScanFrom` path is unaffected (it already prefers `MapCursor`), so the two-cursor discipline holds.

**B4 — Where-am-I: indoors flag + fog/unexplored branch — SHIPPED.** `Scanner.WhereAmI` speaks the area display name +
a 3×3 `Geo.RegionWord`; it now also appends "indoors" and "unexplored". *Indoors:* interior when
`BlueprintAreaPart.m_IndoorType != None` (cached `AccessTools.Field`, guarded). *Unexplored (new `Exploration/FogProbe.cs`):*
the party is never fogged, so the query uses a **cursor-relative origin** (planted `MapCursor` when the tile explorer is
active, else the anchor's live position) and a **per-tile mask sample** — NOT the entity `IsInFogOfWar` (units/objects
only). The sample reads the LIVE GPU reveal mask directly: `rt = (RenderTexture)FogOfWarArea.Active.FogOfWarMapRT`
(`R8G8B8A8_UNorm`), `u=(x-wb.min.x)/wb.size.x`, `v=(z-wb.min.z)/wb.size.z` from `wb = area.GetWorldBounds()` (≡ shader
`_FogOfWarMask_ST`, so no reflection and no `TextureDensity` recompute), then a **synchronous 1×1 `ReadPixels`** at
`(u·rt.width, v·rt.height)` into a reused linear-RGBA32 texel and threshold at 128: **red = visible, green = ever-explored,
neither = never-seen**. This corrects the earlier sketch in this row (which pointed at `WarhammerLocalMapRenderer.m_LocalMapTexture`
/ `RequestData()` async / `FogOfWarBounds`+`LocalMapFowScaleOffset`): the whole-RT `RequestData` path is `R8G8B8_SRGB` (so
its threshold would be ~188, not 128) and only runs at save — the live-RT direct read is the right per-keypress path.
Green-is-explored is HIGH-confidence in C# (green-only coop compressor + green-persisting Restore/history-copy) but the
final blend is compiled HLSL → confirm once with `FogProbe.Dump(worldPos)` from /eval (see the Status live-probe note),
plus that `ReadPixels` works on this `RTHandle`. Room name stays deferred to RoomMap (Phase J).

**B5 — World-tracking spatial voices (moving-source audio).** RT's soundscape deliberately uses **staggered one-shots**
(`Sonar` → `Spatializer.PlayAt`), and `AudioMixer.LoopVoice` gives a *static-position* loop; WA's `SpatialSources`
continuously re-spatializes a resident/looping voice so it tracks a *moving* world entity (e.g. a patrolling enemy
hum). *Approach:* add an optional "attached emitter" to `AudioMixer`/`Spatializer` that re-reads a live
`ScanItem.NearestPoint(listener)` each tick and updates pan/volume — the moving-source analog — folded into Phase H's
resident WallTone voices. Explicitly **optional**: the one-shot sweep is the chosen default; only build this if the ear
pass wants persistent (not swept) source cues.

**B6 — Navmesh-probe unification.** RT's walkability/step logic is spread across `TileExplorer.Move` (grid
`GetNode(x±1,z±1)` step) and `InteractableDescriber` (occupant/cover/walkability readout) with no single probe helper;
WA has a `NavmeshProbe` the overlays share. *Approach:* before Phase E overlays add more grid-query consumers, extract
one probe helper (node-at-point, walkable?, neighbour-step, nearest-walkable) that `TileExplorer`, `MapCursor` plant,
and the future overlay systems all call. Pure refactor, no behaviour change; low value until Phase E lands.
