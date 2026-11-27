# Tile Explorer — keyboard grid cursor with spoken tile readout

## Context

Blind players have no way to understand the spatial layout of a surface area: where walls, cover,
doors, loot, and units are relative to the party. The existing nav features ([[ExplorationNav]] cycles
nearby interactables, [[LandmarkNav]] cycles whole-area markers) answer "what's near me" but never
"what is at *this specific spot*" or "what does this room look like tile by tile".

This feature adds a **virtual grid cursor** the user drives with the arrow keys, one tile at a time,
over the game's pathfinding grid (`CustomGridGraph`, the square 1.35 m grid — see [[rt-world-grid]]).
Each step speaks a full readout of the tile (occupant, walkability + reason, cover on each edge,
position relative to the anchor unit — see [[rt-cover-los]]). It works both while exploring and in
tactical combat, and follows the camera so a sighted helper sees the same tile.

Confirmed design decisions (from the user):
- **Scope:** works in **both** exploration and combat (any on-foot surface = `GameModeType.Default`).
- **Per-step announcement:** **everything** each step — occupant + walkable/reason + cover edges + relative position.
- **Activation:** **toggle mode** — a key turns the cursor on/off; while on, arrows drive it.

## Files

**New:** `RTAccess/Accessibility/TileExplorer.cs` — static class holding cursor state, input handling,
gating, movement, camera follow. Mirrors the existing `static class … { public static void Update() }`
pattern (`ExplorationNav.cs`, `PartyHotkeys.cs`).

**Modify:**
- `RTAccess/Accessibility/InteractableDescriber.cs` — add `DescribeTile(CustomGridNodeBase node, MechanicEntity anchor)`
  returning the composed "everything" line, reusing the file's existing private helpers (`ResolveName`,
  `Verb`, `Clean`) and the world-axis compass convention (+Z north, +X east).
- `RTAccess/Main.cs` — call `TileExplorer.Update();` inside `OnUpdate` (next to the other `*.Update()` calls).
- `RTAccess/Accessibility/ExplorationEvents.cs` — in its existing `OnAreaActivated` handler, call
  `TileExplorer.Reset()` so a stale cursor from a previous area is cleared.

## Design

### Cursor & movement
- State (static fields): `bool Active`, `CustomGridNodeBase _cursor`.
- Cursor is a node on the active `CustomGridGraph`. Get the graph from the anchor's node
  (`node.Graph as CustomGridGraph` / `CustomGridNode.GetGridGraph(node.GraphIndex)`); index with
  `grid.GetNode(x, z)` (same call `CoverVisualizer.UpdateCoverMeshes` uses), clamp to `[0,width)×[0,depth)`.
- Arrow → integer step on grid axes (grid is world-aligned in the normal case): **Up = +Z (north),
  Down = −Z, Right = +X (east), Left = −X**. This matches the existing map-relative compass
  (`InteractableDescriber`, +Z north / +X east). Off-grid target → keep cursor, say "Edge."
- The cursor roams **all** tiles, walkable or not (the point is to find walls/layout), so step by raw
  `(x,z)±1`, not by walkable connections.

### Anchor (reference unit)
`GetAnchor()` = `Game.Instance.SelectionCharacter.SelectedUnit.Value`
?? `Game.Instance.Player.MainCharacterEntity` (in combat the current-turn unit is the selected unit).
Its node = `anchor.CurrentUnwalkableNode` (a `CustomGridNode`, nearest regardless of walkability).
Relative position in the readout is computed from the anchor node's `(X,Z)CoordinateInGrid`.

### Gating
At the top of `Update()` (defensive, matches existing features):
`Game.Instance != null` && `ControllerMode == Gamepad` (console mode) && `CurrentMode == GameModeType.Default`.
`Default` covers both on-foot exploration and surface tactical combat, and excludes dialog / full-screen
windows / cutscene / global & space maps. If `Active` and gating drops (mode change, no anchor), auto-deactivate
and say "Tile explorer off."

### Activation & keys (toggle mode)
Uses only keys the other nav features don't, so there's no double-handling when both run in `OnUpdate`:
- **Insert** — toggle on/off. Turning *on* (re-)initialises `_cursor` to the anchor's node and announces
  "Tile explorer on" + the current tile. (Toggling off→on therefore doubles as "recenter on my unit".)
- **Arrow keys** — move the cursor one tile; announce the new tile.
- **Delete** — re-announce the current tile.

(Note: arrow input can't be hard-consumed from a UnityModManager `OnUpdate`; any incidental camera-pan is
harmless since we `ScrollTo` the cursor anyway.) v1 is read-only — acting on a tile (move party / interact)
is a deferred enhancement.

### Announcement (`InteractableDescriber.DescribeTile`)
Key-driven → `Speaker.Speak(line, interrupt: true)` (each step supersedes the previous, per
[[rt-interrupt-speech-rule]]; stepping fast naturally cuts long lines off at the headline). Parts, in order,
each omitted when empty:

1. **Occupant** (headline): `node.GetUnit()` → `CharacterName` + faction tag ("enemy" if `Faction.IsPlayerEnemy`,
   else "ally"); else an interactable map object at the tile → name + verb (reuse `ResolveName`/`Verb`); else
   `DestructibleEntity.FindByNode(node)` → prop name.
   - Map-object lookup: `EntityBoundsHelper.FindEntitiesInRange(node.Vector3Position, ~0.7f)`, filter to
     `MapObjectEntity` whose `GetOccupiedNodes()` contains the cursor node.
2. **Walkability**: if `!node.Walkable` and no entity already named → "wall" (bare geometry) or "blocked"
   (otherwise). Walkable & empty → "clear".
3. **Cover edges**: for dir ∈ {N=2, E=1, S=0, W=3} call `LosCalculations.GetCellCoverStatus(node, dir)`
   (the exact source `CoverVisualizer` uses); append "half/full cover <dir>" for any non-`None` edge.
4. **Relative position**: `(dx,dz)` = cursor − anchor in tiles → "N east/west, M north/south" (omit zero
   components; "here" if on the anchor tile). Optionally append cell distance via
   `WarhammerGeometryUtils.DistanceToInCells`.

Example lines: `"Cultist, enemy. Half cover north. 5 east, 2 north."` · `"Wall. 3 east, 1 north."` ·
`"Door, activate. 2 east."` · `"Clear. here."`

### Camera follow
On every cursor move: `CameraRig.Instance?.ScrollTo(_cursor.Vector3Position)` (guard null).

## Known limitations
- Arrow→axis mapping assumes the area's grid is world-aligned (`isometricAngle 0`, no graph rotation —
  the normal case). The spoken **bearing** is always computed from the true world delta, so it stays
  correct even if a grid were rotated; only the arrow-to-direction feel would be off in that rare case.
- Space combat (different grid/mode) is out of scope.

## Verification
1. Build: `dotnet build RTAccess.slnx -c Release` (build the **.slnx**, not the .csproj — per [[rt-a11y-architecture-verdict]]).
2. In-game (console mode forced by default), load a surface area. Press **Insert** → expect "Tile explorer on"
   + the unit's tile readout. Arrow around: confirm walls/doors/cover/units are announced and the camera follows.
3. Walk the cursor into a known wall/low-cover spot and verify "wall" vs "half/full cover <dir>" matches what's
   on screen (cross-check with the game's own cover overlay by holding Ctrl, which drives `CoverVisualizer`).
4. Enter tactical combat; confirm the cursor still toggles and reads tiles (anchor = current unit), and that
   enemy tiles announce "enemy" + their cover.
5. Press **Insert** again (or change mode) → "Tile explorer off"; confirm arrows return to normal and no
   double-speech with ExplorationNav/LandmarkNav.
