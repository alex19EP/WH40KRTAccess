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
4. **Fog is entity-scoped, not positional.** There is **no** `FogOfWarController.IsInFogOfWar(Vector3)` (no `FogOfWarController` class exists; only static `FogOfWarControllerData` with no position query). `IsInFogOfWar` is an argument-less entity property (`Entity.cs:253`) plus group wrappers. The Fog cue therefore cues **entities** transitioning into/out of fog (`entity.IsInFogOfWar`), not the cursor tile. A per-tile fog test would not compile. (If a texture/feature fog test is ever needed, go via `FogOfWarControllerData.GetFogOfWarFeature()`.)
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

### Phase D — targeting-from-cursor + PathInfo — **targeting DONE (build 0/0, in-combat verified); PathInfo PENDING**

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
  - **PENDING — PathInfo (the reachability/MP half).** Port a speech-only "Path, N tiles / out of MP / No path"
    readout: WA's `CombatMode.TryPathInfo` doesn't exist in RT, so drive it from the cached
    `IUnitMovableAreaHandler.HandleSetUnitMovableArea` push (reachable cells + MP) resolved against the cursor node.
    Default ON, speech-only, no overlay. Lands next.
