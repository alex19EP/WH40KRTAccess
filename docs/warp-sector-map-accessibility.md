# Warp / Sector-Map accessibility

**Status: implemented from the decompile, NOT yet tested in-harness.** Warp travel is quest-gated in the
campaign, so the live game could not be driven when this was built (shared-harness rule + no access to the
sector map yet). Everything below compiles and is wired in; the items tagged **TODO(harness)** need live
verification once the sector map is reachable. This supersedes the "M4 — sector map (sketch only)" section of
`orbital-listing-wilkes.md`.

The sector map is the galaxy-scale sibling of the in-system star-system map, so this is deliberately a clone of
the shipped `SystemMapScreen` recipe — a flat, frozen, nearest-first list of graph nodes, labels mirroring the
overtip card, verbs driving the game's own VM/controller. **No new graph primitive was needed.**

## What the warp UI shows a sighted player (game model)

Warp travel = **`GameModeType.GlobalMap`**, a full game area (`BlueprintSectorMapArea`), hosted on the shared
`RootUiContext.SpaceVM`. `SpaceStaticPartVM` maps `GlobalMap → SpaceStaticComponentType.SectorMap → SectorMapVM`.

- **Star-system nodes** — `SectorMapObjectEntity` (`Game.Instance.State.SectorMapObjects`; `.View` =
  `SectorMapObject`). Visible when `View.IsExploredOrHasQuests`. State: `IsExplored/IsVisited/IsScannedFrom/
  IsAvailable/IsHidden`. The node's visual state (ship-marker / unvisited-no-path / unvisited / visited) is
  chosen by `SectorMapObject.SetPlanetVisualState()` — a glyph a blind player can't see, so we speak it as a word.
- **Warp passages (routes / edges)** — `SectorMapPassageEntity`: `CurrentDifficulty` (Safe/Unsafe/Dangerous/
  Deadly), `CurrentExploreStatus`, `DurationInDays`, `EncounterChance`. Difficulty shows on-screen ONLY as line
  colour + 0–3 skulls — no text — so it must be verbalized. Reachable set from the current system =
  `SectorMapController.GetStarSystemsToTravel()`; the passage between two systems = `FindPassageBetween(a,b)`.
- **Bottom HUD** — `SectorMapBottomHudVM` (static `.Instance`): `CurrentValue` = **Navigator's Resource** (the
  warp economy currency), `IsScanAvailable/IsTraveling/IsScanning/IsExitAvailable`; actions `ScanSystem()`,
  `ExitToShip()`, `OpenShipCustomization()`.
- **In-transit "HUD" — there isn't one.** `WarpTravelVM` is a headless visibility flag with no bound view. In
  transit you get warp FX + sound, the ship marker crawling the route, and the HUD with Scan/All-Systems locked.
  Progress is private timer state (no public %). **Travel is non-cancellable once started** (no abort command).
- **System Information window** (`SpaceSystemInformationWindowVM` + Planet/OtherObjects/Anomalies children) —
  the per-system dossier (planets, resources, anomalies), visited-gated. Deferred (see below).

### Premise corrections (verified in the decompile)
- **No provisions.** The only warp currency is Navigator's Resource, and **travel does not consume it** —
  scanning and route-building do. Travel's cost is time + encounter risk.
- **No momentum / no warp-weather gauge** during transit. "Momentum" is a combat mechanic; `BlueprintWarpWeatherRoot`
  is the combat/Veil system. In-transit danger = passage difficulty + the encounter roll only.
- **`SectorMapPassageEntity.DurationInDays` is measured in SEGMENTS, not days** — `SectorMapTravelController`
  compares elapsed `.TotalSegments()` against `m_TravelDuration = passage.DurationInDays`, and `GameHistoryLog`
  logs "duration N segments". **We do NOT speak this value yet** (see the checklist) to avoid a wrong "N days".

## What was built (no live access needed)

### `RTAccess/Screens/SectorMapScreen.cs` (new)
Base context, `Key "ctx.sectormap"`, Layer 0, `StartUnfocused`. `IsActive()` = `CurrentlyLoadedArea is
BlueprintSectorMapArea && SpaceVM != null` (a distinct area type ⇒ naturally mutually exclusive with the other
Layer-0 contexts). `SectorMapVM` is re-resolved every frame via `SpaceVM.StaticPartVM.TryGetComponentVM(
SpaceStaticComponentType.SectorMap)` — never cached (it is disposed on mode change). Three Tab stops:

- **Systems** — every `IsExploredOrHasQuests` system, one `GraphNodes.Button` each, order **frozen nearest-first**
  at area entry (`_order` by `UniqueId`; the WA no-reshuffle lesson). Label mirrors the card: name, a status word
  (here / visited / reachable+`route`(difficulty+encounter%) / no route), colony (`ColonizationController.
  GetColony`), quest (`SectorMapObject.CheckQuests()`), rumour (overtip `CheckRumours()`), bearing + distance
  (`Exploration.Geo`, XZ plane). Enter → a `ChoiceSubmenuScreen.OpenRows` verb picker (Travel / Enter system),
  gated per-system and driven through the overtip VM's own `TravelToSystemImmediately()` / `VisitSystem()` (which
  keep the passage-explored guard + coop ping), with a controller fallback (`WarpTravel` / `VisitStarSystem`).
- **Status** — Navigator's Resource (+ pending-change), current system, travel/scan state. **These read live even
  during a jump** (only verbs gate on `Interactive`).
- **Actions** — Scan system (`Hud.ScanSystem()` when `IsScanAvailable`), Exit to ship (`Hud.ExitToShip()`), Ship
  customization, the GlobalMap service-window openers (reused from `SystemMapScreen`), Log review.

`Interactive` (the **verb** gate — reading is never gated) = top screen is `ctx.sectormap` AND `CurrentMode ==
GlobalMap` AND not `IsTraveling`/`IsDialogActive`. There is no "stop travel" verb by design (the game has no abort).

### `RTAccess/Accessibility/WarpEvents.cs` (new)
The `SpaceEvents` sibling — a long-lived `EventBus` subscriber, subscribed in `Main.Load`, `.Tick()` in
`OnUpdate` (edge-state housekeeping only), unsubscribed in unload. All lines passive → QUEUED. Voices:
entering warp (dest + difficulty; suppressed once via `MarkCommandedTravel()` when our own screen commanded it),
arrival, pause/resume (edge-guarded — resume also fires on load), scan started / complete (+ new-contact count),
new route charted, route difficulty reduced, and a neutral mid-jump scripted-event cue. Navigator-Resource
spend/gain is deliberately **not** voiced here (LogTap already speaks the game's NavigatorResource log channel —
voicing it here would double).

### Wiring & localization
- `ScreenManager.Initialize()` — `Register(new SectorMapScreen())` after `SystemMapScreen`.
- `Main.cs` — `EventBus.Subscribe/Unsubscribe(WarpEvents.Instance)` + `WarpEvents.Instance.Tick()` in `OnUpdate`.
- `assets/locale/enGB/ui.json` — a fresh `sectormap.*` block (screen/tab labels, status words, difficulty words,
  scan/travel/visit action labels, and the `evt_*` warp-event lines). Generic card flags reuse the existing
  `systemmap.has_colony/has_quest/has_rumour/units`. Game content (system/quest/rumour names) passes through
  untranslated.

Compiles clean (`dotnet msbuild RTAccess/RTAccess.csproj -t:Compile` — 0 errors, 0 warnings).

## In-game test checklist (run once warp travel is unlocked)

Prep: with the dev server up, `create_new_passage` / `lower_passage_difficulty` (`CheatsGlobalMap`) can set up
routes to test against. Watch `speech_log.txt` (`[!]` interrupted, `[+]` queued) and `focus_log.txt` throughout.

**A. Screen comes up & is safe**
1. Enter the sector map → the `SectorMapScreen` should activate (announces "Sector map"); Tab enters the Systems
   list; arrows stay with the camera while unfocused; Escape while focused blurs to the map, while unfocused
   yields to the game.
2. Confirm it does NOT fight the star-system map (`SystemMapScreen`) or any service window — exactly one Layer-0
   context active.

**B. Systems list & labels (the label-mirrors-the-card law)**
3. Browse the Systems list. For each system confirm the spoken label matches what the card shows: name (or
   "Unknown system" when it shouldn't be named), the status word (current/visited/reachable/no route), and the
   colony/quest/rumour flags. **TODO(harness): verify the overtip collection populates under our forced Mouse
   mode** (`OvertipsByEntity`); if labels are missing flags, the overtip VMs aren't refreshing and the label must
   read entity/controller state directly instead.
4. Confirm the frozen nearest-first order does NOT reshuffle under the cursor as the ship/camera moves.
5. On a reachable system, confirm the route detail (difficulty word + encounter %) matches the skull tier shown.
   **TODO(harness): once the skull-tier wording is seen, decide whether to switch `DifficultyWord` to the game's
   own `UIStrings.GlobalMapPassages` string for an exact match.**
6. **TODO(harness): decide the `DurationInDays` unit.** Time a real jump against the value; if it is segments,
   add a `Calendar` segment→days conversion and only THEN add duration to the route label (currently omitted).

**C. Verbs (drive the game's own methods)**
7. Enter on a reachable system → verb picker shows "Travel to X"; selecting it starts the jump. Confirm no
   double-speak (our keypress "Travel to X" vs WarpEvents' entering line — `MarkCommandedTravel` should suppress
   the latter).
8. Enter on the current system (if it has an area) → "Enter X" loads the area.
9. Confirm verbs are inert (speak "Not available now") while mid-jump / under a dialog, but the Status stop still
   READS during the jump.
10. **TODO(harness): EventSystem leak.** The game's own overtip popups stay live under our overlay — confirm
    travel/visit does not double-fire (the game view's button + our verb). If it does, add a `DialogChoiceGate`-
    style ownership gate at the travel/visit method.

**D. Passive narration (`WarpEvents`)**
11. Start a jump → "Entering the warp toward X" (with difficulty); at the far end → "Arrived at X". Pause/resume
    (e.g. leaving/returning to the map mid-jump) → single paused/resumed lines, not repeated.
12. Scan from the current system (Actions → Scan) → "Scanning the system", then "Scan complete" (+ "N new
    contacts" if any were revealed).
13. Create a route / lower a route's difficulty (Navigator-Resource spend) → the route-charted / route-safer
    lines fire, and Navigator's Resource is NOT double-announced (LogTap owns it).
14. **TODO(harness): mid-jump scripted events.** Trigger a jump that fires a mid-jump etude — confirm the neutral
    "Warp disturbance" cue is useful and NOT redundant with a LogTap line or a dialog. Gate or remove if it doubles.
15. Confirm a warp random-encounter routes through the existing `DialogueScreen`/`BookEventScreen` (no separate
    encounter screen is built, matching WoTR's `GlobalMapEncounterScreen` being intentionally not ported).

**E. Actions & input**
16. Exit to ship, Ship customization, and the service-window openers work; **TODO(harness): prune `WindowButtons`
    to the set actually enabled in GlobalMap mode.**
17. Input arbitration on the sector map (as with the system map): arrows/WASD = camera, bare Space = pause, Tab =
    our list. Confirm the screen claims only what it needs while focused and nothing leaks.

## Deferred (not built — later slices)
- **System Information dossier** (`SpaceSystemInformationWindowVM` — planets/resources/anomalies per system) as a
  stacked sub-screen, and a Space-tooltip on each system node. The browse label carries the card-level info for now.
- **Filters/legend toggles** (`SpaceFiltersVM`) — our categorized list is the accessible analog; revisit if the
  sighted filters prove load-bearing.
- **Navigator-Resource shop UI** — creating routes / lowering difficulty / increasing scan radius as first-class
  actions with cost/affordability. **TODO(harness): "Increase Scan Radius" (`SectorMapController.ChangeScanRadius`)
  has no located VM/button — find its UI entry point before surfacing it.**
- **Spatial star-map cursor + sonar** (the WoTR `GlobalMapCursor`/`GlobalMapSonarSystem` port) — gated on RT
  spatial audio being switched on. The flat list + verb picker is a complete navigation model without it.
- **Automatic in-transit progress announcer** — no public progress %, and milestone events (started/arrived/
  encounter) already cover the jump; only add if play-testing shows a need.

## Files
- `RTAccess/Screens/SectorMapScreen.cs` (new), `RTAccess/Accessibility/WarpEvents.cs` (new).
- `RTAccess/Screens/ScreenManager.cs` (register), `RTAccess/Main.cs` (subscribe/tick/unsubscribe),
  `RTAccess/assets/locale/enGB/ui.json` (`sectormap.*` block).

## Prior art
WoTR Global Map (`C:\modding\not-my\wotr-access\src\Exploration\GlobalMap*.cs`, `Screens\GlobalMapScreen.cs`) —
same `GameModeType.GlobalMap` mode-gate and the "drive the game's own handler, never reimplement" softlock
lesson. RT-only vs WoTR: richer passages (difficulty/duration/encounter vs binary locked), active scanning, the
Navigator-Resource economy, and the system dossier — all new here; the encounter screen is intentionally not ported.
