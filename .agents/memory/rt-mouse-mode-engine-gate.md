---
name: rt-mouse-mode-engine-gate
description: Game gates the surface interactable layer on console mode at the engine level (IsControllerMouse)
metadata: 
  node_type: memory
  type: reference
  originSessionId: f0f71460-c4c2-45c3-8105-5bd3d49b32f5
---

`SurfaceMainInputLayer.OnUpdate` (decompiled `SurfaceMainInputLayer.cs:107`) wraps ALL of its
interactable machinery in `if (!Game.Instance.IsControllerMouse && LayerBinded.Value)`. So in
**mouse mode** (`ControllerMode == Mouse`) the surface interactable layer is engine-dead:
`m_InteractableObjects` stays empty, `OnNext/PrevInteractable` early-return, `OnInteract` no-ops,
and `ISurroundingInteractableObjectsCountHandler` is never raised.

**Consequence for [[ui-paradigm-pivot]]:** removing forced console mode breaks the already-shipped
[[rt-tile-explorer]] interactable cycling and ExplorationEvents interactable announce — and
removing our own `ControllerMode == Gamepad` gate accomplishes nothing because the gate is in the
*engine*, not our code.

**Resolution (shipped):** the scanner / tile cursor were ultimately self-built on the mod's own raw-`Input`
framework (`Exploration/Scanner.cs`, `Accessibility/TileExplorer.cs`), NOT by driving `SurfaceMainInputLayer` —
nothing in the mod calls `UpdateInteractableSet`/`UpdateInteractions`. Interactions dispatch through the game's own
`ClickMapObjectHandler.Interact`; combat acts via the command API (the `!IsControllerMouse` gate touches only the
interactable input LAYER, not that API — see `Combat/CommandDispatch.cs`).

Other game levers confirmed this session: keyboard suppression =
`Game.Instance.Keyboard.Disabled.SetValue(true/false)` (CountingGuard; the `.Scope()` overload is
NOT in the RT decompile). Raw `UnityEngine.Input.GetKeyDown` is mode-independent.

**Console-mode removal DONE (Phase 1.5 core):** stopped forcing gamepad mode. Gotcha — deleting the
`Game.ControllerOverride` Harmony patch brings back the inaccessible "press A for gamepad / Enter for
keyboard" boot prompt (the override had suppressed it). Fix in `ConsoleMode.cs`: repurpose the override
to return `ControllerModeType.Mouse` (flag `ForceMouseAtLaunch`) — skips the prompt AND boots mouse mode.
Verified: `ControllerMode==Mouse`, no prompt, parallel `MainMenuScreen` navigates. The in-game helpers were
re-implemented to run in mouse mode — no `ControllerMode==Gamepad` gate and no F6/`SetFocusedPatch` console-read
path anymore (see `Accessibility/ConsoleMode.cs` and `Screens/InGameScreen.cs`; F6 is now only a DEBUG room-map
dump). See [[rt-dev-harness]].
