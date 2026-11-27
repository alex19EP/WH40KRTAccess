---
name: rt-space-maps-research
description: "Space maps — 3 surfaces (SectorMap/SystemMap/Exploration tablet); drive points (GameCommandQueue commands), overtip/HUD/info-window sighted sources, parity gates, WA GlobalMap recipe; SystemMapScreen + ExplorationScreen + colony screen APIs; design = system-map-first, flat nearest-first, speech-only v1"
metadata: 
  node_type: memory
  type: project
  originSessionId: 0b89054a-e5f6-44e1-9716-c16de5a15f4f
---

Research mapped everything sighted players see on RT's space maps. Synthesis:
`docs/space-maps-ui-research.md`; plan `docs/plans/orbital-listing-wilkes.md`.

**Three surfaces:** SectorMap (`GameModeType.GlobalMap`, warp between systems), SystemMap
(`GameModeType.StarSystem`, in-system flight), Exploration window (planet "tablet", section machine
NotScanned/Exploration/Colony/ColonyProjects). Both maps are continuous (no grid, XZ plane Y=0); all actions are
`GameCommandQueue` commands (MoveShip, ScanStarSystemObject, StartWarpTravel, CreateNewWarpRoute,
LowerWarpRouteDifficulty, VisitStarSystem, PointOfInterestInteract…) — clean drive points.

**Sighted info sources:** per-object **overtips** (name gated on scan state, icon rows w/ hover hints, tooltips)
+ HUD (Navigator resource, research %, anomaly radar, proximity noises) + info windows
(`SpaceSystemInformationWindowVM` — openable for ANY system via `HandleShowSystemInformationWindowConsole`, ideal
for a review cursor).

**Parity gates to preserve:** unvisited sector systems hide detail; unscanned objects show "???"/unknown glyph;
route danger encoded by line color (localized names exist).

**WA GlobalMap = recipe** ([[wotr-access-prior-art]]): two cursors one-way coupled, drive the game's real click
path, mirror-view-invoke-VM w/ grace window, mode-stack vs current-mode gate (RT: `RootUiContext.IsSpace` vs
`CurrentMode`), frozen-order/live-set lists, Exclusive encounter modal, scoped sonar sweep. Armies/miles/junction-BFS
don't transfer; `SectorMapController.GetStarSystemsToTravel/AllPassagesForSystem` replace the BFS.

**Design locked (maintainer):** system map FIRST (only surface the current save reaches); Objects browse = **flat
list, nearest-first** (no tree, no cycle keys); **speech-only v1** (earcons/sonar join the deferred audio pass).

**Screens shipped:**
- `SystemMapScreen` — `ctx.systemmap` graph-native base context: Objects (frozen-nearest-first) / Status /
  Actions stops, StartUnfocused, Escape blur↔game. `SpaceEvents` announcer (movement/scan/research/proximity,
  MarkCommandedMove suppression) + `systemmap.*` locale. Reused `InGameScreen.WindowLabel/WindowEnabled` +
  `InteractableDescriber.Compass8`. Labels read live overtip VMs (OvertipEntityPlanetVM etc.) keyed by entity
  UniqueId.
- `ExplorationScreen` (tablet, mirrors the game section machine via wrapper ActiveOnScreen; layer 9) +
  `AnomalyScreen` (Show/Hide ReactiveCommand tracking — VM has no shown-state) + `StatCheckLootScreen` (POI
  instance is VIEW-owned: `ExplorationStatCheckLootPCView.ViewModel`) + `ExpeditionScreen` + `GroupChangerScreen`
  (Surface OR Space; Confirm = view's AcceptChangeGroup command; Escape honors CloseCondition). GroundOperation
  party-picker is a hard dependency of the tablet flow (protagonist locked).
- `ColonyManagementScreen` (Ctrl+Y window; colony list → page) + shared `ColonyNodes` builders (both colony
  surfaces render through them; rewards popup Collect = `ColonyRewardsVM.HandleHide` claim path) + radar readout
  in Status + OpenAugmentations relocated U→Ctrl+U.

**Graph gotchas:** ControlId equality = structural key ONLY (loop nodes need unique suffixes); PushContext label
is identity-bearing (keep static). Reachable VM paths: `RootUiContext.SpaceVM.StaticPartVM.{ExplorationVM,
ZoneExitVM, SystemScannerVM, SystemMapNoisesVM}`; scan via `ExplorationScanButtonWrapperVM.Interact` (POI
promoted, header revealed). StarSystem-mode keybinds: **arrows/WASD=camera, bare Space=Pause, Tab=highlights**
(arbitration!), Ctrl+letter openers live, `OpenAugmentations`=bare U NOT relocated by default; warp gated off in
ch.1. XP toast duplicates the log (LogTap covers it). (Eval REPL quirks: [[rt-dev-harness]].)

**Open Qs:** system map fog is ambiguous — no `FogOfWarArea` in the ch.1 scene (gate = IsInGame + !HideInUI) but
`StarSystemFoWController` puts a `FogOfWarRevealer` on the ship and overtip views never test fog — resolve
in-harness before building a scanner ([[rt-visual-parity]]); warp-encounter→space-combat handoff unread; sector
map (M4) + sector-map game keybinds unenumerated.
