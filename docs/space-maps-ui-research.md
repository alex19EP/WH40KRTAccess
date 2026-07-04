# Space maps — what sighted players see, and how (research, 2026-07-04)

Workflow-researched (6 readers + completeness critic) over `decompiled/`, WrathAccess source,
and the mod tree. This is the "what does the game show sighted players" half; the
accessibility design comes next. Full agent reports lived in the session scratchpad; this doc
is the durable synthesis. All game claims cite decompiled paths.

## The three surfaces

RT's "world map" is actually **three stacked surfaces**, each its own game mode / VM tree:

| Surface | Game mode | What it is | Root VMs |
|---|---|---|---|
| **Sector map** (Koronus Expanse) | `GameModeType.GlobalMap` | warp travel between star systems | `SectorMapVM`, `SectorMapOvertipsVM`, `SectorMapBottomHudVM` |
| **System map** | `GameModeType.StarSystem` | ship flies inside one star system among planets/anomalies | `SpaceStaticPartVM` children, `SystemMapOvertipsVM` |
| **Exploration window** | (over StarSystem) | full-screen "tablet" when the ship lands on an object | `ExplorationVM` (+ colonization VMs) |

Both map modes run **continuous positions, no grid** (like surface exploration). Movement and
all actions are net-synchronized `GameCommandQueue` commands — clean drive points for us.

---

## 1. Sector map (warp map)

### Domain
- `Kingmaker.Globalmap.SectorMap/SectorMapObjectEntity.cs` — a system node: `IsExplored`,
  `IsVisited`, `IsScannedFrom`, `IsAvailable`, `IsHidden`; derived `Planets/Anomalies/OtherObjects`.
- `SectorMapPassageEntity.cs` — a warp route: `PassageDifficulty {Safe, Unsafe, Dangerous, Deadly}`,
  `DurationInDays`, `EncounterChance`, two-sided `ExploreStatus`.
- Player state `Game.Instance.Player.WarpTravelState`: `NavigatorResource`, `ScanRadius`,
  `IsInWarpTravel`, `CreateNewPassageCost`.
- Controller `Game.Instance.SectorMapController`: `CurrentStarSystem`, `Scan()`,
  `GetStarSystemsToTravel()` (systems reachable via an explored passage), `FindPassageBetween`,
  `GenerateNewPassage`, `LowerPassageDifficulty`, `VisitStarSystem` (loads the system area),
  `JumpToShipArea()` / `JumpToSectorMap()`.

### What sighted players see, channel by channel
1. **World-space node visuals**: node invisible until `IsExploredOrHasQuests` (quest markers
   force-reveal). Five states: ship-here / explored-but-unreachable / unvisited / visited /
   all-done. Name label when explored. A "can travel" decal ring on reachable systems **tinted
   by the connecting passage's danger**.
2. **Route lines**: one curve per passage, visible only when explored; **danger is encoded
   purely by line color/material/width** (indexed by `(int)PassageDifficulty`; localized names
   via `UIStrings.GlobalMapPassages.GetDifficultyString`). Routes touching the current system
   render at full alpha, others dimmed to 0.28.
3. **Rumour circles** (world-space) while the rumour objective is active + rumour overtips
   whose hover hint is the objective title(s).
4. **Per-system overtip** (`OvertipEntitySystemVM` / `OvertipSystemView`): system name; icons
   colonized/quest/rumour with hover hints (colonization info string, newline-joined numbered
   objective titles); floating "+N" when the current system yields Navigator resource. Buttons:
   **Info** (current system only), **Travel** (opens the navigator popup; double-click =
   travel immediately), **Visit** (enter the system area). Right-click tooltip
   `TooltipTemplateGlobalMapSystem`: unvisited systems show only "Unknown system" + quests/
   rumours; visited ones add colonized line, planet list, "enemies in system" (un-interacted
   Enemy anomalies), asteroid fields, interacted `ShowOnGlobalMap` anomalies. **Unvisited
   systems deliberately hide detail — parity law: we must keep that gating.**
5. **Navigator popup** (`SpaceSystemNavigationButtonsVM`): if no explored route → **Create Way**
   button showing cost (needs the current system scanned + enough Navigator resource); if a
   route exists → **Travel** button (label colored by danger, hover shows "travel to X,
   <difficulty>") + up to 3 **Lower danger** buttons, cost = steps × `LowerPassageDifficultyCost`.
   Insufficient funds → warning toast (`IWarningNotificationUIHandler`) + HUD counter flash.
6. **Bottom HUD** (`SectorMapBottomHudVM.Instance`): Navigator-resource counter (with hover
   cost-preview via `IGlobalMapWillChangeNavigatorResourceEffectHandler`), **Scan** button
   (once per system, 3 s pulse revealing systems+passages within `ScanRadius`, grants
   Navigator resource), center-on-ship, exit-to-bridge, ship customization.
7. **Windows**: `SpaceSystemInformationWindowVM.Instance` — side panel for ONE system
   (auto-opens on warp arrival in mouse mode): planets (name only if explored, colonized icon,
   resource icons, POI chips, row tooltip = the planet tooltip), other objects, interacted
   anomalies, quests/rumours. `AllSystemsInformationWindowVM` — "Known star systems" list of
   **visited** systems, sorted by name, rows with colonized/quest/rumour/resources hints +
   camera-jump button. The two windows are mutually exclusive.
8. **Layer filters** (`SpaceFiltersVM`): sighted-only toggles for Systems/Routes/Rumors layers.

### Warp jump dynamics (during travel)
`SectorMapTravelController`: duration in in-game days; past ~50% it can fire queued etude
events, then a **difficulty-weighted random-encounter dialog** (travel pauses →
`HandleWarpTravelPaused/Resumed`), arrival can be overridden by a destination **book event**;
encounters can hand off into **space combat** (`CombatRandomEncounterState` — not yet read).
`WarpTravelVM` is just an IsVisible toggle for the warp visual. Travel costs no resource —
only days + risk.

### Drive/read points
- Commands: `GameCommandQueue.ScanOnSectorMap()`, `.CreateNewWarpRoute(from,to)`,
  `.LowerWarpRouteDifficulty(to,difficulty)`, `.StartWarpTravel(from,to)`, `.VisitStarSystem(s)`.
- `IGlobalMapInformationWindowsConsoleHandler.HandleShowSystemInformationWindowConsole(entity)`
  opens the info window **for an arbitrary system** — ideal for a review cursor.
- Events: `ISectorMapWarpTravelHandler`, `ISectorMapScanHandler`, `ISectorMapPassageChangeHandler`,
  `INavigatorResourceCountChangedHandler`, `ISectorMapStarSystemChangeHandler`.
- Gotchas: `OvertipEntitySystemVM.TravelToSystem` is private (use `TravelToSystemImmediately()`
  or the popup VM); views enforce resource checks + a 2 s fill animation before commands, the
  controller re-validates but the no-money **toast** is view-layer — check
  `WarpTravelState.NavigatorResource` ourselves.

---

## 2. System map (in-system flight)

### Domain
- Everything is a `StarSystemObjectEntity` (`Kingmaker.Globalmap.SystemMap/`): subtypes
  `PlanetEntity`, `StarEntity`, `AsteroidEntity`, `CometEntity`, `CloudEntity`,
  `ArtificialObjectEntity`, and `AnomalyEntityData` (`Kingmaker.Globalmap.Exploration/`).
  Each carries `IsScanned`, `PointOfInterests` (status NotExplored/NeedInteraction/Explored),
  `ResourcesOnObject`, `ResourceMiners`; `IsFullyExplored` = scanned + all POIs explored.
  Enumerate via `Game.Instance.State.StarSystemObjects`.
- Anomalies: `AnomalyObjectType {Default, ShipSignature, Enemy, Gas, WarpHton, Loot}`,
  `IsInteracted`, `HideInUI`, interact-on Touch/Click/Distance; an anomaly can **block**
  another object (can't land until the anomaly is fully explored). Some anomalies move.
- Ship: `Game.Instance.StarSystemMapController.StarSystemShip` (`.Position`); movement =
  `StarSystemMapMoveController.MovePlayerShip` with circle-avoidance tangent pathing
  (`ShipPathHelper`), Esc = stop. Landing raises `IStarShipLandingHandler.HandleStarShipLanded`
  → opens the Exploration window (or the Transition window for `MultiEntranceObject`s).

### What sighted players see
1. **Overtips** (three kinds, `SystemMapOvertipsVM`):
   - **Planet** (`OvertipEntityPlanetVM`): name label only when scanned (else an
     "unknown planet" glyph); label color = 4-state scheme (Default/Quest × Active/Inactive
     — **Inactive = fully explored = "nothing left here"**). Icon row with hover hints:
     colony (colonization info incl. project % / waiting event), quest + rumour (numbered
     objective titles), resource + extractor (fade-in panels listing name/count rows),
     POI ("Points of interest detected:" + names), DLC2 tithe crown. Click → fly there.
     Hover tooltip `TooltipTemplateSystemMapPlanet`: explored/not-explored line, "scan
     required" when unscanned, colonized/can-colonize, quests, rumours, and (scanned only)
     POI list, resources, colony events.
   - **Anomaly** (`OvertipEntityAnomalyVM`): name + type icon — Loot icon / Enemy icon with a
     red danger ring / question-mark for the rest; hover hint = localized type name; quest
     icon. Hidden when `HideInUI`. `OpenAnomalyInfo()` opens the anomaly window.
   - **Generic object** (`OvertipEntitySystemObjectVM`): name = literal `"???"` until scanned;
     POI icon + hint. **Only artificial objects and asteroids get a visible overtip** — stars,
     comets, clouds have a VM but `m_Visible` is false.
   - No distance/fog test in the overtip views — gates are `IsInGame`, per-kind visibility,
     on-screen, cutscene. But see the FoW open question below.
2. **HUD** (`SpaceStaticPartVM` children): resource bar (all colony resources + Profit Factor,
   `SystemMapSpaceResourcesVM`); system title + **research %** (`SystemTitleView`, updates via
   `IStarSystemMapResearchProgress`); **anomaly radar** (`SystemScannerVM` — radar sweep on
   area load plotting every un-interacted, non-hidden anomaly + color-coded type list, or
   "no anomalies in system"); **proximity noises** (`SystemMapNoisesVM` — three interference
   icons within 10 units: anomaly near / unexplored-POI planet near / resource planet near —
   a ready-made sonification channel); trajectory line while moving; time-rewind controls
   (`TimeRewindVM`: pause/resume/rewind + current date); warp-jump/stop/ship-menu cluster
   (`ZoneExitVM`: `ExitToWarp()` gated on `IsWarpJumpAvailable`, `StopShip()`, bridge/customization).
3. **Scanning the system object** (two-phase `ScanStarSystemObjectGameCommand`): flips
   `IsScanned` → real name appears, POIs promote to NeedInteraction and their icons/lists
   appear, resource/extractor icons appear, planets grant XP, research % recomputes.
4. **Notifications** (toasts, `SystemMapNotification*`): colony events, mining start/stop,
   encyclopedia entries; XP and Profit-Factor toasts ride the same channel.
5. **Time**: `StarSystemTimeController` sets **GameTimeScale = 2880× while the ship moves** —
   in-game days pass during flight; colony/event timers fire mid-flight (sighted players watch
   the date spin in the HUD).

### Drive/read points
- Fly: `GameCommandQueue.MoveShip(entity, VisitType.MovePlayerShip)` (exactly what overtip
  click does); `InteractWithStarSystemObjectGameCommand(entityOrNull, worldPos)` for empty
  space (the click handler clamps coords first); `StopStarSystemStarShip()`.
- Scan: `GameCommandQueue.ScanStarSystemObject(entity, finishScan: true)`.
- Events: `IStarSystemShipMovementHandler`, `IStarShipLandingHandler`,
  `IScanStarSystemObjectHandler`, `IAnomalyHandler`, `IExplorationHandler`,
  `IAnomalyUIHandler`, `IMiningUIHandler`, `IStarSystemMapResearchProgress`.

---

## 3. Exploration window + colonization

- Opens on landing (`IExplorationUIHandler.OpenExplorationScreen(sso)`); root `ExplorationVM`
  with a section machine `{NotScanned, Exploration, Colony, ColonyProjects}`; shared state
  singleton `StarSystemObjectStateVM.Instance` (current object/planet/colony/IsScanned).
- **Header is VIEW-owned** (`ExplorationBaseView.SetPlanet`): name (or `"???"`), and for
  planets Tithe Grade / Aestimare / World Type straight from `BlueprintPlanet` ("undetermined"
  until scanned). No VM mirror — reflect the view or blueprint.
- **POIs**: icon buttons on a ring around the 3-D planet. 8 blueprint subtypes with distinct
  interact behavior — BookEvent (dialog), Cargo (adds cargo), ColonyTrait (message box),
  Expedition (slider sub-dialog: people count vs reward tiers), GroundOperation (party picker
  → loads a ground area), Loot (space loot window), Resources (silent grant, icon disappears),
  StatCheckLoot (skill-check sub-window → loot with the check result). All funnel through
  `GameCommandQueue.PointOfInterestInteract(entity, poiBlueprint)`. Per-POI VM exposes
  Name/Icon/IsExplored/IsInteractable/IsQuest/QuestObjectiveName/IsRumour; hover hints say
  not-interactable / already-explored / not-explored.
- **Resources**: ring icons (click = install/remove **resource miner** with confirm boxes,
  gated on miner items in inventory) + a read-only list panel (icon/name/count, tooltip =
  description); miner counter `x N`; global resource bar overlays this colony's deltas.
- **Colonization**: there is **no colonize button** — colonies are founded via the
  `CreateColony` game action inside dialogs (typically a Book Event POI choice), gated on
  scanned + Profit Factor ≥ cost. Colony section shows stats (Efficiency/Contentment/Security
  with negative-modifier recolor + tooltips), trait chips, event chips (click starts the event
  dialog in-system; remote = "needs visit" warning), projects (rank-tiered picker;
  requirements/rewards with checkmarks; `StartColonyProject` command), rewards-since-last-visit
  popup (claims via `ReceiveLootFromColony`). The ship-side **Colony Management** service
  window reuses the exact same component VMs with `isColonyManagement: true`.

---

## 4. WrathAccess prior art (what transfers)

WA's world map (`wotr-access/src/Exploration/GlobalMap*`, `Screens/GlobalMapScreen.cs`) is the
recipe. Portable patterns, all encoded in code we've already ported siblings of:

1. **Two cursors, one-way coupling** — review cursor (category browse + nearest-first cycles)
   never moves the map cursor; `/` plants the movement cursor at the review point. Same model
   our surface explorer already adopted.
2. **Drive the game's real click path** — WA documents that a hand-rolled interact softlocked
   the map; it uses `GlobalMapPointView.HandleClick()`. RT equivalent: the `GameCommandQueue`
   commands above (which ARE the game's click path).
3. **Mirror the VIEW when the VM is thin, invoke through the VM** — WA replicates
   `FillDialogInfoLocation` (travel time, closed/restricted, enter-confirm) while calling
   `vm.Accept()/Close()`, resolving the live VM per press with a grace window against
   dispose/recreate churn. RT's system-info window and navigator popup will need the same.
4. **Mode-stack vs current-mode gating** — `IsGlobalMap` (stack) vs `CurrentMode == GlobalMap`
   (interactive) prevents the resume-travel infinite loop under dialogs/events. RT analog:
   `RootUiContext.IsSpace` vs `GameModeType.StarSystem/GlobalMap`.
5. **Frozen list order, live set** for the location list (no reshuffling under the cursor as
   the traveler moves).
6. **Fold hover-only lore into the panel readout** (sighted parity — that's what hover shows).
7. **Travel-event modal as an Exclusive screen** (encounter popup → our warp-encounter case),
   with a "blocking popup must never be silent" fallback.
8. **Sonar sweep scoped to the map** — staggered left→right pings around the cursor, per-type
   sounds via the taxonomy settings tree; junctions default Silent. RT taxonomy would be
   planet/anomaly-type/station/asteroid.

Does NOT transfer: armies/crusade cycles, settlement Manage, miles (1 unit = 1 mile is WotR's
`MilesTravelled`; RT units unverified), the road/junction edge network + BFS (RT's sector graph
is passage-based — same *pattern*, different substrate: `AllPassagesForSystem` +
`GetStarSystemsToTravel` give us connected/reachable cycles almost for free).

Pre-staged in RTAccess already: `InputCategory.WorldMap` (zero references) and a block of
`worldmap.*` locale keys — but they encode WotR's model (junctions/armies/miles) and need
reshaping for RT.

---

## 5. Current RTAccess coverage (honest gap list)

**Works in space today**: ~12 overlay screens resolve VMs Surface-OR-Space (dialogue, book
event, inventory, journal, char info, level-up, loot ×4, esc menu, transition deck-map);
LogTap voices all log channels (Profit Factor, cargo, colony, Navigator resource lines) in any
mode + L review; area-entry/loading announces; Ship/Colony/Cargo window openers with correct
availability gates (but no content screens behind them).

**Dead in space**: no screen resolves `RootUiContext.IsSpace` (no space base context — the
2026-07-03 audit calls this "apparently missed, no deferral evidence"); every exploration
verb/cue is gated `ExplorationActive` = `ctx.ingame` + `GameModeType.Default`, so scanner,
tile explorer, sonar, fog cue, K gauges (incl. the only Profit Factor readout) are all inert
in `GlobalMap`/`StarSystem`/`SpaceCombat`; `WorldModel` never reads `State.SectorMapObjects`
or `State.StarSystemObjects`; zero code for warp, anomalies, scanning, colonization,
StatCheckLoot. Space combat is separately deferred (HOOKMAP subsystem 7).

---

## 6. Open questions / follow-up reads

1. **System-map fog of war** (HIGH, parity law): `StarSystemFoWController` attaches a real
   `FogOfWarRevealer` to the player ship (verified: `Kingmaker.Controllers.StarSystem/
   StarSystemFoWController.cs`), yet overtip views never test fog. Resolve in-harness: are
   far objects' overtips visible to sighted players before the ship approaches, or does
   `IsInGame`/fog hide them? Decides whether our existing `FogProbe.Classify` gating extends
   to the system map.
2. **Warp encounter → space-combat handoff**: `CombatRandomEncounterController` /
   `CombatRandomEncounterState` unread.
3. **Game keybindings active in map modes**: `KeyboardAccess.GetGameModesArray` — the
   `ServiceWindows`/`ForPause`/`CameraControls`/`WorldFullscreenUI` groups include
   `StarSystem`+`GlobalMap`, so GameKeybinds' Ctrl+letter relocation already applies; the
   concrete pause/camera/time bindings to arbitrate around are unenumerated.
4. **`SpacePointMarkersVM`**, `ColonyNotificationType` enum values, console view variants —
   skipped.
5. Verify "no colonize button" in-harness (the CreateColony-via-dialog path is well-cited but
   worth one live confirmation when we build the exploration screen).
