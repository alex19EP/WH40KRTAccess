# Exploration & interaction accessibility (keyboard interactable navigator)

## Context

RTAccess today makes **menu/dialogue-style UI** accessible — it rides the game's console
focus ring (`SetFocusedPatch` → `UiTextReader`) and adds keyboard shortcuts for service
windows, party selection, dialogue cues, and radial menus. That is enough to get *into* a
conversation, but **not enough to play through the opening area**: a blind player still can't
move through the ship, find/reach doors, loot, NPCs and exits, or hear that an area changed.
A grep of `RTAccess/` confirms there is currently **zero** code for movement, interaction,
map objects, or area transitions.

This plan adds the first half of that gap — **exploration + interaction** (combat is a
separate, larger build). The opening prologue is dialogue-heavy but still requires reaching
interactables and at least one area transition, so this unblocks traversal of the start area.

### Why this is small: reuse the game's own console interactable system

In forced console mode the game *already* finds, filters (fog + availability), sorts, and lets
you cycle nearby interactables — we only add keyboard entry points + speech. Verified paths:

- `Kingmaker.Code.UI.MVVM.View.Surface.InputLayers/SurfaceMainInputLayer.cs`
  - `public static SurfaceMainInputLayer Instance { get; }`
  - `public virtual void OnNextInteractable()` / `OnPrevInteractable()` — cycle the chosen object
    (bound in-game to gamepad bumpers actions 14/15, which the keyboard does **not** reach — so
    we call them directly, the existing `PartyHotkeys`/`WindowHotkeys` pattern).
  - `public void OnInteract()` — walks the selected unit to the chosen object and raises its
    interaction (bound to Confirm action 8; Enter may already trigger it in-world).
  - `TryInvokeUpdateHandle` raises, per object, the announce event below whenever the set/choice
    changes (`SurfaceMainInputLayer.cs:507`). It only re-raises when the set or chosen actually
    changed (`:470` guard), so we won't get idle per-frame spam — but the chosen object legitimately
    changes as you walk, so we dedupe on the chosen entity reference.
- Announce event: `Kingmaker.PubSubSystem/ISurroundingInteractableObjectsCountHandler.cs`
  `void HandleSurroundingInteractableObjectsCountChanged(EntityViewBase entity, bool isInNavigation, bool isChosen)`.
  Subscribe via `EventBus.Subscribe(object)` (`Kingmaker.PubSubSystem.Core/EventBus.cs:31`,
  returns `IDisposable`; `Unsubscribe` at `:42`). A plain object implementing the interface works
  (as `SurfaceMainInputLayer` itself does).

This is the same architecture already used elsewhere in the mod (console-mode-gated keyboard
updaters + Harmony/EventBus reads), so it slots in cleanly and keeps all speech on
`Speaker.Speak(..., interrupt: false)` per [[rt-interrupt-speech-rule]].

## Keys (user-chosen)

A coherent nav-key cluster, all confirmed free in [[rt-keyboard-usage]], console-mode gated so
they don't clash with mouse-mode play:

| Key | Action |
|-----|--------|
| `PageUp` | Previous interactable (`OnPrevInteractable`) |
| `PageDown` | Next interactable (`OnNextInteractable`) |
| `Home` | Re-announce the currently chosen interactable (or "nothing nearby") |
| `End` | Interact — walk to & use the chosen (`OnInteract`); Enter likely also works |

## Approach

### 1. NEW `RTAccess/Accessibility/InteractableDescriber.cs` — spoken-string builder

Static helper: `string Describe(EntityViewBase entity)` → e.g. `"Door, approach, 8 metres, ahead-left"`.
There is **no** generic name property and **no** localized verb strings, so we replicate the small
mapping `OvertipMapObjectVM.UpdateObjectData()` already uses (`OvertipMapObjectVM.cs:300+`):

- **Name** from `entity.Data` + `entity.InteractionComponent` (`EntityViewBase.InteractionComponent`,
  `EntityViewBase.cs:132` — first `InteractionPart`):
  - unit (`entity.Data as BaseUnitEntity`, or `entity.GetComponent<UnitEntityView>()?.Data`,
    `UnitEntityView.cs:181`) → `BaseUnitEntity.CharacterName`.
  - `InteractionDoorPart` → `Game.Instance.BlueprintRoot.LocalizedTexts.UserInterfacesText.Tooltips.Door`
    (open/closed via `InteractionDoorPart.GetState()` for the description).
  - `InteractionLootPart` → `InteractionLootPart.GetName()` (`InteractionLootPart.cs:452`).
  - `InteractionStairsPart` → `...Tooltips.Ladder`.
  - `InteractionActionPart` → `InteractionActionPart.Settings.DisplayName`.
  - trap parts → `...Tooltips.Trap`; else fall back to `entity.GameObjectName`.
- **Verb** from `InteractionPart.UIInteractionType` (`InteractionPart.cs:101`; enum
  None/Action/Move/Info/Credits/Pets) → hardcoded English map (Action→"activate", Move→"approach",
  Info→"examine", …).
- **Distance + direction** (full-v1): `Vector3` from `Entity.Position` (`Entity.cs:220`) for both the
  chosen entity and `Game.Instance.SelectionCharacter.SelectedUnit.Value`. Distance = planar (XZ)
  metres, rounded. Direction = angle between (entity − character) and the **camera forward** projected
  to XZ, bucketed 8-way (ahead / ahead-right / right / behind-right / behind / …). Camera forward
  resolved at build time via the game's camera rig (e.g. `CameraRig.Instance`) or `Camera.main`;
  if unavailable, omit direction gracefully (name + verb + distance only).

### 2. NEW `RTAccess/Accessibility/ExplorationEvents.cs` — EventBus subscriber

A single long-lived object implementing the surrounding-interactables handler **and** the
area/loading handlers, subscribed once at load:

- `ISurroundingInteractableObjectsCountHandler`: on `isChosen == true`, dedupe on the entity
  reference (`_lastChosen`); on a genuinely new chosen entity, `Speaker.Speak(InteractableDescriber.Describe(entity))`.
  Clear `_lastChosen` when the chosen one reports `isChosen == false`. Expose
  `ReannounceCurrent()` (re-speak `_lastChosen`, or "nothing nearby") for the `Home` key.
- Area/loading (full-v1), `Kingmaker.PubSubSystem.Core`:
  - `IAreaActivationHandler.OnAreaActivated()` → speak `Game.Instance.CurrentlyLoadedArea.AreaDisplayName`
    (`BlueprintArea.cs:77`).
  - `IOpenLoadingScreenHandler.HandleOpenLoadingScreen()` → "Loading"; `ICloseLoadingScreenHandler`
    → optional "Loaded".

### 3. NEW `RTAccess/Accessibility/ExplorationNav.cs` — keyboard updater

`static void Update()` called from `Main.OnUpdate`, mirroring `WindowHotkeys`/`PartyHotkeys`:
gate on `Game.Instance != null`, console mode (`ControllerMode == Gamepad`), and exploration
(`Game.Instance.CurrentMode == GameModeType.Default`). Then:
`PageUp` → `SurfaceMainInputLayer.Instance?.OnPrevInteractable()`; `PageDown` → `OnNextInteractable()`;
`End` → `SurfaceMainInputLayer.Instance?.OnInteract()`; `Home` → `ExplorationEvents.ReannounceCurrent()`.
All wrapped in try/catch with `Main.Log` on failure.

### 4. EDIT `RTAccess/Main.cs`

- In `Load` (after `Speaker.Initialize`): create the `ExplorationEvents` singleton and
  `EventBus.Subscribe(it)`; in `OnUnload`: `EventBus.Unsubscribe(it)`.
- In `OnUpdate`: add `ExplorationNav.Update();` next to `WindowHotkeys.Update()` / `PartyHotkeys.Update()`.

## Files

- NEW `RTAccess/Accessibility/InteractableDescriber.cs`
- NEW `RTAccess/Accessibility/ExplorationEvents.cs`
- NEW `RTAccess/Accessibility/ExplorationNav.cs`
- EDIT `RTAccess/Main.cs` (subscribe/unsubscribe + `ExplorationNav.Update()`)
- Reused unchanged: `Speech/Speaker.cs`, `Accessibility/ConsoleMode.cs`.

No publicizer work needed: the methods used are public. `Code.dll` is already publicized in
`RTAccess.csproj` if any private field read becomes necessary.

## Edge cases

- **No character selected** (`SelectionCharacter.SelectedUnit.Value == null`): `OnInteract` no-ops;
  announce "no character selected" rather than silence. Shift+A/D (PartyHotkeys) selects one.
- **0 or 1 interactable**: `OnNext/PrevInteractable` early-return when count ≤ 1; the single one is
  auto-chosen and announced via the event. `Home` covers the "what / nothing" query.
- **Chattiness while walking**: the chosen (closest) object changes as you move, so ambient
  re-announcements are expected and useful; dedupe-on-reference prevents per-frame repeats. If too
  chatty in testing, add a short time/word throttle (note, not built v1).
- **Combat**: target cycling uses a separate `SurfaceCombatInputLayer`; v1 gates to `Default` mode and
  leaves combat to the combat subsystem.
- **Lifecycle**: one subscribe at load / unsubscribe at unload — no per-area churn.

## Verification

Build the **.slnx** (not the .csproj — `$(SolutionDir)` is undefined otherwise), close the game first
so native DLLs unlock; it auto-deploys to the UMM mod folder:

```
dotnet build RTAccess.slnx -c Release
```

The blind dev runs the game (console mode forced by default) and confirms, with `speech_log.txt`
for offline inspection:

1. In the opening area, `PageDown`/`PageUp` step through nearby objects, each spoken as
   name + verb + distance + direction (e.g. "Door, approach, 6 metres, ahead").
2. `Home` re-announces the current pick (or "nothing nearby"); with no character selected it says so.
3. `End` (and/or Enter) walks the selected character to the chosen object and triggers it (door opens,
   loot opens, NPC starts dialogue → existing dialogue reader takes over).
4. Crossing a transition speaks "Loading" then "Entering <area>".
5. Regression: dialogue, service-window keys (I/C/J/M/…), and party selection still work unchanged.

## v2 refinements (from first in-game log review — NOT YET CODED)

The v1 build works end-to-end (area announce + cycle + name/verb/distance/bearing confirmed in
`speech_log.txt`). Two issues surfaced in a crowded hub (Верхние палубы / Upper Decks), with
user-chosen fixes:

1. **Crowd NPCs read as raw GameObject names** (e.g. `BCT_TopOfficersAssistant_005_..._(Clone)`).
   Root cause: they are `LightweightUnitEntityView` → `Data` is `LightweightUnitEntity : AbstractUnitEntity`,
   not `BaseUnitEntity`, so `InteractableDescriber.ResolveName` falls through to `GameObjectName`.
   **Fix:** resolve the name off the common base `AbstractUnitEntity.CharacterName`
   (`LightweightUnitEntity.Name => Blueprint.CharacterName`) instead of `BaseUnitEntity` only.

2. **Filter to "real" interactions** (user choice). The game's *inclusion* test
   (`SurfaceMainInputLayer.TryRefreshInteractableObjectsList`, `:322-410`) is permissive — ambient
   crowd and flavour decor (`FloorDecor` "examine", set-dressing) pass it because they carry trivial
   interactions. Use the game's **overtip-eligibility** (the "should this float a label" decision) as
   the announce filter instead:
   - **Map objects:** keep if any interaction has `InteractionSettings.ShowOvertip == true`
     (mirror `OvertipMapObjectVM.HasInteractionsWithOvertip`), OR it's an exit
     (`AreaTransitionPart` / `AreaTransition` component — always keep). This drops `FloorDecor`-type
     flavour while keeping doors/loot/stairs/levers/exits. (Note: `OvertipMapObjectVM.CheckNeedOvertip`
     is the wrong tool here — it returns true for flavour decor and *false* for area transitions.)
   - **Units:** keep non-extra units (named NPCs / enemies / companions); for `IsExtra` crowd keep only
     if `ExtraUnitShouldHaveOvertip` is true — i.e. `UnitPartInteractions.HasDialogInteractions` or the
     blueprint has `AddLocalMapMarker` (`LightweightUnitOvertipsCollectionView.cs:42,65`). Drops ambient
     crowd, keeps talkable/marked NPCs.
   - Implement as an `InteractableDescriber.ShouldAnnounce(EntityViewBase)` gate used by
     `ExplorationEvents` before speaking (and skipping non-announceable picks).

3. **Auto, but throttled** (user choice). Keep auto-announcing the closest pick while walking, but on
   top of the existing dedupe-on-reference add: (a) suppress re-announce of the same entity within a
   short cooldown even if it churns in/out of "chosen", and (b) a minimum interval between spoken picks
   so moving through a cluster doesn't machine-gun. `Home` re-announce stays immediate (bypasses throttle).

Open: confirm `Home` (re-announce) and `End` (interact) behaviour in-game — not distinguishable in the
log. Also no `"Loading."` was seen before the area announce (the Continue-load path may raise the
loading-screen open before our subscriber exists; verify on a real in-game area transition).
