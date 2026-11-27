---
name: rt-object-interaction
description: "RT interactables are off-grid/continuous; interact via the game's own ClickMapObjectHandler.Interact; a11y cursor = proximity selection, not tile-footprint"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 170f62a3-be23-4b53-8f3a-a62613e8aeaf
---

**Interactable map objects live in continuous world-space, NOT slotted one-per-tile** (workflow-verified 2026-07-01,
5 source lenses + live probe of `VoidshipOfficersDeck`, high confidence). A `MapObjectEntity.Position` is its
level-authored view-transform position, off any `CustomGridGraph` cell centre by up to ~0.95 m (the cell-corner
distance). Measured: 41 interactables averaged 0.464 m off-centre; doors/skill-checks sit exactly on cell **edges**
(½ cell) and **corners** (shared by 4 tiles). Objects can also span multiple cells or occupy **zero** (off-mesh exit
pins). `GetOccupiedNodes()` is a lossy on-demand rasterization (snaps to nearest node), so a per-tile footprint test
is NOT a sound interaction key. See [[rt-world-grid]] (grid is the combat/pathfinding substrate; exploration is
real-time/continuous).

**Interaction is world-space, not grid-based.** Mouse: `PointerController.SelectClickObject` raycast → collider →
`ClickMapObjectHandler.OnClick`. Console/gamepad: `SurfaceMainInputLayer` picks nearest-in-cone from
`EntityBoundsHelper.FindEntitiesInRange(unitPos, 12.7f)`, gated by `ClickMapObjectHandler.HasAvailableInteractions`.
**Both converge on the same dispatch:** `ClickMapObjectHandler.Interact(gameObject, units, forceOvertipInteractions)`
→ `TryInteract(part, users)`. There is NO per-tile object index (nodes track only *unit* occupancy via `GetUnit()`).

**Clicking does NOT always walk the party to the object.** `interaction.SelectUnit(units)` picks ONE actor; other
selected units get `InterruptMove`. `InteractionType.Direct` → `UnitCommandsRunner.DirectInteract` (in place, no
move). `InteractionType.Approach` → `UnitInteractWithObject.ApproachAndInteract`, but short-circuits (no move) when
already within `ApproachRadius` cells (`InteractionPart.IsEnoughCloseForInteraction`). Traps force `leaveFollowers`.

**RTAccess implementation (shipped R2):** the tile cursor is a **spatial-scan overlay, not the interaction
primitive**. Object interaction is object/proximity-based, reusing the game's own dispatch verbatim (same as
WrathAccess `ProxyMapObject`):
- `ProxyMapObject.Interact()` → `ClickMapObjectHandler.Interact(view.gameObject, SelectedUnits, forceOvertipInteractions: true)`
  (`true` because a blind player has no hover overtip button — this is the console behaviour). Exits keep the
  `AreaTransitionHelper.StartAreaTransition` special-case. Used by BOTH cursor-Enter and scanner-I.
- `InteractableDescriber.InteractableAt(node)` = nearest `MapObjectEntity` within `InteractReach` (1.5 cells) of the
  cursor, gated by `HasAvailableInteractions` (+ `AreaTransitionPart`). `DescribeTile` announces that same object so
  the readout matches what Enter fires.
- Gap vs WA: no lockpick `ILockpickUIHandler` special-case for locked objects yet.

**Uniform activation (2026-07-02):** the two interact keys were inconsistent — `I` acted ONLY on the scanner review
selection, `Enter` ONLY on the nearest object to the tile cursor, they used different actionability gates, and the
`M` objects-review cycle skipped Mechanisms/Traps so those objects were reachable only via `Enter` or the Ctrl+Page
category browse (the "Home then Enter" pain). Chosen model (user): keep both keys' roles but make `I` never
dead-end. Changes: (1) `ScanItem.CanInteract` virtual (base false; `ProxyMapObject` overrides = `HasAvailableInteractions
|| AreaTransitionPart`, mirroring `InteractableDescriber.IsActionable` — ONE actionability gate for both keys);
(2) `Scanner.Interact` = selection-if-`CanInteract` (distance-agnostic, cross-area refused) → landmark→travel →
FALLBACK to `InteractableAt(MapCursor.Node ?? Anchor().CurrentUnwalkableNode)` → `scan.nothing_nearby`;
(3) `Scanner.InGroup` Objects case now includes `Mechanisms` + `Traps` (Scenery still excluded — nothing to
activate), so every interactable object is reachable by cycle + `I`; (4) `TileExplorer.InteractAtCursor` + the
scanner share the SAME localized outcome lines (`scan.interacting`/`scan.cant_interact`/`scan.nothing_nearby`).
Both keys still funnel through `ProxyMapObject.Interact`. Compiles 0/0; NOT yet in-game verified.

**Landmark handling (2026-07-02, same session):** VERIFIED in game code that a landmark (`ILocalMapMarker` /
`LocalMapMarkerPart`) is NOT mouse-clickable — marker PC views (`LocalMapMarkerPCView` + subclasses like the empty
`LocalMapExitMarkerPCView`) have no click handler; `ILocalMapMarker` exposes only read methods; the sole local-map
click path `LocalMapVM.OnClick` acts on the clicked POSITION (`state?ScrollTo:MoveSelectedUnitsToPoint`), not a
marker (entity arg = coop ping only). So travel is a landmark's ONLY action. User's chosen model: landmarks live
ONLY in the scanner's category browse, NOT on dedicated keys. Changes: removed the V/B review cycles
(`ReviewExits`/`ReviewPoi` + `Group.Exits`/`Poi` + bindings) and DELETED `LandmarkNav` entirely (`[ ] \` freed);
added a marker-sourced **"Points of interest"** category (`taxonomy.poi`, `Categories` gained a `bool Marker` field;
`CategoryList` branches to `MarkerList(refPos)` = Poi/Loot/DestinationMark/VeryImportantThing pins). Area **exits**
stay as their activatable world objects in the Exits category (marker-exits dropped). `I` on a `ProxyMarker` →
`Scanner.TravelTo` = `Geo.SnapToWalkable` (relocated from LandmarkNav) march + `MoveSelectedUnitsToPoint`, refused
in combat (`travel.combat`). Freed keys: V B [ ] \. Compiles 0/0; NOT yet in-game verified.

**Interaction OUTCOME feedback (shipped):** `InteractionEvents` (global `EventBus` subscriber, like `BarkEvents`)
voices `IPickLockHandler.HandlePickLockSuccess/Fail` → "<object>, lock picked." / "lock pick failed." (the game only
raises non-critical fails, from `SkillUseRestrictionPart`).

**Variative/locked-object CHOICE (shipped R2-deferred wave B, in-harness verified 2026-07-01):** RT has NO
`LockpickVM` — a locked/variative object (choice of skill vs Tech-Use vs Key vs Melta vs Destroy, each with a
chance %) is the generic **variative-interaction** system. `ProxyMapObject.Interact` now guards the static dispatch:
when `VariativeInteractionVM.HasVariativeInteraction(view)` it raises
`EventBus.RaiseEvent<IVariativeInteractionUIHandler>(h => h.HandleInteractionRequest(view))` (the static
`ClickMapObjectHandler.Interact` SKIPS that branch — it lives only in mouse `OnClick` / overtip), and a new
`VariativeInteractionScreen` (Exclusive modal, layer 24; mirrors `SurfaceDynamicPartVM.VariativeInteractionVM.Value`
→ `Variants` as `ProxyActionButton`s, label incl. chance + resource + "unavailable", activate = `variant.Interact()`,
Back = `vm.Close()`) surfaces the choice. Mirrors WrathAccess's lockpick guard. See [[rt-tile-explorer]].

Remaining feedback gaps (bigger, not clean EventBus hooks): **search-point / skill-check** interactions
(`InteractionSkillCheckPart`) resolve through the rule system + `ISkillCheckInteractionTrigger` fact-callback → would
need a Harmony rule-hook; **loot containers** raise `ILootInteractionHandler.HandleLootInteraction` which opens a loot
WINDOW → needs the parallel loot-window UI (service-window gap).

Ties to [[rt-tile-explorer]], [[rt-inspect-contract]], [[wotr-access-prior-art]].
Plan/details: docs/plans/coupled-charting-hopper.md §7 (R2).
