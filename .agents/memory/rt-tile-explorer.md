---
name: rt-tile-explorer
description: "The always-active exploration cursor — WA two-cursor coupling (movement cursor + review selection), the move-to gotcha, and the tile-cursor-is-a-scan-overlay principle"
metadata: 
  node_type: memory
  type: project
  originSessionId: 2f822d1e-f036-4c1c-bca4-a9d4bc347439
---

The exploration tile cursor reads ARBITRARY grid cells on the square 1.35 m pathfinding grid ([[rt-world-grid]],
[[rt-cover-los]]). It is **always active** (no toggle) and coupled with the scanner, WrathAccess-style, but keeps
stepping RT's discrete tiles (no continuous glide). Files: `Accessibility/TileExplorer.cs` (cursor state + input +
gating + camera), `Exploration/MapCursor.cs`, `Exploration/Scanner.cs`; readout composed by
`InteractableDescriber.DescribeTile(CustomGridNodeBase, MechanicEntity)`. Plans:
`docs/plans/coupled-charting-hopper.md`, `docs/plans/abundant-mixing-diffie.md`.

**TWO-cursor coupling (WA "option one" — do NOT collapse to one):** (1) **movement cursor** (`MapCursor`) = the tile
stepped by arrows; the sort/measure origin (`ScanFrom = cursor`), and what overlays/audio/targeting/move-to read.
(2) **review selection** (`Scanner.Selected`) = the scan item enumerated by PageUp/Dn + review-group keys. **Review
NEVER moves the cursor** — the cursor moves only on arrows or an explicit Home/Slash plant-on-selection
(`MapCursor.Set`), so `ScanFrom` is stable and there's no sort ping-pong. (An interim draft that collapsed the two —
browse moves cursor — deleted WA's "look around while holding position" and was reverted.) `MapCursor.Set(Vector3)`
null-guards (`GetNearestNodeXZ` is `[CanBeNull]`) and keeps the previous node so planting onto an off-grid item can't unplant.

**Keys** (registered `InputCategory.Exploration` actions, not raw polling; verb PAIRS cursor / selection):
- **arrows** = step N/E/S/W (primary, shadowable); **Shift+arrows** = step (shadow-immune secondary).
- **Enter/KeypadEnter = `cursor.interact`** (nearest reachable interactable) / **`I` = interact review SELECTION**.
- **`'` (Quote) = inspect CURSOR unit** / **`Y` = inspect review SELECTION unit** (via [[rt-inspect-contract]]).
- **Backspace = `cursor.move_to`** (move party to cursor; aiming → Cancel) / **Home + Slash = plant cursor on selection**.
- **C** = recenter cursor on party; **Delete** = re-announce cursor; **X** = where-am-I; **O** = re-speak selection;
  **V** (exits) / **B** (POI) = landmark review groups (`ProxyMarker` over `LocalMapModel.Markers`, same set as
  `LandmarkNav` `[`/`]`, but reversible + cursor-relative + Home-plantable). All announces interrupt (key-driven).
  Reused game keys (C/Backspace/Enter/'/Y/Home) rely on FOCUS MODE suppressing the game's World-mode hotkeys; fall
  back to free keys O/U/Z if any leaks. `C`/`Delete` are guarded on `Navigation.HasFocus` (no `ui.*` twin) so they
  don't leak through HUD focus; `Shift+arrows` stay shadow-immune. NEVER bind bare **Insert** — it's NVDA's modifier
  key (NVDA eats the keydown before the game sees it).

**MOVE-TO gotcha** (`TileExplorer.MoveToCursor`, one guarded helper):
- Exploration → `UnitCommandsRunner.MoveSelectedUnitsToPoint((Vector3)node.position)` (formation-aware, the canonical
  click-to-move; says "Moving party."/"Moving <name>." reading live `SelectedUnits`).
- Combat (TB) → do NOT use `MoveSelectedUnitsToPoint` (its TB branch keys off `UnitPathManager.Instance.CurrentNode`
  = the mouse pointer, refreshed every frame → wrong tile). Build the command like ClickUnitHandler:
  `unit.TryCreateMoveCommandTB(new MoveCommandSettings{Destination=(Vector3)node.position, DisableApproachRadius=true},
  false, out status)` → non-null `unit.Commands.Run(cmd)` else speak `status`
  (NotEnoughMovementPoints/DestinationUnreachable/CannotMove). `unit = TurnController.CurrentUnit as BaseUnitEntity`.
  **But gate on the engine's own turn guards first**: `UnitCommandsRunner.MoveSelectedUnitToPointTB` THROWS on
  wrong-turn / non-single-select (`:260-268`); the hand-rolled `TryCreateMoveCommandTB` bypasses them, so without a
  guard Backspace on an enemy turn would command the enemy / falsely say "Moving". Gate on `IsPlayerTurn` + selected ==
  CurrentUnit && controllable; TB move-to also gets a **two-step confirm** (commits, spends MP, no preview). `Move()`
  clears `_armedNode` on every step (else step-away-and-back within the confirm window commits on one press). Types:
  `MoveCommandSettings`+`UnitHelper.MoveCommandStatus` in `Kingmaker.UnitLogic`; `UnitCommandsRunner` in
  `Kingmaker.Controllers.Units`.

**The tile cursor is a spatial-scan OVERLAY, not the object-interaction primitive.** Interactables are off-grid /
continuous; interaction is world-space via the game's own `ClickMapObjectHandler.Interact` — see
[[rt-object-interaction]]. So `cursor.interact` (Enter) hits the NEAREST interactable within reach (proximity +
`HasAvailableInteractions` gate), NOT the tile-footprint occupant; `ProxyMapObject.Interact` dispatches through
`ClickMapObjectHandler.Interact(view, SelectedUnits, forceOvertipInteractions:true)` (both cursor-Enter and
scanner-I); `DescribeTile` always announces the nearby object so the readout == what Enter fires. Variative
interactions (lockpick) route through `IVariativeInteractionUIHandler` + the Exclusive `VariativeInteractionScreen`.

**Grid mechanics:** cursor = a `CustomGridNodeBase`; graph via `node.Graph as CustomGridGraph`; step with
`grid.GetNode(x±1,z±1)` (null out of bounds → "Edge"). Anchor = `SelectionCharacter.SelectedUnit.Value ??
Player.MainCharacterEntity`; its node = `anchor.CurrentUnwalkableNode`. World pos = `(Vector3)node.position`; camera
follow `CameraRig.Instance.ScrollTo`, gated by the `exploration.camera_follow` BoolSetting (default On; review cycles
never scroll). Readout order (parts omitted if empty): occupant [`node.GetUnit()` → name + enemy/ally
(`Faction.IsPlayerEnemy`) > map object via `EntityBoundsHelper.FindEntitiesInRange((Vector3)node.position,
GridCellSize)`.OfType<MapObjectEntity>() filtered by `GetOccupiedNodes().Contains(node)` → name+verb >
`DestructibleEntity.FindByNode` → "obstacle"] ELSE `node.Walkable`?"clear":"wall"; then per-cardinal-edge cover via
`LosCalculations.GetCellCoverStatus(node, dir).CoverType` (dirs N/E/S/W = 2/1/0/3); then relative offset
"N east/west, M north/south" / "here". Party formation restore = `party.select_all` (Ctrl+A) →
`SelectionManagerBase.Instance.SelectAll()`.

**Per-cell area-effect containment = `CoveredNodes`, NEVER the metric shape (2026-07-12, both fixed).** "Is this
tile in the hazard?" must test `ae.CoveredNodes.Contains(node)` — the quantized pattern the sighted decal paints
AND the game's pass-through mechanic tests (`RuleCalculatePassedAreaEffects`); `ScriptZoneCylinder.CoveredNodes`
quantizes `(int)(Radius/nodeSize)` so a 3 m cylinder paints only 2 cells — the metric `Shape.Contains`/`ScanItem
.Contains` disc over-reports a rim of unpainted cells, and a directional pattern (cone) degrades to its bounding
circle. `PathInfo.HazardWarning` was fixed first (user-reported false positives); `InteractableDescriber.AppendZones`
(the tile-cursor hazard line) had kept the shape test — now routed through `ProxyAreaEffect.Covers(node)` (null-shape
guard: `CoveredNodes` throws pre-shape). Scanner distance/bearing keep the metric shape on purpose (edge-distance
approach guidance, not containment).

**GOTCHAs:** `GraphNode`/`NavGraph`/`node.position`/`.Walkable`/`.Graph` live in **AstarPathfindingProject.dll** — that
reference had to be ADDED to RTAccess.csproj (not matched by the Kingmaker*/Code* wildcards). `MechanicEntity` is in
`Kingmaker.EntitySystem.Entities` (NOT `Kingmaker.Mechanics.Entities`). Grid-rotation edge case: arrow→axis assumes a
world-aligned grid; the bearing always uses the true world delta. RT has NO RoomMap for WA's room-scoped exits — port
area-scoped via `LandmarkNav`/`LocalMapModel.Markers` instead ([[rt-room-classifier]]). Related: [[rt-combat-plan]]
(TB targeting/move rides this cursor), [[rt-visual-parity]] (fog-gate `DescribeTile`).
