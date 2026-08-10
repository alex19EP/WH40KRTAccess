# The Local Map window — what sighted players get (2026-07-24)

Teardown of the game's Local Map service window ahead of making it accessible. All facts are read from
the decompiled sources; nothing here was confirmed in a live session yet.

Sources: `Kingmaker.Code.UI.MVVM.VM.ServiceWindows.LocalMap.{LocalMapVM,LocalMapLegendBlockVM,
LocalMapLegendBlockItemVM}`, `…LocalMap.Markers.*`, `…LocalMap.Utils.{LocalMapModel,ILocalMapMarker,
LocalMapMarkType}`, `…View.ServiceWindows.LocalMap.{LocalMapBaseView,PC.LocalMapPCView,
Common.Markers.*}`, `Kingmaker.Visual.LocalMap.WarhammerLocalMapRenderer`,
`Kingmaker.View.MapObjects.{LocalMapMarker,LocalMapMarkerPart,LocalMapMarkerSettings}`,
`Kingmaker.View.UnitLocalMapMarker`, `Kingmaker.Blueprints.Root.Strings.UILocalMapTexts`.

## Entry points

- The game's `"OpenMap"` keybind → `ServiceWindowsVM.HandleOpenLocalMap` (`ServiceWindowsVM.cs:148,273`).
  Bare `M` in the stock keymap, so `GameKeybinds` relocates it to `Ctrl+M`.
- `ServiceWindowsVM.HandleOpenWindowOfType(ServiceWindowsType.LocalMap)` — what the HUD button and the
  mod's own windows list (`src/RTAccess/Screens/InGameScreen.cs:166`) call.
- Raises `FullScreenUIType.LocalMap` and `IFullScreenLocalMapUIHandler` (which only the party views
  listen to, to hide portraits).

Not available in every area: the window renders `Game.Instance.CurrentlyLoadedAreaPart`, and ship decks
route the map key to the Transition window instead (already covered by `TransitionScreen`).

## What is on screen

**Title** — `LocalMapVM.Title` = `Game.Instance.State.LoadedAreaState.Area.Blueprint.AreaDisplayName`,
rendered through a scrambling TMP effect. The window's own label is `UIStrings.MainMenu.LocalMap`.

**The map image** — `WarhammerLocalMapRenderer.Draw()` bakes a top-down silhouette of the area's
geometry into a `RenderTexture` sized `LocalMapBounds.size.{x,z} * 5` (5 px per world unit), tinted
`MainColor` green with a lighter border, and hands back `LocalMapFowScaleOffset` so the shader masks it
by fog of war. It is a picture — there is no text, no room decomposition, no labels baked into it. It
redraws every frame (`LocalMapVM.OnUpdateHandler`).

**A compass frame** — `CompassAngle` = `CameraRig` yaw minus the area's `LocalMapRotationDeg`; the frame
rotates counter to it so north stays put. The whole map image is also rotated by the area's authored
`LocalMapRotationDegree` (0/90/180/270).

**Markers** laid over the image (`LocalMapBaseView.AddLocalMapMarker`), one prefab per
`LocalMapMarkType`, each with a plain hover hint = the marker's `Description`
(`LocalMapMarkerPCView.BindViewImplementation`, last line). Markers that fall outside the visible
viewport are clamped to the edge and grow a direction arrow (`ShowMarkersAlways` / `ShowHideArrow`).

**Right-hand buttons** (`LocalMapPCView`), each with a localized hint from `UILocalMapTexts`:
zoom in (`ZoomMapPlus`), zoom out (`ZoomMapMinus`), centre on Rogue Trader (`CenterOnRogueTrader`), and a
legend toggle (`ShowLegend`/`HideLegend`) that reveals `LocalMapLegendBlockVM` — an icon+description list
authored in `BlueprintUILocalMapLegend.LocalMapLegendBlockItemInfo`, i.e. "this shape means loot".

## What the player can *do*

`LocalMapVM.OnClick(viewportPos, state, entity)`:

- **Left click / left drag** (`state: true`) → `CameraRig.Instance.ScrollTo(worldPos)` — move the camera.
- **Right click** (`state: false`) → **`UnitCommandsRunner.MoveSelectedUnitsToPoint(worldPos)`** — a real
  move order to that world point. This is the one genuinely load-bearing verb in the window.
- Middle-drag pans, scroll wheel zooms, and in co-op a click pings.
- Clicks outside the area bounds are snapped to `LocalMapBounds.ClosestPoint`.
- The centre-on-RT button additionally calls `ScrollCameraToRogueTrader()`.

Note the markers themselves are **not** clickable targets — `LocalMapMarkerPCView` handles no click; the
click is read off the map surface and the marker only contributes its `Entity` for the co-op ping. That
matches the note already in `src/RTAccess/Exploration/ProxyMarker.cs`.

## The marker model (the part worth mirroring)

`LocalMapModel.Markers` is a static `HashSet<ILocalMapMarker>` that parts add/remove on attach/detach.
`ILocalMapMarker` = `GetMarkerType()`, `GetDescription()`, `GetPosition()`, `IsVisible()`,
`IsMapObject()`, `GetEntity()`.

`LocalMapMarkType`: `PlayerCharacter`, `Exit`, `VeryImportantThing`, `Loot`, `Poi`, `Unit`,
`DestinationMark` (+ `Invalid`, pruned each update).

`LocalMapVM.SetMarkers` composes the displayed set from three sources:

1. **Every `LocalMapModel.Markers` entry** whose entity is non-suppressed and whose position is
   `IsInCurrentArea` → `LocalMapCommonMarkerVM`. Loot markers with an empty description fall back to the
   game's generic "Loot" string. Contributors: `LocalMapMarkerPart` (map objects, from the
   `LocalMapMarker` component / `LocalMapMarkerSettings`), `UnitLocalMapMarker` (unit views — which flip
   to `Loot` once `IsDeadAndHasLoot`), plus the `MarkOnLocalMap` game action and the `AddLocalMapMarker`
   unit fact.
2. **Party and pets** (plus `RemoteCompanions` in `CapitalPartyMode`), alive, in game, in area → a
   `LocalMapCharacterMarkerVM` (name, portrait, `IsSelected`) *and* a `LocalMapDestinationMarkerVM` (the
   pending move destination, visible only while `ClickPointerManager.UnitMarksLocalMap` holds one).
3. **Remembered non-player units** from `MainCharacterEntity.CombatGroup.Memory.UnitsList` that are
   `IsVisibleForPlayer`, alive and in area → `LocalMapUnitMarkerVM` (name, `IsEnemy`). `OnUpdateHandler`
   adds and removes these live as visibility changes.

### Visibility gates — reuse these verbatim

The window is already parity-safe by construction, and its gates are exactly the ones our visual-parity
law wants (see [[rt-visual-parity]]):

- `LocalMapMarkerPart.IsVisible()` = `!Hidden && Owner.IsRevealed && Owner.IsInGame &&
  ((MapObjectEntity)Owner).IsAwarenessCheckPassed`. Revealed **and** the perception check passed.
- `UnitLocalMapMarker.IsVisible()` = the view's `IsVisible`.
- Enemy markers additionally gate on `IsVisibleForPlayer` and are dropped the moment it goes false.

So a screen built on `MarkersVm` + these predicates cannot leak anything a sighted player can't see —
no extra `FogProbe` gating needed for the marker list itself.

## What RTAccess already covers

`ProxyMarker` (`src/RTAccess/Exploration/ProxyMarker.cs`) wraps `ILocalMapMarker` into the scanner's "points
of interest" category, reading the same set, filtered in `ScannerDump` to `Poi` / `Loot` /
`DestinationMark` / `VeryImportantThing`, sorted from the cursor, with travel-to on the scanner's
`I` key. `InteractableDescriber.DescribeMarker` already produces
"<description>, <type>, <distance>, <bearing>", and `MarkerTypeLabel` localizes all six types.

Party and hostile positions are **also** already reachable: `taxonomy.units.party` and
`taxonomy.units.enemies` are existing scanner categories sourced from the `WorldModel` registry (they
use a different visibility gate than the map's — see the open item in [[rt-scanner-consistency]] — but
the content class is the same). Of everything the window draws, exactly one datum had no scanner
equivalent: the **pending move destination** per party member.

So what the window adds over the scanner is: the area title, pending move destinations, exits as pins
(the scanner surfaces exits as real activatable objects instead), the legend, and click-to-move.

## What was built (2026-07-24)

**`src/RTAccess/Screens/LocalMapScreen.cs`** — service window, layer 10, registered in `ScreenManager`.
Reads `LocalMapVM.MarkersVm` directly rather than re-deriving from `LocalMapModel.Markers`, which is
both the "read the game's own VM" law and free visual parity (all the gates above are already applied
to that collection). Six Tab-stop zones per [[prefer-tab-stops-per-zone]]: overview (area title +
counts), party (name, selected, position, and "heading to …" when a destination pin exists), landmarks
(nearest first), hostiles (nearest first), legend, actions.

Every pin row carries the window's one real verb — order the selected units to that point. It routes
through a new `Scanner.TravelToPoint(Vector3, string)`, which `TravelTo(ScanItem)` now also delegates
to, so the combat refusal, the `Geo.SnapToWalkable` snap and the spoken confirmation are shared with
the landmark cycle's `I` key rather than reimplemented. Distances and bearings come from
`InteractableDescriber.DirectionAndDistance`, so the map and the scanner phrase a pin identically.

Zoom in/out was deliberately **not** mirrored: it is presentation of a texture we never render, and a
fake control that changes nothing audible is worse than an absent one. Centre-on-Rogue-Trader is kept
because it genuinely moves the camera.

**`ProxyUnit.Detail`** gained the one genuinely-new datum: a party member's pending destination, read
from the same `ClickPointerManager.UnitMarksLocalMap` the map's destination pins use, phrased relative
to the unit ("heading to 8 tiles, north") since `Detail` has no access to the scan origin.

Compile-verified only — not yet deployed or exercised in a live session.
