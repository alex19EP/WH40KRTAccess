---
name: rt-input-system-verdict
description: "Input architecture — why the custom keyboard framework stays, the merge-not-own arbitration model, and the FOUR competing input paths + their gates"
metadata: 
  node_type: memory
  type: project
  originSessionId: 2f822d1e-f036-4c1c-bca4-a9d4bc347439
---

The mod's `RTAccess/Input/*` framework polls raw `UnityEngine.Input` each frame (port SayTheSpire2 →
WrathAccess → RTAccess). **Verdict: keep it; do NOT migrate to the game's input system.** Memo:
`docs/input-system-architecture-review.md`.

**Why keep it:**
- "The in-game input system" for KEYBOARD commands is `Kingmaker.UI.InputSystems.KeyboardAccess` — NOT Rewired.
  KeyboardAccess is itself a raw `UnityEngine.Input` per-frame poller (KeyboardAccess.cs:98-107) with
  `RegisterBinding`/`Bind`, exact-modifier chords, a text-field guard, and a `Disabled` guard — the SAME paradigm
  as the mod's port.
- **Rewired cannot host the ~50 mod commands:** 23 baked gamepad actions (ids 0-22, one "Default" category);
  runtime `ReInput.mapping.Actions` is read-only, no `AddAction`/`CreateAction` (confirmed via DLL reflection). You
  can add bindings to EXISTING actions (`ControllerMap.CreateElementMap`) but there are no spare actions; adding
  actions needs a `UserData` swap + `ReInput.Reset()` (global rebuild, unsafe).
- Features the custom layer needs that KeyboardAccess lacks: **focus-dependent chord shadowing** (bare arrows = HUD
  nav when focused vs tile cursor when not; `InGameScreen.cs:52-69` flips UI↔Exploration on `Navigation.HasFocus` +
  `InputManager.RebuildLive`), per-screen **category layering** (KeyboardAccess context is `GameModeType` only), and
  **OS-rate typematic repeat** (`OsKeyboard`). The mouse-mode gate ([[rt-mouse-mode-engine-gate]]) kills the
  Rewired/console path but NOT KeyboardAccess.

**The "merge, don't own" model** (per-chord arbitration, not a blanket mute):
- `Input/GameKeybinds.cs` — on boot moves the game's ten bare-letter service-window openers (C/I/J/M/L/Y/V/B/N/U) to
  **Ctrl+letter** via the game's OWN settings path (`SettingsRoot.Controls.Keybindings.General.<X>.
  SetKeyBindingDataAndConfirm`), freeing the bare letters + auto-updating in-game/tutorial hints
  (`RenewRegisteredBindings` raises `IKeybindChanged`). Idempotent; never clobbers a manual rebind.
  `ActionBar.ChangeWeaponSet` (bare X) is vacated to Ctrl+X the same way. `Main.OnToggle` reverts on UMM-disable
  (`GameKeybinds.Revert()` + `FocusMode.Set(false)`).
- `Accessibility/KeyboardArbitration.cs` — Harmony prefix on `KeyboardAccess.OnCallbackByBinding`: focus OFF →
  vanilla; Exclusive mod screen → mute all; else suppress a game key ONLY if `InputManager.ClaimsChord(...)` this
  frame. `ClaimsChord` uses a frame-guarded `EnsureLive()` (order-independent vs the game tick) and **snapshots
  `Navigation.HasFocus` once per frame** — without the snapshot a focused-HUD Escape's `Navigation.Blur()` flips
  HasFocus mid-frame → Escape both blurs AND yields (double-fire).
- Context-split keys via `InputAction.YieldsWhenUnfocused()`: **Space** (tooltip when focused, game Pause/End-turn
  when not); **Escape** (`ui.back`) — routed through `EscHotkeyManager` → KeyboardAccess binding `"EscPressed"` (so
  our prefix intercepts it): HUD-focused Esc → blur to exploration; exploration Esc → game pause menu (mod's
  `EscMenuScreen` wraps it); mod-window Esc → closes it.
- Keep the H/G/Ctrl+A/char-select override handlers — they aren't reimplementations: each calls the game's OWN
  `SelectionManagerBase` API (`Hold()`/`Stop()`/`SelectAll()`/SetSelected) and only owns the key dispatch to add the
  spoken confirmation the game omits (those game calls raise no/too-broad events → silent or chatty otherwise).
  `SelectionAnnouncer` + `WeaponSetAnnouncer` poll for selection / weapon-set changes from sources the key paths
  don't voice (weapon-set reads `Body.CurrentHandEquipmentSetIndex`; both silenced in TB combat where the reactive churns).

**FOUR competing input paths** (each needs its own containment — chord suppression alone catches only #1):
1. **Raw KeyboardAccess** → KeyboardArbitration (above).
2. **Bare-letter game keybinds** → GameKeybinds relocation (above).
3. **Unity EventSystem "Submit"/click** on the game view's selected button — a THIRD path parallel to BOTH our
   navigator AND KeyboardArbitration. The game's own UI views stay live beneath our parallel tree ([[ui-paradigm-pivot]])
   and fire e.g. `AnswerVM.OnChooseAnswer` via Submit on the same Enter that dismissed a tutorial (selecting an
   un-navigated answer; also double-fires every selection, usually a stale no-op once `DialogController.
   CurrentCueUpdateTick` advanced). **Fix = ownership gate at the game's action method:** `UI.Proxies.DialogChoiceGate.
   Choose(vm)` wraps our own call in a `MineNow` flag; Harmony prefix `DialogChoiceGuard` on `OnChooseAnswer` allows
   ONLY the flagged call while `FocusMode.Active && RootUiContext.HasDialog`, fails open otherwise (the full dialogue-reading screen is [[rt-dialogue]]). **General lesson:**
   any parallel-tree screen overlaying a live game view can leak via EventSystem Submit — gate at the action method
   with an ownership flag, don't just suppress chords. (Red herring: the game's KeyboardAccess dialogue bindings are
   DialogChoice1..9 = Alpha1-9, NextOrEnd = Space, not Enter.)
4. **Rewired console-nav layers** — game views push `GridConsoleNavigationBehaviour` layers onto `GamePad.Instance`
   even on PC (EscMenuBaseView.BuildNavigation + ~20 siblings), reading the keyboard through Rewired (Enter = Confirm
   action 8, arrows move the layer's invisible focus ring). Symptom: declining an esc-menu quit-confirm let the ring's
   Confirm enqueue a FRESH quit box via `CommonVM.m_MessageQueue` → an unkillable box. **Fix = `Input/GameInputLayerGate.cs`:**
   one Harmony prefix on `Owlcat.Runtime.UI.ConsoleTools.GamepadInput.BindDescription.Handler` (the funnel every layer
   bind passes through) skipping dispatch while `Navigation.HasFocus`. Blast-radius audit (1097 sites) = PATCH SAFE
   (loading press-any-key, cutscene-skip, text input, Escape, camera, mouse — none ride the gated path; console
   `*ConsoleView` layers never instantiate on PC). If gamepad support ever enters scope, exempt
   `ChoseControllerModeWindowView`/`GamepadConnected*`.

Harness gotchas: `IDialogMessageBoxUIHandler.HandleOpen` with null callbacks makes a box whose
`OnDeclinePressed`/`Dispose` NRE — test with REAL boxes (esc-menu quit confirm), clean up via the VM's own
`OnDeclinePressed()`. Physical-key testing that reaches the real paths (`Navigation.DispatchJustPressed` bypasses
them): PowerShell `AppActivate(pid)` + `SendKeys.SendWait`. Dead-scaffolding (`BindingSetting`/`CapturesRawInput`)
preserved as future accessible-rebind hooks. Relates to [[rt-a11y-architecture-verdict]], [[rt-keyboard-usage]].
