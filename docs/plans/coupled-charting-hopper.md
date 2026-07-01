# RT Always-Active, Scanner-Coupled Exploration Cursor — Plan

Plan name: `docs/plans/coupled-charting-hopper.md` (sibling to `echoing-charting-lovelace.md`). A **focused revision of `echoing-charting-lovelace.md` §3.1** that makes the exploration cursor **always-active (no Ctrl+T toggle)** and gives the cursor↔scanner pair the **exact Pathfinder / WrathAccess (WA) two-cursor coupling**, on RT's discrete square tile grid. It **keeps** echoing's two-cursor discipline (it does **not** collapse to one position — an earlier draft of this plan did, and it was wrong: see §2), **keeps** `ScanFrom = movement cursor` (echoing Phase A), drops the Ctrl+T toggle, migrates the cursor off raw `UnityEngine.Input` polling into `InputManager`, and adds the full WA-parity verb/key map. Phases C–J of echoing (scanner taxonomy/`ScanBounds`/`WorldModel.Tick`, targeting, overlays, audio) compose on top unchanged.

---

## 0. Executive summary

The directive: the exploration cursor should work like WA's — **always active, no toggle**, and **coupled with the scanner** — *except* it steps RT's discrete 1.35 m square grid instead of WA's continuous navmesh. Today RT has two loosely-linked pieces: `TileExplorer` (a Ctrl+T **mode**; arrows move the cursor only while toggled on; raw-polled in `Main.OnUpdate`) and `Scanner` (registered `InputCategory.Exploration` actions: PageUp/Dn category browse + review-group cycles). This plan keeps them as **two coupled positions** and removes the mode.

**The locked model (WA, verified in code).** There are **two positions, deliberately distinct and tightly coupled**:

1. the **movement cursor** (`MapCursor`) — a tile you step with arrows; it is the **sort/measure origin** and what overlays/audio/targeting/move-to all read; and
2. the **review selection** (`Scanner`'s `_selectedKey` / `Selected`) — the scan *item* you enumerate with PageUp/Dn and the review-group keys.

The coupling is WA's exactly: **review never moves the cursor** (`Main.cs:696-698` *"the movement cursor, which NEVER moves (look around while holding position)"*; `DoCycleReview` `Scanner.cs:479` changes only the selection and sorts candidates by `DistanceTo(ScanFrom)`), the cursor moves **only** on arrows or on the explicit **Home/Slash plant-on-selection** (`scan.cursorToItem` → `CommitCursor` → `Cursor.Set`, `Scanner.cs:105,575`), and the review sort origin is the (stable) cursor. Because review doesn't move the cursor, `ScanFrom = cursor` is stable — there is **no ping-pong**, and no need for the anchor fallback an earlier collapse-draft invented.

The act/inspect verbs come in **cursor / selection pairs** (this is why I and Y exist and are not redundant):

- **Enter** interacts with the **cursor occupant**; **I** interacts with the **review selection**.
- **'** (apostrophe) inspects the **cursor** unit; **Y** inspects the **review-selection** unit.
- **Backspace** moves the party to the **cursor** tile; **Home/Slash** plants the cursor on the **selection**.

Three correctness fixes from review are hard requirements:

1. **The hand-rolled move-to skips the engine's turn guards.** RT's own `MoveSelectedUnitToPointTB` *throws* when the selected unit isn't the current-turn unit (`UnitCommandsRunner.cs:265-268`) and when the selection isn't exactly one (`:260-263`). Both `TileExplorer.MoveToCursor` and `Scanner.MoveToSelected` call `TryCreateMoveCommandTB` directly on `TurnController.CurrentUnit` and skip those guards — so on an **enemy's turn** a `Backspace` (move-to) press would command the *enemy* or be silently rejected while the mod falsely says "Moving." Always-active + a reflex move-to key turns a latent, Insert-only hazard into a one-keystroke one. The unified move helper must gate on `IsPlayerTurn` + selected-is-current-turn-and-controllable first.
2. **TB move-to commits with no preview and burns the unit's whole movement in one press** (`showMovePrediction:false` + immediate `Commands.Run`). In turn-based combat, `Backspace` move-to ships as a **two-step confirm** (arm + speak cost, commit on the second press).
3. **The Home/Slash plant onto an off-grid item must not silently unplant the cursor.** `GetNearestNodeXZ()` is `[CanBeNull]` (flying/elevated/off-grid points); `MapCursor.Set(null)` would clear the cursor, teleporting it back to the party on the next step. The `Set(Vector3)` overload null-guards and keeps the previous node.

Two phases, each independently shippable and harness-verifiable: **R1** makes the cursor always-active with registered, guarded controls; **R2** wires the WA two-cursor coupling (plant-on-selection, the verb pairs, the unified readout).

## 0.1 Phase overview

| Phase | Deliverable | Effort | Depends-on | Harness verify |
|---|---|---|---|---|
| **R1** | Drop the Ctrl+T mode (no replacement toggle); register `cursor.*` actions (arrows + Shift+arrows; **Enter=interact-cursor, Backspace=move-to, C=recenter, Delete=re-announce, Escape=cancel**); remove `TileExplorer.Update()`; gate on Exploration-category liveness; **one deduped, turn/pause-guarded move-to (Backspace)** with TB two-step confirm; drop the bare-`Insert` `scan.move_to` binding | S–M | — (revises `echoing` A/B) | `/input cursor.up` steps with no toggle; `/input` Shift-slot steps while HUD focused; `/eval` cursor dead in a window, live in Pause; `/input cursor.move_to` (Backspace) on a simulated enemy turn → "Not your turn" (no command issued); TB first-press arms + speaks, second commits; `C` recenters with no game-`C` leak |
| **R2** | WA two-cursor coupling: review cycles change the **selection only** (cursor stays put); **Home/Slash** plants cursor on selection (null-guarded) + camera (frees `Home` by moving where-am-i → `X`); `ScanFrom` = cursor (stable); the **verb pairs** — `Enter`/`I` interact (cursor/selection), `'`/`Y` inspect (cursor/selection, split off `scan.inspect`/`K`); fold area-scoped POI (`B`) + exits (`V`) review groups; one composed readout; camera-follow setting | S–M | R1 | `/input scan.item_next` changes the selection but `/eval MapCursor.Node` is **unchanged**; `/input scan.cursor_to_item` (Home) plants the cursor on the selection; `Enter` vs `I` act on cursor-occupant vs selection respectively; `/eval` review sort is monotonic from the (stationary) cursor — no ping-pong |
| **R3** | Party selection + combat status: register `party.select_all` (**Ctrl+A**) → `SelectionManagerBase.SelectAll()` (restores whole-party so formation move-to is recoverable) and `combat.status` (**R**) → speaks `InGameScreen.StatusLine()` AP/MP/turn one-press; optional `party.hold` (**H**) / `party.stop` (**G**); move-to announces selection cardinality ("Moving party" vs "Moving \<name\>"). Skip WA stealth/AI (no RT feature) | S–M | R1 (move helper), R2 (coupling) | `/input party.select_all` → `SelectedUnits.Count` = party; single-member then `cursor.move_to` → "Moving \<name\>", after select-all → "Moving party" (`/eval` formation); `/input combat.status` speaks AP/MP/turn one-press; game Ctrl+A/H/G don't leak (`/gui`) |

This plan touches `echoing-charting-lovelace.md` §3.1 and a few of its Phase-C/D verify items (§2). Phases C–J of that plan are otherwise unaffected.

---

## 1. Goal & scope

**What "always-active, scanner-coupled, tile-stepped" means.** At any moment in exploration or surface tactical combat (and not while a menu/dialogue/HUD owns the keyboard), the player presses an arrow and a world cursor steps one tile and speaks it — **no mode to enter first** — and the scanner browses *the same surface*: review cycles enumerate things **around the cursor without moving it** (look around while holding position), and Home/Slash jumps the cursor onto whatever you're reviewing. Two positions, tightly coupled, always live.

**IN scope:**
- **Drop the mode.** Remove `TileExplorer._active` (`:35`), the Ctrl+T toggle (`:49-50`), the `if (!_active) return;` gate (`:51`), `Toggle()` (`:84-94`), `Deactivate()` (`:96-101`), the raw `Update()` poll (`:37-75`), the `Deactivate()` null-bails in `Move`/`MoveToCursor` (`:108`,`:132`), and the `TileExplorer.Update();` call in `Main.cs:134`.
- **Always-active wiring:** lazy plant on first use (`EnsurePlanted`), an explicit **recenter** verb on `C`, area-change clear retained.
- **Migrate the cursor's keys to registered `InputCategory.Exploration` actions** (`cursor.*`) so they participate in chord-shadowing and `/input`. Completes echoing Phase B's deferred "TileExplorer input-registration migration."
- **Keep WA's two input slots:** Primary plain-arrows (shadowable) + Secondary `Shift+arrows` (shadow-immune), framework-registered.
- **Keep the two coupled positions** (movement cursor + review selection) with WA's exact rules: review doesn't move the cursor; Home/Slash plants; `ScanFrom` = cursor.
- **The WA-parity verb pairs:** `Enter`/`I` (interact cursor / selection), `'`/`Y` (inspect cursor / selection), each with the targeting dual-role; one guarded `Backspace` move-to.
- **One composed readout** keyed on the cursor node for arrow-steps and on the selected item for review cycles, sharing one composer.
- A **camera-follow setting** (the camera follows the *movement cursor* on step / Home-Slash plant / recenter).
- **Drop the NVDA-violating bare-`Insert` `scan.move_to` binding.**

**OUT of scope (unchanged from `echoing-charting-lovelace.md`):**
- **Continuous / sub-tile glide** — explicitly excluded ("EXCEPT FOR TILES"). RT is a 4-connected square `CustomGridGraph` (1.35 m); stepping stays discrete N/E/S/W via `Move(dx,dz)`. WA's `ContinuousGlide`/`HeldVector`/`TraceAlongNavmesh` are not ported.
- **The scanner taxonomy/`ScanBounds`/`WorldModel.Tick`/typed-announce upgrade** (echoing Phase C — this plan only changes coupling, keys, and the readout composer).
- **Targeting-from-cursor, PathInfo, overlays, the audio soundscape** (echoing D–I) — they read `MapCursor.Position`, which is unchanged by this plan.
- **`RoomMap` watershed room segmentation** — deferred to echoing Phase J, so WA's *per-room* exit cycle (`V`) is ported **area-scoped** (§3.7).
- **Bare `Insert` as a binding** — NVDA's modifier key; this plan removes the one standing violation and never re-introduces it.
- **Reviving `ExplorationNav` / `SurfaceMainInputLayer`** — dead in mouse mode; all scanning self-drives off `Game.Instance.State.*` + `EntityBoundsHelper`.

---

## 2. What RT already has / what changes vs `echoing-charting-lovelace.md`

**Reuse foundation (live, correct, kept):**
- `Exploration/MapCursor.cs` — the shared movement cursor: `Node`/`Has`/`Position` (falls back to the anchor's live view position when unplanted), `Set(CustomGridNodeBase)`, `Clear()`.
- `Accessibility/TileExplorer.cs` — `Move(dx,dz)` (grid step via `(cur.Graph as CustomGridGraph).GetNode(x+dx,z+dz)`), `Describe()` → `InteractableDescriber.DescribeTile`, `ScrollTo`, `GetAnchor()`, `Reset()` (clears the node on area change).
- `Exploration/Scanner.cs` — registered actions (`ItemPrev/Next`, `CategoryPrev/Next`, `Review*`, `InteractSelected`, `MoveToSelectedTile`, `WhereAmINow`, `ReadParty`), `Select(list, idx, refPos)`, `IndexOfSelected`, `ScanFrom()`, `Anchor()`. The review selection (`_selectedKey`) is exactly the second position this plan keeps.
- `Input/*` framework — `InputManager.Tick` chord-shadowing, `KeyboardBinding.ModifiersMatch` (exact), `.Repeating()`/`.Grouped()`, `InGameScreen` `FocusedCats`↔`UnfocusedCats`↔`NoControlCats`.
- `Accessibility/InteractableDescriber.cs` — `DescribeTile(node, anchor)`, `DirectionAndDistance(from,to)` (metres + 8-way map-relative compass), the map-object-at-node lookup gated on `GetOccupiedNodes().Contains(node)`.
- `ControlState.HasControl` — `ClickEventsController != null`; the gate the scanner rides.

**Changed vs `echoing-charting-lovelace.md`:**

| `echoing` §3.1 decision | This plan |
|---|---|
| **Ctrl+T toggle kept** (mode enter/exit; turning on recenters) | **Dropped outright — no replacement toggle.** Cursor is always-active. Recenter survives as its own verb on **`C`** (WA's `cursorRecenter`/`overlay.recenter` = `C`, `Main.cs:662,688`). |
| **Two-cursor discipline** — `MapCursor` is the movement cursor; the scanner keeps a separate review selection | **Kept, with WA's explicit coupling.** Review cycles change the **selection only** and never move the cursor (`Main.cs:696-698`); **Home/Slash plants** the cursor on the selection (`Scanner.cs:105,575`). The act/inspect verbs come in cursor/selection pairs (Enter/I, '/Y). *(An interim draft of this plan collapsed the two into one position; that deleted WA's "look around while holding position" capability and created a sort ping-pong — reverted.)* |
| **`ScanFrom = MapCursor` when planted else anchor** (Phase A) | **Kept as-is.** Stable, because review doesn't move the cursor: sorting review candidates from the (stationary) cursor cannot ping-pong. (The collapse-draft's "revert `ScanFrom` to the anchor" patch is **not** needed and is dropped.) |
| **`TileExplorer` left on Phase-A raw polling** | **Migrated here.** `cursor.*` actions registered in `InputBindings`; `TileExplorer.Update()` + its `Main.cs:134` call removed. |
| **`scan.move_to` on bare `Insert`** (`InputBindings.cs:87-88`) | **Removed** (NVDA eats Insert). Move-to is `cursor.move_to` on **`Backspace`** (WA's `scan.moveToCursor`, `Main.cs:539-540`). Insert freed. |
| **`scan.interact` on `End`** (interact selection) | **Rebound to `I`** for WA-parity (`scan.interact` = `I`, `Main.cs:531`), and a *new* `cursor.interact` on `Enter` interacts the cursor occupant. **`End` freed.** |
| **Gate = `ExplorationActive`** (`CurrentMode == Default`) | **Gate = Exploration-category liveness** (`ControlState.HasControl`). Net delta: the cursor also goes live during real-time **Pause** (matching the scanner) — safe scouting, with move-to pause-guarded. |

**`echoing-charting-lovelace.md` phases that compose unchanged (only touchpoints):**
- **Phase C** (scanner taxonomy/`ScanBounds`/`WorldModel.Tick`/`ProxyAreaEffect`/`ScanSounds`/typed announce): unaffected; its "two-cursor discipline holds" verify item is satisfied *as written* (we keep two cursors). Footprint `Contains` later sharpens R2's cursor-occupant lookup. Adds a **POI** (`B`) and **exits** (`V`) review group — area-scoped (§3.7).
- **Phase D** (Targeting/PathInfo): the targeting commit/cancel routes through the verb pairs — `Enter` → commit at cursor, `I` → commit on selection, `Backspace`/`Escape` → cancel (WA `Scanner.cs:114,122,128`). PathInfo's MP-cost readback enriches R1's TB two-step confirm ("4 tiles, 3 MP — press again to move").
- **Phases E (overlays), F–I (audio), J (deferred):** read `MapCursor.Position` unchanged; the overlay cursor wrapper now wraps an always-active cursor (no enter/exit), which simplifies it.

---

## 3. Architecture

### 3.1 The two coupled positions (the model)

The surface has **two positions, tightly coupled, always in sync by WA's rules:**

- **Movement cursor** = `MapCursor.Node` (grid truth) + its `Vector3`. Stepped by arrows; planted on the selection by Home/Slash; recentred on the party by `C`. It is the **sort/measure origin** (`ScanFrom`) and the point overlays/audio/targeting/move-to read.
- **Review selection** = `Scanner`'s `Selected` (`_selectedKey`). Changed by PageUp/Dn (item within category), Ctrl+PageUp/Dn (category), and the review-group keys (Comma/Period/N/M/B/V). **Changing the selection does NOT move the cursor.**

Coupling (verbatim WA semantics):
- **Review sorts from the cursor.** `DoCycleReview` (`Scanner.cs:479`) sorts candidates by `DistanceTo(ScanFrom)` where `ScanFrom` = the movement cursor (falling back to the party's live position when unplanted). Stable, because the cursor doesn't move while you cycle — *"closest first from where I'm holding."*
- **Home/Slash plants the cursor on the selection.** `scan.cursor_to_item` → `CommitCursor` → `MapCursor.Set(selection.Position)` (null-guarded, §3.5) + camera. The one explicit "go to what I'm reviewing" jump.
- **Two readouts, one composer.** Arrow-step speaks the **cursor tile** (`DescribeTile`: occupant + per-edge cover + offset). Review-cycle speaks the **selected item** (name + faction/state + metres + compass, measured from the cursor) plus the `"N of M"` index. Both share one composer so an enemy reads consistently by either path; the difference is which target (cursor tile vs selected item) supplies the headline.

**Why two positions (not one).** Keeping the cursor still during review is the *feature*: a blind player parks the cursor and enumerates everything around it without losing their place, then acts on the reviewed thing with `I`/`Y` or jumps to it with Home. Collapsing to one position deletes that and forces a cursor-move on every browse (which also re-sorts from the moved point → ping-pong). Two positions is both WA-faithful and simpler.

### 3.2 Always-active wiring & lifecycle

**Lazy plant + explicit recenter** replaces "toggle on → recenter":
- `EnsurePlanted(out bool fresh)`: if `!MapCursor.Has`, plant on `GetAnchor()?.CurrentUnwalkableNode`; if that is null, speak "No reference point." and return false; else set it and report `fresh`.
- Each step handler does: `if (!EnsurePlanted(out var fresh)) return; if (fresh) { Announce(); return; } Move(dx,dz);`. So the **first** arrow plants on the party and reads that tile; the **second** takes the first real step. `cursor.move_to`/`cursor.reannounce` likewise read the fresh plant on the first press rather than acting.

**Where the cursor sits untouched: implicitly at the party.** `MapCursor.Position` falls back to the anchor's live view position when unplanted, so the scanner already measures from the party before the player ever arrows. The cursor does **not** scroll the camera until the player actually arrows / plants / recenters.

**Lifecycle:**
- **Boot / area load:** unplanted. `TileExplorer.Reset()` keeps `MapCursor.Clear()` on `ExplorationEvents.OnAreaActivated` (RT grids are per-area; a stale node from the previous area's graph is invalid). Drop only the `_active = false` line. The review selection clears on area change too.
- **First arrow / Home plant:** lazy plant.
- **Selection / turn change:** do **not** auto-move a planted cursor. While unplanted, the fallback tracks the new anchor. `C` snaps a planted cursor back to the anchor on demand.
- **Leaving exploration (window/dialogue/cutscene/loading):** no teardown — the registered `cursor.*`/`scan.*` actions go dead because `InGameScreen` drops the Exploration category (`NoControlCats`) when `!HasControl`. The planted node persists and stays valid on return within the same area.

### 3.3 Input ownership & the full key map

All cursor keys move out of raw `UnityEngine.Input` polling into registered `InputCategory.Exploration` actions in `InputBindings.RegisterDefaults`, each `.Grouped("cursor")`.

**Primary slot — plain arrows (shadowable):** `cursor.up/down/left/right` → `UpArrow/DownArrow/LeftArrow/RightArrow`, `.Repeating()`, handlers `StepNorth/South/East/West`. They collide with `ui.up/down/left/right`; `InGameScreen` resolves it per frame — HUD **unfocused** → Exploration ranked above UI → cursor moves; HUD **focused** → UI ranked above → arrows shadowed, navigator owns them (the old `if (Navigation.HasFocus) return;`, now framework-driven).

**Secondary slot — Shift+arrows (shadow-immune):** `cursor.up2/down2/left2/right2` → `Shift+UpArrow` etc., `.Repeating()`, same handlers. Verified free (UI uses bare arrows; buffers are Alt+arrows; scanner reverse cycles are Shift+letters; `ModifiersMatch` is exact). Keeps stepping the cursor even when the combat action bar owns the bare arrows — the always-works escape hatch.

**The full WA-parity key map** (collisions all intentional / resolved by shadowing; reused game keys rely on focus-mode suppression — §6):

| Key | Action | Target / notes |
|---|---|---|
| Arrows | step movement cursor | primary slot, shadowable |
| Shift+Arrows | step movement cursor | secondary slot, shadow-immune |
| PageUp / PageDown | previous / next item in category | **review selection**; cursor stays put |
| Ctrl+PageUp / Ctrl+PageDown | previous / next category | review selection |
| Comma / Period / N / M (Shift = reverse) | review groups: party / enemies / neutrals / objects | review selection; nearest-first from the cursor |
| B / V (Shift = reverse) | review groups: POI / exits-transitions | **R2** — area-scoped (from `LocalMapModel.Markers`); per-room scoping deferred (no RoomMap) |
| Home / Slash | **plant cursor on the review selection** | the explicit "go to selection" jump |
| Enter / KeypadEnter | interact with **cursor occupant** | aiming → commit ability at cursor |
| I | interact with **review selection** | aiming → commit ability on selection |
| Backspace | move party to **cursor** tile | guarded (turn / pause / TB two-step); aiming → cancel |
| ' (Quote) | inspect **cursor** unit | via `rt-inspect-contract` |
| Y | inspect **review-selection** unit | via `rt-inspect-contract` |
| Escape | cancel targeting, else open pause menu | `InputCategory.InGame` (survives loss of control) |
| C | recenter cursor on party (+ camera) | WA-parity |
| Delete | re-announce cursor tile | NVDA-safe |
| `[` / `]` | (existing `LandmarkNav`) cycle area landmarks incl. exits/POI | area-scoped (RoomMap deferred — §3.7) |

**Not used:** Ctrl+T (no toggle), bare Insert (NVDA), `G` (the game's *Stop*), `End` (freed — interact moved to `Enter`/`I`).

### 3.4 The act/inspect verb pairs + the guarded move helper

Each act/inspect verb comes in a **cursor / selection** pair — the reason `I` and `Y` exist (they act on the parked review selection without disturbing the cursor):

- **Interact:** `cursor.interact` (`Enter`) → `InteractAtCursor`, resolves the **cursor occupant** (`node.GetUnit()` + the `GetOccupiedNodes().Contains(node)` map-object lookup; "nothing here" + **no move** on an empty tile). `scan.interact` (`I`) → `InteractSelected`, acts on the **review selection** (the item reference — robust even when the cursor is parked elsewhere or its nearest node falls just outside the item's footprint). Both check `Targeting.Aiming` first (`Enter` → `CommitAtCursor`, `I` → `CommitOn(Selected)`).
- **Inspect:** `inspect.cursor` (`'`) → the cursor-occupant unit; `inspect.review` (`Y`) → the review-selection unit. Both gate on `InspectUnitsHelper.IsInspectAllow` and, on RT, open via the `rt-inspect-contract` path (raise `IUnitClickUIHandler.HandleUnitConsoleInvoke`, read `SurfaceHUDVM.InspectVM.Tooltip` via `TooltipReader`) — **not** WA's `TooltipHelper.ShowInspectTooltip`. "Nothing to inspect" when there's no inspectable unit.
- **Move-to:** `cursor.move_to` (`Backspace`) → the **cursor** tile, through the one guarded helper below; `Targeting.Aiming` → `Cancel`.

**Dedupe the move fork.** `Scanner.MoveToSelected` and `TileExplorer.MoveToCursor` (+ their identical `MoveFailure` switches) are near-verbatim duplicates. Collapse to one guarded helper targeting `MapCursor.Node`:

```
MoveToCursor():
  if (!EnsurePlanted(out fresh)) return;
  if (fresh) { Announce(); return; }                 // first press reads; doesn't move
  node = MapCursor.Node; game = Game.Instance;
  if (game.TurnController.TurnBasedModeActive):
      if (!game.TurnController.IsPlayerTurn)          # mirrors engine guard UnitCommandsRunner.cs:265-268
          { Speak("Not your turn."); return; }
      sel = game.SelectionCharacter.SelectedUnit?.Value
      if (sel == null || sel != game.TurnController.CurrentUnit || !sel.IsDirectlyControllable())
          { Speak("Select your active character."); return; }   # mirrors :260-263 single-unit guard
      if (!_confirmArmed(node)) { _arm(node); Speak(pathCostPhrase(node) + ", press again to move."); return; }
      cmd = sel.TryCreateMoveCommandTB(... showMovePrediction:false ...)
      if (cmd != null) { sel.Commands.Run(cmd); Speak("Moving."); } else Speak(MoveFailure(status));
  else:
      if (Game.Instance.IsPaused) { Speak("Paused."); return; }   # don't queue a surprise order
      if (GetAnchor() == null) { Speak("No character selected."); return; }
      UnitCommandsRunner.MoveSelectedUnitsToPoint(node.Vector3Position); Speak("Moving.");
```

- **Turn guard:** never build/run a command on `TurnController.CurrentUnit` unless it is the player's turn and the selected, directly-controllable unit — otherwise `Backspace` on an enemy's turn would command the enemy or falsely announce "Moving." (`IsPlayerTurn` already used at `InGameScreen.cs:363`.)
- **Two-step confirm (TB only):** first press arms + speaks cost and returns; a second `cursor.move_to` within ≈3 s commits. Until echoing Phase D PathInfo lands, `pathCostPhrase` speaks tile distance ("4 tiles away"); Phase D enriches to "4 tiles, 3 MP." Exploration moves commit immediately, except while paused.
- **Pause guard:** the exploration branch refuses while `Game.Instance.IsPaused` (the cursor is now live in Pause). Stepping/reading while paused is fine; only the commit is gated.

Move-to lives on its **own** key (`Backspace`), separate from interact (`Enter`), so movement is never an accidental side-effect of "interact."

### 3.5 `MapCursor.Set(Vector3)` — the null-guarded plant

The Home/Slash plant (and any item-targeted plant) goes through a `Set(Vector3 worldPos)` overload:
- Prefer the item's **nearest occupied node** when it exposes a footprint (Phase C); else snap via `worldPos.GetNearestNodeXZ()`.
- **If the snapped node is null** (off-grid: flying/elevated item — `GetNearestNodeXZ` is `[CanBeNull]`), **keep the previous node** (don't assign null) and return false. The caller still speaks the item from the cursor; the cursor never silently unplants and teleports back to the party on the next step.

### 3.6 Camera-follow

A setting `exploration.camera_follow` ∈ {Off, On}, **default On**: the camera `ScrollTo`s the **movement cursor** whenever it moves — arrow-steps, the Home/Slash plant (the explicit "go to selection" jump, where you *want* the camera to land), and `C` recenter. Review cycles do **not** move the cursor, so they never scroll the camera; the parked-cursor view stays put while you enumerate. `ScrollTo` is hoisted so step and plant share one path that reads the setting first. (`Off` = the cursor never drives the camera — for a player who never wants camera motion.)

### 3.7 Room exits / POI — area-scoped (RoomMap deferred)

WA's `V`/`Shift+V` (`review.nextExit`/`prevExit` → `Scanner.CycleRoomExits`, `Main.cs:729-734`, `Scanner.cs:412`) cycles the **current room's** exits: `RoomMap` geometric openings + in-room door/area-transition items (a closed door *is* the exit since it cuts the navmesh; an item within ~2 m of an opening replaces it). **RT has no `RoomMap`** — watershed room segmentation is deferred to echoing Phase J (doorway detection at 1.35 m is unsolved) — so the per-room scoping is not portable yet.

RT's portable equivalent is **area-scoped** and already ships: `LandmarkNav` (`[`/`]`, `rt-exploration-nav`) cycles `LocalMapModel.Markers` (exits, area-transitions, POI, loot, objectives) nearest-first. **Plan:** keep `[`/`]` working; fold an area-scoped **"exits/transitions"** review group (and WA's `B` **POI** group) into the coupled scanner so they behave like the other review cycles (sort from the cursor, land as the selection, Home plants the cursor on them); revisit true room-scoping when `RoomMap` lands (Phase J).

---

## 4. The hard RT-specific decisions

1. **Tile-stepped, not glided.** The cursor steps the 4-connected square `CustomGridGraph` via `Move(dx,dz)` → `GetNode(x+dx,z+dz)`; "Edge." at the boundary. No diagonals, no sub-tile motion. This is the user's "EXCEPT FOR TILES" carve-out from WA's continuous model.
2. **Two coupled positions, WA-exact.** Review changes the selection only; the cursor moves on arrows / Home-Slash / C. `ScanFrom` = the (stationary-during-review) cursor → stable sort, no ping-pong. The act/inspect verbs are cursor/selection pairs (Enter/I, '/Y).
3. **Engine turn guards are not optional.** RT's own move path *throws* on wrong-turn / multi-select (`UnitCommandsRunner.cs:260-268`); the mod's hand-rolled `TryCreateMoveCommandTB` path bypasses them, so the mod must reproduce them. The always-active cursor + a reflex move-to key (`Backspace`) makes this a one-keystroke hazard.
4. **TB move-to is destructive → two-step confirm.** A single unconfirmed `Backspace` in turn-based combat spends the unit's whole movement with no preview. Confirm in TB; commit immediately (pause-guarded) in exploration. Move-to on its own key (separate from interact) means it's never triggered by mistaking it for "interact."
5. **Gate on category liveness, not `CurrentMode`.** Riding `ControlState.HasControl` (via the registered Exploration category) unifies the cursor's gate with the scanner's. The only behavioral delta is the cursor going live during Pause — a safe scouting moment, with move-to pause-guarded. (There is no separate `TacticalCombat` `GameModeType`: surface combat runs in `Default`; `SpaceCombat`/`GlobalMap`/`StarSystem` don't declare Exploration.)
6. **Bare Insert is gone for good; no Ctrl+T, no `G`.** NVDA eats Insert; the freed key is left unbound. All cursor keys (arrows, Shift+arrows, Enter, KeypadEnter, I, Backspace, `'`, Y, Delete, `C`, Escape, Home/Slash) are NVDA-safe and WA-parity where WA has an equivalent. `G` (game *Stop*), Ctrl+T, bare Insert, and `End` are not used by the cursor.
7. **Reused game keys (`C`, `Backspace`, `Enter`, `'`, `Y`, Home) rely on focus-mode suppression.** The maintainer states the game's own World-mode hotkeys don't fire while the mod's exploration owns input. This is load-bearing and verified in-harness (§6); fall back to free keys (`O`/`U`/`Z`) for any that leak.

---

## 5. Phased build order

Each phase is independently shippable and harness-verifiable (`/eval`, `/speech`, `/gui`, `/input`, `/screenshot`, `/loadsave`). Build-verify each (`dotnet build RTAccess.slnx -c Debug` → 0/0) before in-game.

### Phase R1 — Always-active cursor + registered, guarded controls (S–M)
**Why first:** removes the toggle and the raw poll and makes move-to safe — the prerequisite for the coupling.
- **`InputBindings.cs`:** register `cursor.up/down/left/right` (plain, `.Repeating()`, `.Grouped("cursor")`), `cursor.up2/down2/left2/right2` (Shift, `.Repeating()`), `cursor.reannounce` (Delete), `cursor.recenter` (`C`), `cursor.interact` (Return + KeypadEnter), `cursor.move_to` (Backspace) — all `InputCategory.Exploration`; plus `cursor.cancel` (Escape, `InputCategory.InGame`). **Remove** the `scan.move_to`/`Insert` binding (`:87-88`). **No Ctrl+T, no `G`.**
- **`TileExplorer.cs`:** delete `_active`, `Update()`, `Toggle()`, `Deactivate()`, the Ctrl+T toggle branch + `!_active` gate + `Navigation.HasFocus` early-return, the `Deactivate()` null-bails; drop `_active = false` from `Reset()`. Add `EnsurePlanted(out bool fresh)` and the registered handlers (`StepNorth/South/East/West`, `ReAnnounce`, `Recenter`, `InteractAtCursor`). Keep `Move` (minus null-bail), `Describe`, `ScrollTo`, `GetAnchor`.
- **`Scanner.cs` / `TileExplorer.cs`:** dedupe `MoveToSelected`/`MoveToCursor` + `MoveFailure` into one guarded move helper targeting `MapCursor.Node` (turn guard + single-controllable-unit guard + TB two-step confirm + pause guard); `cursor.move_to` (Backspace) routes through it.
- **`Main.cs`:** remove `TileExplorer.Update();` (`:134`).
- **`MapCursor.cs` / docs:** doc the two coupled positions and `ScanFrom = cursor`.
- **Verify:**
  - `/input cursor.up` (or real UpArrow) with no prior toggle → cursor plants on the party and reads the tile; again → steps north. `/eval MapCursor.Has` true after first press.
  - `/input cursor.up2` (Shift+Up) steps even with the HUD focused (`/eval Navigation.HasFocus == true`).
  - `/eval` open a service window → `cursor.up`/`scan.item_next` silent (Exploration dropped via `NoControlCats`); close → live. In Pause → cursor steps; `cursor.move_to` → "Paused." (no command).
  - Turn guard: in a TB save with `IsPlayerTurn == false` (or `SelectedUnit != CurrentUnit`), `/input cursor.move_to` → "Not your turn." / "Select your active character."; `/eval` asserts no command issued on `CurrentUnit`.
  - Two-step confirm: on the player's turn, first `cursor.move_to` → "N tiles away, press again to move." + `/eval` no command yet; second within the window → command issued, "Moving."
  - `/input cursor.interact` (Enter) on an empty tile → "nothing here"; `/eval` confirms no move command and no `ui.activate` double-fire; on a door/loot tile → interacts.
  - `/input cursor.recenter` (`C`) snaps a wandered cursor back to the party; `/eval`/`/gui` confirm the game's `C` (inventory) did **not** open (focus-mode suppression holds).
  - `/gui` HUD intact; no double-speech.

### Phase R2 — WA two-cursor coupling + verb pairs + unified readout + camera (S–M)
**Why:** wires the Pathfinder coupling onto the now-always-active cursor.
- **`InputBindings.cs`:** rebind `scan.interact` from `End` → **`I`** (`End` freed); **split the existing merged `scan.inspect` (bare `K`) into `inspect.review` (`Y`) + `inspect.cursor` (`'`)** (frees `K`); **free `Home` by moving `scan.where_am_i` → `X`** (WA-parity) so `scan.cursor_to_item` can bind `Home` + `Slash`. Confirm review-group keys (Comma/Period/N/M, Shift = reverse) and item/category keys exist (Phase B). **Collision note (verified):** RT today still has `scan.where_am_i` on `Home`, `scan.inspect` on bare `K`, `scan.interact` on `End`, and the NVDA-violating `scan.move_to` on bare `Insert` — all four move/drop across R1/R2.
- **`Scanner.cs`:** ensure `DoCycleReview`/`Select` change the **selection only** (no `MapCursor.Set`); `ScanFrom()` returns the cursor (Phase A) — keep; add `CommitCursor` (Home/Slash → `MapCursor.Set(selection.Position)` null-guarded + camera); `InteractSelected` (`I`) and the new `InteractAtCursor` (`Enter`) resolve selection vs cursor occupant; both honor `Targeting.Aiming`.
- **`MapCursor.cs`:** add the null-guarded `Set(Vector3)` overload (§3.5).
- **`Inspect` (new, RT-contract):** `inspect.cursor`/`inspect.review` open the game Inspect window for the cursor-occupant / review-selection unit, gated by `InspectUnitsHelper.IsInspectAllow`, via the `rt-inspect-contract` path.
- **`InteractableDescriber.cs`:** one composer for both readouts — cursor-tile (arrow-step) and selected-item (review-cycle), spatial tail = `DirectionAndDistance(cursorPos, target)` (metres + compass), browse path appends `"N of M"`.
- **Settings:** add `exploration.camera_follow` (Off/On, default On), read by the shared `ScrollTo`.
- **Fold the area-scoped exits + POI review groups now (closes WA `V`/`B`):** register `scan.review_exits` (**V** / Shift+V reverse) and `scan.review_poi` (**B** / Shift+B reverse) as `InputCategory.Exploration`, `.Grouped("scanner")` cycles in `InputBindings.cs`, routed through `Scanner.DoCycleReview` so they sort nearest-first from the (stationary) cursor, land as the review selection, and Home/Slash plants the cursor on them — unlike `[`/`]`, which are raw-polled, merged, and non-reversible. Source them from `LocalMapModel.Markers` (the same markers `LandmarkNav` reads), filtered by `LocalMapMarkType`: Exit + area-transition → exits; Poi/Loot/objective → POI (`LandmarkNav.cs:52-58`, `InteractableDescriber.MarkerTypeLabel:168-179`). Keep `[`/`]`/`\` (`LandmarkNav`) as the quick merged ring + walk-to. True per-room scoping stays deferred to echoing Phase J (no `RoomMap`).
- **Add a scanner-selection re-announce (closes the announce gap on the selection side):** R1 already covers WA's `K` "announce-cursor" as `cursor.reannounce` / **Delete** (re-reads the cursor tile), but no verb re-speaks the current scanner *selection* (`Scanner._selectedKey`/`Selected`). Add `scan.announce_selection` re-speaking the selection through the shared readout composer. WA's `K` is freed by the inspect split, but bind a free key (`O`/`U`/`Z`, or Shift+Delete) to avoid re-coupling announce to a key the player now reads as inspect-history — **not** K.
- **(Optional WA-parity) announce-party alias on Shift+K:** announce-party already ships as `scan.party` / **P** (`Scanner.ReadParty`); Shift+K is verified free (only bare K is bound, exact-modifier match), so add it as a parity alias if desired — no behavior change.
- **Verify:**
  - `/input scan.item_next` changes the selection and speaks it, but `/eval MapCursor.Node` is **unchanged** (cursor parked).
  - `/input scan.cursor_to_item` (Home) → `/eval MapCursor.Node` is now the selection's nearest node; camera scrolled (if On).
  - `Enter` vs `I`: park the cursor on empty ground, review-select an enemy; `/input cursor.interact` (Enter) → "nothing here"; `/input scan.interact` (I) → interacts with the enemy. `'` inspects the cursor unit (none → "nothing to inspect"); `Y` inspects the enemy.
  - Anti-ping-pong: from a parked cursor, `scan.review_enemies` repeatedly → `/eval` distances monotonic from the (stationary) cursor; no bounce.
  - Off-grid plant: Home onto a flying/elevated item → `/eval MapCursor.Node` unchanged (not nulled); item still read from the cursor.
  - `exploration.camera_follow`: review cycles never scroll; arrow-steps, Home plant, and `C` do (when On).

### Phase R3 — party selection + combat status (S–M)
**Why:** R1/R2 wire move-to so selection cardinality decides single-vs-formation in real-time exploration (`UnitCommandsRunner.cs:354-394`), but RT's a11y member-select (`PartyHotkeys` Alt+1..6, Shift+A/D) **collapses the selection to one unit** (`SelectionManagerBase.SelectUnit`, single = replace) and offers **no direct key to restore the whole party** — only a multi-Tab trip into the compass-corner Menu region (`InGameScreen.cs:229`). Once a blind player picks a member, formation move-to is effectively unrecoverable. Separately, WA's `R` combat-status has no one-press RT verb: the acting unit's AP/MP/turn is computed in `InGameScreen.StatusLine()` (`:341-369`) and `UnitBuffer` (`:36-39`) but reachable only passively (enter HUD focus, or step Alt-buffer lines). Both are real, high-value, and cheap (data + locale stubs already exist).

- **`InputBindings.cs`:** register **`party.select_all`** (**Ctrl+A**, WA-parity — safe: the game's own Ctrl+A is suppressed while focus mode holds `Game.Instance.Keyboard.Disabled`, `FocusMode.cs:24-38`, and the mod's focus toggle is Ctrl+Shift+A, `:23`) → `SelectionManagerBase.Instance.SelectAll()`, `InputCategory.Exploration`; register **`combat.status`** (**R** — free per project keymap; K is taken) → speaks the AP/MP/turn content of `InGameScreen.StatusLine()`, `InputCategory.Exploration` (or Global gated `InAGame()`). Optional convenience: **`party.hold`** (**H**) → `SelectionManagerBase.Instance.Hold()` and **`party.stop`** (**G**) → `Stop()` (both already exist as Menu buttons, `InGameScreen.cs:227-228`). Reuse the existing `bind.combat.status` / `bind.party.*` locale labels; **do NOT** wire the Pathfinder `combat.status_actions` / `combat.movement_remaining` strings (`ui.json:125,128`) — use RT terms (`ActionPointsYellow` = AP, `ActionPointsBlue` = MP/cells).
- **`PartyHotkeys.cs` (or a small new handler):** add `SelectAll` and the `combat.status` readout (extract the AP/MP/turn tail of `StatusLine()` into a shared method so the hotkey and the HUD region read identical phrasing).
- **Move-to selection-cardinality announce (coupling refinement):** in R1's unified move helper, before committing the **exploration (real-time)** move, read `Game.Instance.SelectionCharacter.SelectedUnits.Count` and announce **"Moving party"** (formation) vs **"Moving \<name\>"** (single) — RT gives the blind player no visual selection feedback, so this is the only cue that a stray Alt+1 silently turned a formation move into a single-unit move.
- **Explicitly NOT ported:** WA's `Ctrl+S` stealth and `Ctrl+D` AI — RT has no group stealth toggle (stealth is per-unit `PartUnitStealth`) and combat is fully turn-based (no AI handoff). Drop them; do not forward-port stubs. WA's `Ctrl+T` TB↔real-time toggle is N/A for the same TB-only reason.
- **Verify:**
  - `/input party.select_all` (Ctrl+A) after picking a member with Alt+1 → `/eval Game.Instance.SelectionCharacter.SelectedUnits.Count` == full party; `/gui` confirms the game's own Ctrl+A select-all did **not** double-fire (focus-mode suppression holds).
  - Pick one member → `cursor.move_to` speaks "Moving \<name\>"; `party.select_all` → `cursor.move_to` speaks "Moving party"; `/eval` confirms a formation layout via `PartyFormationHelper`.
  - `/input combat.status` (R) on the player's turn in TB combat → speaks "\<AP\> AP, \<MP\> MP, \<unit\>'s turn" with no HUD-focus trip; `/eval` confirms it reads `StatusLine()` content; outside combat → benign (no AP/MP appended).
  - Optional: `/input party.hold` (H) / `party.stop` (G) issue Hold/Stop; `/gui` confirms no game-key leak.

---

## 6. Risks & open questions

**Needs in-game / harness verification:**
- **Focus-mode suppression of game hotkeys (load-bearing for `C`/`Backspace`/`Enter`/`'`/`Y`/Home):** the maintainer states the game's own World-mode hotkeys don't fire while the mod's exploration owns input. **Confirm in-harness** that `C` doesn't open the character screen, `Backspace` doesn't trigger a game action (game `Ctrl+Backspace` = SelectAll), `Enter` fires one handler (no `ui.activate` double-fire), and `'`/`Y`/Home don't leak. If suppression is partial, fall back recenter/inspect/plant to confirmed-free keys (`O`/`U`/`Z`); the scanner letters (N/M/P/K/comma/period) coexisting is the precedent.
- **Paused move queuing:** confirm whether `MoveSelectedUnitsToPoint` → `…RT` queues an order during Pause that fires on unpause. Default: **block** the exploration move while paused; relax if harness shows a paused queue is harmless.
- **`EnsurePlanted` anchor null:** `GetAnchor()?.CurrentUnwalkableNode` is `[CanBeNull]` only when the unit is off-grid (mid-teleport / not spawned). Confirm an anchor + valid node exist in normal exploration; "No reference point." otherwise.
- **Off-grid / footprint-mismatch plant:** Home-plant's nearest node may fall outside `GetOccupiedNodes()` for large/irregular items. R2 plants on the nearest *occupied* node when a footprint is available; pre-Phase-C falls back to `GetNearestNodeXZ()`. `I`/`Y` (item-targeted) sidestep this entirely — confirm they act on the object even when the cursor's node is just outside the footprint.
- **Inspect mechanism:** `inspect.cursor`/`inspect.review` use the `rt-inspect-contract` path, which differs from WA's `TooltipHelper`. Confirm it opens for a valid unit and says "nothing to inspect" otherwise.

**Confirmed by review (no further action):**
- Exact-modifier chord-shadowing delivers the arrow ownership (plain → cursor unfocused / nav focused; Shift+arrows → cursor in both). `ModifiersMatch` is exact.
- WA two-cursor semantics confirmed in code: review never moves the cursor (`Main.cs:696-698`, `Scanner.cs:479`); Home/Slash plants (`Scanner.cs:105,575`); `ScanFrom` = cursor → stable sort.
- The gate change is safe; net delta = cursor live in Pause.
- Freeing bare Insert is a real NVDA win; all cursor keys are NVDA-safe.
- WA-parity bindings (in-area explorer): `explore.interact` = Return+KeypadEnter (`Main.cs:455-457` → `InteractAtCursor`), `scan.interact` = `I` (`:531` → `InteractSelected`), `scan.moveToCursor` = Backspace (`:539-540`), `inspect.cursor` = `'` / `inspect.review` = `Y` (`:535-538`), `scan.cursorToItem` = Home/Slash (`:523-526`), `overlay.recenter` = `C` (`:688`), `explore.cancelTargeting` = Escape (`:468-477`). Both Enter and Backspace carry a targeting dual-role (`Scanner.cs:114,122,128`).

**Maintainer decisions:**
- TB confirm window length (≈3 s) and whether to also gate exploration moves (default: TB-only).
- Whether to fold the exits/POI review groups into R2 or keep `[`/`]` (`LandmarkNav`) as the sole exit nav until RoomMap.
- `exploration.camera_follow` default (On).

---

## 7. Implementation progress

> Living tracker. Each phase is **build-verified** (`dotnet build RTAccess.slnx -c Debug` → 0/0) as it lands; in-game harness verification is batched and flagged below where pending.

### R1 — Always-active cursor + registered, guarded controls — **CODE-COMPLETE, build 0/0, adversarially reviewed; in-harness verification PENDING** (2026-06-30)

**Shipped:**
- **Toggle removed.** `TileExplorer` dropped `_active` / `Update()` (raw poll) / `Toggle()` / `Deactivate()`; `Main.OnUpdate` no longer calls `TileExplorer.Update()`. The cursor is now driven by `InputManager.Tick` via registered `InputCategory.Exploration` actions and planted **lazily** (`EnsurePlanted(out fresh)`): the first arrow / re-announce / move-to plants on the anchor and **reads** the tile rather than acting (a cold press never walks the party onto its own tile).
- **Registered controls** (`InputBindings.cs`, all `InputCategory.Exploration`, `.Grouped("cursor")`): `cursor.up/down/left/right` (plain arrows, `.Repeating()`, PRIMARY — shadowed to `ui.*` when the HUD is focused); `cursor.up2/down2/left2/right2` (Shift+arrows, `.Repeating()`, SECONDARY — shadow-immune); `cursor.recenter` (C); `cursor.reannounce` (Delete); `cursor.move_to` (Backspace + Return + KeypadEnter).
- **One guarded move helper** (`TileExplorer.MoveToCursor`) — deduped `Scanner.MoveToSelected`/`MoveToSelectedTile`/`MoveFailure` into it (those + their two now-dead usings removed from `Scanner`). TB branch: `IsPlayerTurn` + selected == `CurrentUnit` + `IsDirectlyControllable` guard, then a **two-step confirm** (first press announces `N tiles away`, second commits — no preview, spends MP). Real-time branch: pause-guard (`Game.IsPaused`) + anchor guard → `UnitCommandsRunner.MoveSelectedUnitsToPoint`.
- **Dropped** `scan.move_to` / **bare Insert** (NVDA-violating) and its handler.

**Deliberate deviations from the §5 R1 / Appendix A wording (intentional, noted):**
- **`cursor.interact` (Enter → interact-at-cursor) is NOT in R1.** It needs the R2 occupant resolver; binding it now would only duplicate logic. Instead **Enter / KeypadEnter are a temporary move-to alias** alongside Backspace (preserves the old Enter = move-to, no regression). R2 frees Enter for interact and leaves Backspace as the sole move-to.
- **`cursor.cancel` (Escape) deferred to echoing Phase D** — there is no targeting mode yet, so it would only duplicate `ui.back`/Escape.

**Adversarial review (4-lens workflow, each finding verified) → fixes applied:**
- **[FIXED, medium] Stale-arm bypass.** `Move()` didn't clear `_armedNode`, so stepping away and back to an armed tile within 3 s committed the TB move on a single press (`GetNode` returns the same node instance → reference-equal). Now `Move()` clears `_armedNode` on every step, making the documented "re-arms whenever the cursor moves" invariant literally true.
- **[FIXED, low] `C`/`Delete` leaked through HUD focus.** They have no `ui.*` twin, so unlike the arrows/Backspace/Enter they fired the world cursor (camera jump + speech-interrupt) during menu nav. Added `if (Navigation.HasFocus) return;` to `Recenter`/`ReAnnounce` (NOT to `Step` — the Shift+arrow secondary slot must stay shadow-immune).
- **[FIXED, doc] Stale comments** corrected: `Scanner` class doc (Insert references), `MapCursor` doc ("TileExplorer turning on" → lazy self-plant), `ConsoleMode` doc (referenced the deleted `ExplorationNav` + defunct `ControllerMode == Gamepad` gating).
- **[REFUTED, no action]** "Scanner measures from a stale cursor" — this is the *intended* two-cursor coupling (`ScanFrom = cursor`, stable; C is the documented remedy).

**In-harness verification (2026-07-01, save `VoidshipOfficersDeck`, exploration) — PASSED:**
- `/input` drives the registered `cursor.*` actions live (always-active, no toggle) — `fired cursor.X (handler)`.
- **Lazy plant**: the first press read the tile ("clear, here") instead of acting.
- **Stepping**: all four cardinals correct (up=north, down=south, left=west, right=east); offsets compound ("1 east, 1 north"). Readout carries occupant ("Багардор, ally"), walkability ("clear"), and cover ("half cover east").
- **Secondary slot**: `cursor.up2` / `cursor.right2` (Shift+arrows) step — verified live.
- `cursor.recenter` snaps to the party ("Багардор, ally, here"); `cursor.reannounce` re-reads the tile.
- **Real-time move-to**: `cursor.move_to` → "Moving." (party walked to the cursor).
- **FIX 2 confirmed live**: with the HUD focused (`Navigation.HasFocus == True` via `/eval`), `cursor.recenter`/`cursor.reannounce` produced NO speech (suppressed by the focus guard) while the shadow-immune `cursor.up2` still stepped; with the HUD unfocused they work — guard correct in both directions.

**Still untested behaviorally (build- + review-verified only):**
- **FIX 1 / TB two-step confirm + stale-arm clear** — turn-based-only; the test save has no encounter. The fix (`Move()` clears `_armedNode`) is a one-liner with verified logic; re-test at the next combat.
- **Key-level chord shadowing** (plain arrows yield to HUD nav when focused) — `/input` fires by action-id and bypasses the key→action shadow layer, so it can't exercise it; code-verified via `InGameScreen`'s category ordering (same mechanism the shipped scanner already rides).

**Side note (pre-existing, NOT R1):** `ui.back`/Escape does not return focus to gameplay from the top-level in-game HUD (`HasFocus` stays true) — a HUD-nav behavior to look at separately.

---

### R2 — WA two-cursor coupling + verb pairs + object-interaction redesign — **CODE-COMPLETE, build 0/0, adversarially reviewed, in-harness verified** (2026-07-01)

**Shipped (two-cursor coupling + verb pairs):**
- **`MapCursor.Set(Vector3)`** — plant the shared cursor on the grid node nearest a world point; returns `bool` (false when off-graph, keeping the previous node) so callers can tell a plant failed rather than falsely re-announcing the old tile.
- **`Scanner.CursorToSelection`** (Home / Slash) — plant the movement cursor on the current review selection's tile without moving the selection (the coupling core). Off-graph selections say "Can't place the cursor there."
- **Verb pairs**: `cursor.interact` (Enter / KeypadEnter) interacts with the world via the cursor; `scan.interact` (I) interacts with the review selection. `inspect.cursor` (') inspects the cursor occupant; `inspect.review` (Y) inspects the review selection (split from the old merged `InspectTarget`). `scan.where_am_i`→X; `scan.interact` moved End→I; `scan.inspect`/K removed; `cursor.move_to` is Backspace-only.

**Core finding — the tile cursor is a spatial-scan overlay, NOT the object-interaction primitive (workflow-verified, high confidence):**
- Exploration is **real-time with continuous positions** (a commanded unit stops ~0.61 m off the nearest cell centre); only combat is tile-based. The `CustomGridGraph` is the pathfinding/occupancy substrate but does not quantise movement.
- **Interactable objects live in continuous world-space, not slotted one-per-tile.** Source analysis (5 lenses) + a live probe agreed: an object's `Position` is its level-authored view transform, off any cell centre by up to ~0.95 m (the cell-corner distance). The live probe of `VoidshipOfficersDeck` measured 41 interactables averaging 0.464 m off-centre; doors/skill-checks sit exactly on cell edges (½ cell) and corners (shared by 4 tiles). Objects can span multiple cells or occupy none.
- **Interaction is world-space, not grid-based.** The engine resolves a click by raycast→collider→entity (mouse) or nearest-in-cone `EntityBoundsHelper.FindEntitiesInRange` (console); there is no per-tile object index (nodes track only *unit* occupancy). A tile-stepping cursor structurally cannot land on objects.

**Object-interaction redesign (Enter = "nearest object", using the game's own dispatch):**
- **Selection** — `InteractableDescriber.InteractableAt(node)` is now a **proximity query**: nearest `MapObjectEntity` within `InteractReach` (1.5 cells) of the cursor, gated by the game's own `ClickMapObjectHandler.HasAvailableInteractions` (+ `AreaTransitionPart` for exits) — mirroring the console interaction picker. Replaces the broken `GetOccupiedNodes().Contains(node)` footprint test (which missed off-centre/edge/zero-cell objects — e.g. a door-in-a-wall whose containing cell is unwalkable while the cursor snaps to the adjacent floor cell).
- **Dispatch** — `ProxyMapObject.Interact()` now calls **`ClickMapObjectHandler.Interact(view, SelectedUnits, forceOvertipInteractions: true)`** — the game's own click dispatch (unit-selection via `SelectUnit`, Direct-vs-Approach, the already-close short-circuit, trap handling, AP/range warnings). Exits keep the `AreaTransitionHelper.StartAreaTransition` special-case. Benefits both cursor-Enter and scanner-I; matches the WrathAccess pattern verbatim.
- **Readout** — `DescribeTile` now **always announces the nearby interactable** (even behind a unit headline), using the same resolver, so the tile readout and what Enter fires never diverge.

**Adversarial review (4-lens × per-finding verify workflow: 14 confirmed / 1 plausible / 2 refuted) → fixes applied:**
- **[FIXED, medium] `scan.interact` (I) lost its focus-shadow** — bare I had no UI twin, so it mutated the world while the HUD was focused. Added `if (Navigation.HasFocus) return;` to `InteractSelected`.
- **[FIXED, medium] `InteractableAt` skipped area-transition exits** (no `InteractionPart`) — now handled by the `AreaTransitionPart` branch in `IsActionable`.
- **[FIXED, medium] `DescribeTile`/`InteractAtCursor` divergence** on shared unit+object tiles — `DescribeTile` always announces the object Enter would fire.
- **[FIXED, low] `PlantOn` couldn't detect an off-graph `Set` no-op** — `Set(Vector3)` returns bool; `PlantOn` says "Can't place the cursor there."
- **[FIXED, low] `Scanner.SelectedUnit()` returned a stale cross-area unit** — now resolves through the live `WorldModel.Snapshot` (`ResolveSelected()?.Key`).
- **[FIXED, doc] Comment rot** — `Scanner.cs` / `InputBindings.cs` (Home/End → Home), `Inspect.cs` (merged-target → two-key split), `ConsoleMode.cs` / `Main.cs` (tile-cursor gate + Enter's role).
- **[REFUTED, no action]** frozen scan-origin (intended two-cursor contract); one duplicate `Main.cs` reading.

**In-harness verification (2026-07-01, save `VoidshipOfficersDeck`) — PASSED:**
- Plant cursor on a door (Home) → tile reads **"Дверь, activate, 11 east, 5 south"** (proximity resolver names the off-grid door; pre-fix read "clear, …").
- Enter (`cursor.interact`) → **"Interacting with Дверь."** via `ClickMapObjectHandler` dispatch; the door opened and combat engaged (real approach-then-interact).
- Recenter on party (C) → **"Багардор, ally, here"** — no spurious naming of the 17 m door (reach-limited); clear-tile readout intact.

**Deferred (later R2 waves):** `exploration.camera_follow` setting; POI/exits review groups (V/B); `scan.announce_selection`; unified readout composer; WA's lockpick `ILockpickUIHandler` special-case for locked objects.

---

### R3 — party selection + combat status + move-to cardinality — **CODE-COMPLETE, build 0/0, in-harness verified** (2026-07-01)

**Shipped (four registered `InputCategory.Exploration` actions + a coupling refinement):**
- **`party.select_all`** (**Ctrl+A**) → `SelectionManagerBase.Instance.SelectAll()` — the one-press way back to a formation move-to after `PartyHotkeys` Alt+1..6 / Shift+A/D collapsed the selection to a single member. Announces "Whole party selected, N characters." (N > 1) or "Party selected." Ctrl+A is free: focus mode suppresses the game's own Ctrl+A and the mod's focus toggle is Ctrl+Shift+A.
- **`combat.status`** (**R**) → speaks `InGameScreen.StatusLine()` (name + wounds +, in TB, AP / MP / whose-turn) with no HUD-focus trip. `StatusLine()` widened `private`→`internal static` so the hotkey and the HUD status element read identical phrasing (no duplicated logic).
- **`party.hold`** (**H**) → `SelectionManagerBase.Instance.Hold()` ("Holding position."); **`party.stop`** (**G**) → `Stop()` ("Stopped.") — the two optional Menu-button parities, now on keys.
- **Move-to cardinality announce** — `TileExplorer.MovingAnnounce()` reads the live `SelectionCharacter.SelectedUnits` the **real-time** branch actually drives: "Moving party." (Count > 1) vs "Moving \<name\>." (single, falling back to the anchor name when the collection is empty). The only cue a blind player gets that a stray Alt+1 turned a formation move into a single-unit one. TB branch keeps its own "Moving." (single active unit, two-step confirm).
- **Host:** all four handlers are public static methods on `PartyHotkeys` (which already owns the raw-polled member selector); registered in `InputBindings.cs` under the `"party"` settings subgroup.
- **Explicitly NOT ported** (per plan §5): WA's `Ctrl+S` stealth (RT stealth is per-unit `PartUnitStealth`, no group toggle) and `Ctrl+D` AI handoff (RT combat is fully TB); `Ctrl+T` mode-toggle (TB-only N/A). No forward-ported stubs.

**In-harness verification (2026-07-01, latest save loaded directly into TB combat — the encounter R1/R2 saves lacked):**
- `/input combat.status` (R) on the player's turn → **"Багардор, 22 of 22 wounds, 4 AP, 0 MP, Багардор's turn"** — the full TB action-economy line via the now-`internal` `StatusLine()`. (First live exercise of the TB branch; R1/R2 could only build-verify it.)
- `/input party.select_all` → **"Party selected."** (this encounter has a single controllable → the singular branch, correct); `/input party.hold` → **"Holding position."**; `/input party.stop` → **"Stopped."** — all three drive `SelectionManagerBase` and announce.
- `/eval` confirmed `SelectedUnits.Count == 0` in TB combat (turn-based tracks the active-turn unit, not the multi-select collection) — expected, and why `MovingAnnounce` (real-time-only) falls back to the anchor name when the collection is empty.

**Build+reasoned-verified only (needs a multi-select exploration save, not this single-unit TB combat):** the `party.select_all` "Whole party selected, N" (N > 1) branch and the `MovingAnnounce` "Moving party." path. The chain is sound: in PC/real-time mode `SelectAllImpl` synchronously populates `SelectedUnits` with the whole selectable party (`SelectionManagerPC.cs:67`), which `UnitCommandsRunner.MoveSelectedUnitsToPoint` then walks — same collection `MovingAnnounce` reads. Same class of deferral as R1's TB two-step-confirm.

---

## Appendix A — WA → RT explorer key audit

Every WrathAccess (Pathfinder) in-area-explorer key, grouped, with its RT status **verified against the current RT source** (not from memory; via a 5-reader verification pass). Status tags: `shipped <id/key>` (live in RT) · `R1`/`R2`/`R3` (this plan closes it) · `echoing <phase>` (a later `echoing-charting-lovelace.md` phase) · `N/A` (no RT equivalent — TB-only or feature-absent) · `gap` (unported, no phase yet) · `verify` (reports unsure). Where a key ships on a different RT key, the RT key is given.

### Global
| Key | WA action | RT status |
|---|---|---|
| Ctrl+Shift+A | toggle focus mode | shipped: `toggle_focus` / Ctrl+Shift+A (exact) |
| Ctrl+Shift+T | speak test | shipped (key differs): **F12** self-test (raw, `Main.cs:161`) |
| F5 | quicksave | gap (game-native F5; no mod binding) |
| Ctrl+M | mod menu | gap (RT uses UMM's own menu) |

### UI / nav
| Key | WA action | RT status |
|---|---|---|
| Arrows | navigate | shipped: `ui.up/down/left/right` (exact) |
| Tab / Shift+Tab | next / prev region | shipped: `ui.next` / `ui.prev` (exact) |
| Enter / KeypadEnter | activate | shipped: `ui.activate` (Return + KeypadEnter) |
| Backspace | secondary | shipped: `ui.secondary` (exact) |
| Escape | back | shipped: `ui.back` (exact) |
| Space / F1 | tooltip | shipped: `ui.tooltip` (Space + F1) |
| Home / End | first / last | shipped: `ui.home` / `ui.end` (exact) |
| Ctrl+Up / Ctrl+Down | sheet-region prev / next | gap |

### Exploration cursor
| Key | WA action | RT status |
|---|---|---|
| Arrows | step (primary) | shipped raw (TileExplorer, Ctrl+T-toggled); **R1** → always-active + registered `cursor.up/down/left/right` |
| WASD | step (primary) | N/A (RT binds no WASD; arrows are the primary slot — intentionally not ported) |
| Shift+Arrows | step (secondary) | shipped raw (TileExplorer Shift+arrows); **R1** → registered `cursor.*2` (shadow-immune) |
| Shift+WASD | step (secondary) | N/A (no WASD) |
| Enter / KeypadEnter | interact-at-cursor | gap (RT cursor Enter currently = move-to, not interact); **R1** adds `cursor.interact` / Enter+KeypadEnter |
| Backspace | move-to-cursor | shipped (key differs): TileExplorer Enter (while toggled) / `scan.move_to` bare Insert (party→selection); **R1** → guarded `cursor.move_to` / Backspace (TB two-step) |
| X | where-am-i | shipped (key differs): `scan.where_am_i` / **Home** → **R2 moves it to X** (frees Home for `scan.cursor_to_item`) |
| Space | pause | N/A — TB-only (no real-time pause) |
| Escape | cancel-targeting else menu | shipped (partial): `ui.back` / Escape + `InGame`-category Escape survives loss of control (`InGameScreen.cs:60-63`); explicit cancel-targeting-FIRST priority not yet a distinct binding (**verify**); **R1** adds `cursor.cancel` (Escape, InGame) |

### Party
| Key | WA action | RT status |
|---|---|---|
| Ctrl+A | select-whole-party | gap at hotkey layer — menu-only ("Select whole party" → `SelectionManagerBase.SelectAll()`, `InGameScreen.cs:229`); **R3** registers `party.select_all` / Ctrl+A (load-bearing for formation move-to) |
| Ctrl+1..6 | select-member | shipped (key differs): `PartyHotkeys` **Alt+1..6** (raw, `PartyHotkeys.cs:34-42`); + Shift+A/D step prev/next (RT extra) |
| H | hold | partial: menu-only ("Hold position" → `Hold()`, `InGameScreen.cs:227`); no hotkey; **R3** optional `party.hold` / H |
| G | stop | partial: menu-only ("Stop" → `Stop()`, `InGameScreen.cs:228`); no hotkey; **R3** optional `party.stop` / G |
| Ctrl+S | stealth | N/A — no group stealth toggle in RT (stealth is per-unit `PartUnitStealth`); drop, don't stub |
| Ctrl+D | AI | N/A — combat fully TB, no companion-AI handoff; drop, don't stub |

### Combat
| Key | WA action | RT status |
|---|---|---|
| Ctrl+T | toggle TB ↔ real-time | N/A — TB-only (no real-time mode). Ctrl+T currently = TileExplorer toggle, which **R1** removes |
| R | combat-status (action economy + movement) | gap — no one-press AP/MP/turn verb; data exists only passively (HUD region `InGameScreen.cs:341-369` StatusLine + buffer line `UnitBuffer.cs:36-39`); **R3** adds `combat.status` / R (cheap; reuse RT terms `ActionPointsYellow`=AP, `ActionPointsBlue`=MP) |

### Scanner
| Key | WA action | RT status |
|---|---|---|
| PageDown / PageUp | item next / prev | shipped: `scan.item_next` / `scan.item_prev` (exact) |
| Ctrl+PageDown / Ctrl+PageUp | category next / prev | shipped: `scan.cat_next` / `scan.cat_prev` (exact) |
| Shift+PageDown / Shift+PageUp | subcategory | N/A — RT scanner is two-level (item + category); no third subcategory level |
| Home + Slash | plant cursor on selection | gap — `scan.move_to`/Insert moves the *party*, not a cursor; **R2** adds `scan.cursor_to_item` / Home+Slash (the coupling's core; Home freed from where-am-i) |
| K | announce-cursor | cursor-tile re-read shipped via **R1** `cursor.reannounce` / **Delete**; WA's K is occupied by RT `scan.inspect` (split off in R2). No scanner-*selection* re-announce verb (gap) → **R2** `scan.announce_selection` (free key, not K) |
| Shift+K | announce-party | shipped (key differs): `scan.party` / **P** (`Scanner.ReadParty`); Shift+K verified free → **R2** optional WA-parity alias |
| I | interact-selection | shipped (key differs): `scan.interact` / **End**; **R2** rebinds End → I (WA-parity) |
| Backspace | move-to-cursor | shipped (key differs): `scan.move_to` / **bare Insert** (NVDA-violating, still live); **R1** drops Insert, move-to → Backspace `cursor.move_to` |
| F8–F11 | debug | shipped (partial, keys differ): **F9** Rewired dump / **F10** keybindings dump / **F12** self-test (+ F6 mode toggle, F7 reread), raw `Main.cs:144-161` |

### Inspect
| Key | WA action | RT status |
|---|---|---|
| Y | inspect review-selection unit | shipped (key differs, merged): `scan.inspect` / **K** — scanner-selection first (`Inspect.cs:29`); **R2** splits to `inspect.review` / Y |
| ' (Quote) | inspect cursor unit | shipped (merged into `scan.inspect` / **K** — falls back to the tile-cursor occupant); **R2** splits to `inspect.cursor` / ' |

### Review cycles (Shift = reverse)
| Key | WA action | RT status |
|---|---|---|
| Comma | party | shipped: `scan.review_party(_back)` / Comma (+ Shift) |
| Period | enemies | shipped: `scan.review_enemies(_back)` / Period (+ Shift) |
| N | neutrals | shipped: `scan.review_neutrals(_back)` / N (+ Shift) |
| M | objects / others | shipped: `scan.review_objects(_back)` / M (+ Shift) |
| B | POI | partial (key differs): covered by `LandmarkNav` `[`/`]` merged ring (markers incl. POI), raw-polled, non-reversible; **R2** folds area-scoped `scan.review_poi` / B |
| V | room-exits | partial (merged, no dedicated key): `LandmarkNav` `[`/`]` surfaces Exit/transition markers area-scoped (no `RoomMap`); **R2** folds area-scoped `scan.review_exits` / V — true per-room deferred to echoing Phase J |

### Buffers
| Key | WA action | RT status |
|---|---|---|
| Alt+Left / Alt+Right | prev / next buffer | shipped: `buffer.prev` / `buffer.next` / Alt+Left/Right (exact; Global + ungrouped) |
| Alt+Up / Alt+Down | prev / next line | shipped: `buffer.line_prev` / `buffer.line_next` / Alt+Up/Down (exact). Caveat: RT v1 ships only 2 buffers (Selected unit + Target) — narrower than WA |

### Camera
| Key | WA action | RT status |
|---|---|---|
| Alt+WASD | pan | gap (no camera-control bindings) |
| Alt+Q / Alt+E | rotate | gap |
| Alt+F | follow | gap |

(RT's camera handling is the planned `exploration.camera_follow` setting (**R2**) that auto-follows the movement cursor — not WA's manual pan/rotate/follow verbs.)

### Service windows (`InputCategory.Windows`)
| Key | WA action | RT status |
|---|---|---|
| Ctrl+C | character sheet | gap at mod-binding layer: `Windows` category has **zero registered actions**; opens only via game console-mode keyboard or the HUD "Windows" region; mod merely announces (`ServiceWindowAnnounce.cs:17`). Window-nav screens shipped, open-hotkey not |
| Ctrl+I | inventory | gap + **collision**: Ctrl+I is repurposed to "read details of focused element" (`Main.cs:138`); not bound to inventory |
| Ctrl+B | spellbook | N/A — RT has no spellbook (no `ServiceWindowsType.Spellbook`); `bind.window.spellbook` is a moot stub |
| Ctrl+J | journal | gap (no open hotkey; HUD region / game keyboard only) |

### Overlays
| Key | WA action | RT status |
|---|---|---|
| Ctrl+O | cycle overlay | gap — RT has no overlay/sonar/walltone subsystem (echoing Phase E) |
| Ctrl+F1 / Ctrl+F2 | cycle walltones / sonar mode | gap (echoing Phase E) |
| Shift+F1 / Shift+F2 | hold-to-force | gap (echoing Phase E) |
| C | recenter | gap as *overlay*-recenter; note **C is taken by `cursor.recenter`** (R1, cursor↔party recenter) |
| Keypad5 | announce | gap (echoing Phase E) |
| Ctrl+Period / Ctrl+Comma | vertical-follow down / up | gap (echoing Phase E) |

### World map (`InputCategory.WorldMap` — parallel explorer)
| Key | WA action | RT status |
|---|---|---|
| (all: own scanner / cursor / review cycles / travel / overlay) | entire world-map explorer | gap — **entirely unported**. `WorldMap` enum exists (`InputCategory.cs:30`) but no screen declares it and no action is registered. Out of scope for this plan (echoing Phase J) |

> **Audit caution (verified):** the locale keys `bind.combat.*` / `bind.party.*` / `bind.camera.*` / `bind.overlay.*` / `bind.window.*` and `combat.status_actions` / `combat.movement_remaining` (`ui.json:125,128`) are **dead stubs** — forward-ported WA label placeholders referenced by zero `.cs` files, some still in Pathfinder phrasing. Do **not** read them as evidence those features ship.
