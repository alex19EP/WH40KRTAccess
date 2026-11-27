# orbital-listing-wilkes — System map accessibility (space, wave 1 of 2)

**Scope decided 2026-07-04 with maintainer.** System map FIRST (it's the surface the current
save can reach); sector map (M4) reuses the skeleton later; space combat stays deferred
(HOOKMAP subsystem 7). Research basis: `docs/space-maps-ui-research.md` (all game-API claims
cited there). Design decisions locked with the maintainer:

- **Browse shape: flat list, nearest-first** (one tab stop, type spoken in the label). No
  category tree, no cycle keys in v1.
- **Speech only in v1.** Proximity cues are spoken, queued. Earcons/sonar join the deferred
  audio pass (echoing-charting-lovelace) after the ear test.

Hard laws in force: graph-native screens only; drive the game's own commands/VMs; browse
label mirrors the card, tooltip detail on Space; visual parity (never reveal what sighted
players can't see); localize everything (`systemmap.*`, `exploration.*` in
`assets/locale/enGB/ui.json`); interrupt only on keypress provenance.

---

## M0 — harness spike: DONE 2026-07-04 (live on Chapter1System, save untouched — never saved)

1. **Fog: RESOLVED — no fog on the system map.** `FindAnyObjectByType<FogOfWarArea>()` = null
   in Chapter1System; FogProbe reports "no active FogOfWarArea"; the ship's revealer exists but
   has nothing to reveal into. Screenshot confirms the ENTIRE system fits on one screen with
   every `IsInGame` object's overtip visible at all times. **Full-list parity: gate membership
   on `IsInGame` (+ `!HideInUI` for anomalies) only.** Belt-and-braces: keep a FogProbe check
   that no-ops when no area exists, in case a later system ships a fog volume.
   Visual confirmations: star has NO overtip (sun visual only); unscanned planet renders the
   unknown-glyph label; anomalies show names unscanned; quest markers (yellow triangles) show
   on UNSCANNED objects — `HasQuest` is not scan-gated, matches the VM research.
2. **Units + plane: RESOLVED.** All positions Y=0 → XZ plane, 8-point `Geo.Bearing` reuse OK.
   Fixture distances 11–91 units; map ~130×90 units (click clamp z∈[-50,40]). Orrery-schematic
   scale — speak bearing + rounded integer via a localized `systemmap.units` phrase; tune at
   ear pass.
3. **Drive points: ALL VERIFIED LIVE.**
   `InteractWithStarSystemObjectGameCommand(null, pos)` moved the ship to empty space;
   `MoveShip(entity, VisitType.MovePlayerShip)` flew to the unscanned planet, landed, and the
   tablet auto-opened (`IsExploring` flipped true); `ExplorationScanButtonWrapperVM.Interact()`
   ran the full two-phase scan — `IsScanned` flipped, the GroundOperation POI promoted
   `NotExplored→NeedInteraction`, header revealed (name + world type + Aestimare + tithe).
   VM tree paths confirmed: `RootUiContext.SpaceVM.StaticPartVM` fields `ExplorationVM`,
   `ZoneExitVM` (fields IsWarpJumpAvailable/IsExitAvailable/ShipIsMoving/
   HasAccessStarshipInventory; warp=False here — area-gated, chapter 1), `SystemScannerVM`
   (ObjectsList had 3 anomalies; `AnomaliesTypesText` is a plain `List<string>`),
   `SystemMapNoisesVM` (flags live: `PoiIsNear=true` while parked at the scanned planet).
   Eval gotcha: extension methods need `using Kingmaker.GameCommands;` sent as its OWN eval
   first (REPL state persists); bare expressions can't sit inside `if` blocks.
4. **Keybinds: DUMPED (760 rows; StarSystem-mode distinct set recorded).** Load-bearing:
   **arrows + WASD = camera pan, Q/E rotate** (arbitrate arrows while our screen has focus);
   **bare Space = Pause** (and SkipBark/EndTurn contexts) — our Space detail verb must claim
   it; Tab = object highlights (hold); H=Hold, F=FollowUnit, T=SpeedUpEnemies, P=combat log,
   R=FlipZoneStrategist; F5/F8 quick save/load, F11 screenshot. Ctrl+letter window openers
   confirmed LIVE in this mode (Ctrl+C/I/J/M/L/Y/V/B/N). **`OpenAugmentations = bare U` — NOT
   relocated by GameKeybinds** (U is on our free-keys list; add it to the relocation or avoid U).
5. **Toast/log dedup, partial:** landing + scan produced exactly ONE mod line — LogTap voicing
   the XP log entry ("Получен опыт: 50"). So the XP notification needs NO tap. Scan completion
   itself was silent (our announcer's job). Encyclopedia/colony/mining toasts didn't fire this
   session — decide tap-vs-mute when first observed live.
6. **Bonus finding:** this save's next step IS a GroundOperation POI ("Райкади Филия,
   планетоид-тюрьма") — the party-picker screen is a hard M2 dependency, not a nice-to-have.
   Game locale is Russian — pass-through strings arrive in Russian; mod strings stay on our
   own localization path as usual.

## M1 — `SystemMapScreen` (mobility) — SHIPPED 2026-07-04, live-verified

Implemented as designed (details below). Files: `Screens/SystemMapScreen.cs`,
`Accessibility/SpaceEvents.cs`, registrations in `ScreenManager`/`Main`, `systemmap.*` locale
block, `InGameScreen.WindowLabel/WindowEnabled` + `InteractableDescriber.Compass8` made
internal for reuse. Deployed and verified in-game on Chapter1System (maintainer drove the
keyboard; speech log): all 3 stops announce, 7 objects nearest-first with card-mirroring
labels (anomaly type via the game's localized `GetAnomalyTypeName`, "???, star" unscanned,
quest flags, bearings+units, n-of-7), planet Space-tooltip renders the game's template
("Не исследовано / Требуется сканирование"), Status (research %, Profit Factor, resources,
ship state), Actions (Warp jump correctly disabled in ch.1, game-localized window openers with
faithful gates — Colony/Augmentations disabled), Escape blurs to the map.
Not yet exercised live: FlyTo announce + movement-event suppression, scan summary, proximity
cues, research-change announce. Landing still opens a SILENT tablet — that is M2.

New graph-native base context `ctx.systemmap`, `RTAccess/Screens/SystemMapScreen.cs`.
`IsActive()`: `Game.Instance?.CurrentMode == GameModeType.StarSystem`. Declares
`InputCategory.WorldMap` (pre-staged, zero refs today). WA lesson encoded: *act* only when
`CurrentMode == StarSystem` (mode-stack vs current-mode — dialogs/book events over the map
must not trigger map verbs).

Tab stops (Build order = Tab order):

1. **Objects** — flat list over `Game.Instance.State.StarSystemObjects.All` where `IsInGame`
   (anomalies minus `Blueprint.HideInUI`). Order frozen nearest-first from ship position at
   area entry (`_order` built once, WA pattern); live set membership; new anomalies appended.
   ControlId = entity unique id. Labels (live, mirror the overtip card):
   - Planet: `name | systemmap.unknown_planet` (gated `IsScanned||IsScannedOnStart`) + type
     word + flags in card order: colony, quest, rumour, resources, extractor,
     "N points of interest" (`HasPoi` + PoiNamesList count), `systemmap.fully_explored`
     when the game dims it (IsFullyExplored state), + bearing/distance from ship.
   - Generic SSO: name or `"???"` (unscanned) + type word (star/asteroid/comet/cloud/station —
     visually obvious from the 3-D model, parity-safe) + POI flag. Note: the game's hover hint
     leaks the real name unscanned (`OvertipSystemObjectPCView` sets it unconditionally) — we
     deliberately do NOT copy that leak; follow the card.
   - Anomaly: name + `systemmap.anomaly` + type word from `AnomalyObjectType` mapped like the
     icon (Loot→loot, Enemy→enemy, rest→unknown) + quest flag.
   - **Enter** → `GameCommandQueue.MoveShip(entity, VisitType.MovePlayerShip)` + interrupt
     speak `systemmap.traveling_to {name}`.
   - **Space** → full detail: `TooltipTemplateSystemMapPlanet` rendered through
     TooltipViewScraper + the VM hint strings (QuestObjectiveName / RumourObjectiveName /
     PoiNamesList / Resources / ExtractorResources bricks,
     `UIUtilitySpaceColonization.GetColonizationInformation` for colonies).
   - Anomaly context (Backspace or onContext): `OpenAnomalyInfo()` →
     `IAnomalyUIHandler.OpenAnomalyScreen`.
2. **Status** — read-only nodes: system name + research % (cache from
   `IStarSystemMapResearchProgress`), Profit Factor (`Player.ProfitFactor.Total`), colony
   resources (`SystemMapSpaceResourcesVM` rows), current date (`TimeRewindVM` fields),
   warp-jump availability line.
3. **Actions** — Warp jump (`ZoneExitVM.ExitToWarp()`, node present per `IsWarpJumpAvailable`),
   Stop ship (`StopStarSystemStarShip()`), Back to bridge (`ExitToShip()`), window openers
   Ship/Cargo/Colony (reuse `InGameScreen.BuildWindows` gates — extract to a shared helper
   rather than duplicating).

**Escape** mirrors the game: ship moving → stop ship; else esc menu handler.

**`Accessibility/SpaceEvents.cs`** (announcer, queued speech):
- `IStarSystemShipMovementHandler` started/ended → `systemmap.traveling` /
  `systemmap.ship_stopped` (suppress the started-line when our Enter already spoke it).
- `IStarShipLandingHandler` → arrival is announced by the tablet screen opening (M2), not here.
- `IScanStarSystemObjectHandler.HandleScanStarSystemObject` → scan summary: revealed name,
  POI count, resource count.
- `IStarSystemMapResearchProgress` → `systemmap.research {pct}` on change.
- `SystemMapNoisesVM` flags (anomaly/POI/resources near, 10-unit radius) → spoken once on
  rising edge, e.g. `systemmap.near_anomaly`.
- Toast taps per M0 finding #5: `IColonyNotificationUIHandler`, `IMiningUIHandler`,
  `IEncyclopediaNotificationUIHandler` — voice OR add to LogTap's OwnedElsewhere, never both.
- Time: never narrate the 2880× date spin; date lives in Status.

## M2 — `ExplorationScreen` (the landing tablet; makes M1 useful) — SHIPPED 2026-07-04 (c3f60e0)

**Harness-verified live** (Chapter1System): land → tablet announces ("???, exploration",
5-node header) → scan via the game's button (completion summary, name reveals live "???"→
"Райкади Филиа", focus re-homes when the scan button vanishes) → POI list ("Точки интереса:"
+ quest flag) → ground-op POI opens GroupChangerScreen (protagonist "required, disabled") →
Escape backs out of picker → tablet → map cleanly; AnomalyScreen opens/announces/closes.
**Compile-only (no ch.1 fixture): StatCheckLoot, Expedition, colony sections, miner install**
— verify when the campaign reaches them.

Shipped as five screens (compile-clean; harness verification below): `ExplorationScreen`
(layer 9 Exclusive, mirrors the game's section machine by reading each wrapper VM's
`ActiveOnScreen`), `AnomalyScreen` (layer 9; the VM keeps no shown-state so the screen tracks
its Show/Hide ReactiveCommands, subscribed lazily from IsActive), `StatCheckLootScreen`
(layer 26; serves BOTH the POI instance — view-owned, located via
`ExplorationStatCheckLootPCView.ViewModel` — and `AnomalyVM.StatCheckLootAnomalyVM`),
`ExpeditionScreen` (layer 26; crew-size slider via `SetPeopleCount`, view-owned VM),
`GroupChangerScreen` (layer 27, Surface OR Space `GroupChangerContextVM`; Enter moves a
member via the card's own Click command, Confirm replicates the view's net-gated
`AcceptChangeGroup`, Escape honors the VM's `CloseCondition` — a required picker refuses,
spoken). Locale: `exploration.* / statcheck.* / expedition.* / groupchanger.*`.
Gotchas encoded: ControlId equality is by structural key alone (loop nodes need unique key
suffixes); `PushContext` labels are identity-bearing (keep them static); exploration
component VMs are VIEW-owned (find the PC view, read `.ViewModel`).

Graph-native, `RTAccess/Screens/ExplorationScreen.cs`, layered above ctx.systemmap
(fullscreen-window layer, Exclusive). `IsActive()`:
`SpaceStaticPartVM.ExplorationVM.IsExploring` (+ StarSystem mode). Announce on open:
object name + section. Mirrors the game's section machine
(`ExplorationUISection {NotScanned, Exploration, Colony, ColonyProjects}`) — read the live
section, render matching stops:

- **NotScanned**: header (name/`???` + Tithe Grade / Aestimare / World Type — VIEW-owned,
  read blueprint directly, `exploration.undetermined` until scanned) + one **Scan** button →
  `ExplorationScanButtonWrapperVM.Interact()` (the game's two-phase command; completion
  announced by SpaceEvents' scan summary).
- **Exploration**: POI list from `ExplorationPointOfInterestListVM.PointsOfInterestVMs`
  (label: Name + explored/interactable state + quest/rumour flags; Enter →
  `poiVM.Interact()` — its warning toasts are already voiced by WarningReader); resource list
  from `ResourcesVMs` (label: name, count, mining state; Enter → `resourceVM.Interact()` —
  install/remove miner through the game's own confirm boxes, which MessageBoxScreen covers);
  miner counter node.
- **Colony**: stats (Efficiency/Contentment/Security + negative-modifier flag), trait chips,
  event chips (Enter starts the event dialog in-system — DialogueScreen takes over), projects
  button → **ColonyProjects**: rank-tiered project list (requirements with met/unmet, rewards,
  build progress; Enter → `ColonyProjectsVM.StartProject()`), rewards-since-last-visit popup
  (`ColonyRewardsVM`) as a modal stop.
- **Sub-modals** (stops appearing on their `ShouldShow`): Expedition
  (`ExplorationExpeditionVM` — people-count adjuster 1..Max, reward-tier readout with
  unlocked marker, Send); StatCheckLoot (`StatCheckLootVM` pages — per-stat unit card,
  best-unit preselected, SwitchUnit list, CheckStat; resulting loot window is our existing
  LootScreen, which already resolves Surface-OR-Space).
- **Known gap to cover here**: GroundOperation POIs open the party picker
  (`IGroupChangerHandler`) — no RTAccess screen exists for it. Add a minimal GroupChanger
  screen (WA has `GroupChangerScreen.cs` prior art) or verify the picker is reachable via an
  existing screen before shipping M2.
- Close: Escape → `GameCommandQueue.CloseExplorationScreen()`.

Also M2: small **AnomalyScreen** (or a modal stop on SystemMapScreen) mirroring `AnomalyVM`
(name, description, explored state, Visit button, its StatCheckLoot child).

## M3 — polish — SHIPPED 2026-07-04 (7e5fbb6)

Done: **Colony Management window** (`ColonyManagementScreen`, layer 10; colony radio list →
page content) rendering through the new shared **`ColonyNodes`** builders (stats/traits/
events/built list/projects window/rewards popup with Collect = the VM's own `HandleHide`
claim path) — ExplorationScreen refactored onto them and gained the rewards popup;
**anomaly-radar readout** in the SystemMapScreen Status stop (contact count + the game's
`AnomaliesTypesText`, live-verified); **OpenAugmentations bare U → Ctrl+U** via GameKeybinds
(live-verified in StarSystem mode; U is a free mod letter again).
Still open for M3.x: ear-pass phrasing fixes from the maintainer; notification dedup when
colony/mining/encyclopedia toasts are first observed live; colony content live-verification
once a colony exists (compile-only today).

## M4 — sector map (sketch only; own plan later)

> **Superseded — now built out in `warp-sector-map-accessibility.md`** (implemented from the decompile,
> pending in-harness test once warp travel is quest-unlocked). The sketch below is kept for history.

Same skeleton: Objects list → systems (visited/unvisited gating preserved: unvisited =
"Unknown system" + quests/rumours only); Enter → navigator popup as modal stop
(Travel / Create Way / Lower danger with costs + Navigator resource, via
`SpaceSystemNavigationButtonsVM` methods); Space → `TooltipTemplateGlobalMapSystem` content;
per-system info window via `HandleShowSystemInformationWindowConsole(entity)` as the review
detail; Scan on the bottom HUD; warp-travel events (`ISectorMapWarpTravelHandler`) + encounter
dialogs (DialogueScreen/BookEventScreen already cover); Status = Navigator resource,
current system. Pre-read: warp-encounter → space-combat handoff
(`CombatRandomEncounterController`).

## Files

- `RTAccess/Screens/SystemMapScreen.cs` (new), `RTAccess/Screens/ExplorationScreen.cs` (new),
  `RTAccess/Screens/AnomalyScreen.cs` (new, small), `RTAccess/Accessibility/SpaceEvents.cs`
  (new), shared window-opener helper extracted from `InGameScreen`.
- `ScreenManager.Initialize()` registrations; `InputCategory.WorldMap` wiring.
- Locale: `systemmap.*`, `exploration.*` blocks in `assets/locale/enGB/ui.json` (+ settings
  keys if any). The pre-staged WotR `worldmap.*` keys stay untouched (they encode the wrong
  model; retire when the sector map lands).

## Verification

M0 findings recorded back into this plan. M1/M2 in-harness (dev server) + maintainer manual
test steps per the shared-harness rule: browse objects list, fly to an unscanned planet,
scan, interact with each POI type present, install a miner, warp out. Watch `speech_log.txt`
for double-speak (toasts vs LogTap) and for interrupt-provenance violations.

## Open risks

- ~~Fog gating~~ RESOLVED by M0: no fog on the system map; gate on `IsInGame` (+`!HideInUI`).
- ~~Distance phrasing~~ RESOLVED by M0: XZ plane, bearing + integer units phrase.
- Input arbitration in StarSystem mode is wider than expected (M0 #4): arrows/WASD camera,
  bare Space = Pause, Tab = highlights — the screen must claim arrows/Space/Tab via
  KeyboardArbitration exactly while focused, and `OpenAugmentations = bare U` needs the
  GameKeybinds treatment (or our verbs avoid U).
- `SpacePointMarkersVM` and console-only view variants unread — if the PC HUD shows point
  markers we missed, fold into M3.
- The tablet auto-opens on landing (game behavior); ExplorationScreen must not fight
  TransitionScreen on `MultiEntranceObject` landings (those open the Transition window
  instead — already covered by TransitionScreen).
- Warp-jump availability couldn't be exercised (area-forbidden in Chapter1System) — verify
  `ExitToWarp()` in a later-chapter save before calling M1 done.
