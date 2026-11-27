---
name: rt-keyboard-usage
description: "RT's default keyboard bindings (from F10 KeybindingsDump) — what keys are taken per game-mode and what's free"
metadata: 
  node_type: memory
  type: reference
  originSessionId: cf8b65b2-e0e5-4f9a-a1b4-f7dad217dbf6
---

Authoritative dump of `SettingsRoot.Controls.Keybindings` (RTAccess F10 → Keybindings_dump.json). Hotkeys are **game-mode scoped** (GameModesGroup): World, WorldFullscreenUI, CameraControls, Dialog, ForPause, All. This is SEPARATE from the Rewired console-UI map (arrows/Enter/Esc/Ctrl+arrows/Alt+arrows).

**Used (default):** Letters — C Inv-screen, I Inventory, J Journal, M Map, L Encyclopedia, Y Colony, V Ship(+Unpause), B Cargo, N Formation, H Hold, G Stop, F Follow, P CombatLog, R FlipZone, T SpeedUpEnemies, X ChangeWeaponSet, WASD Camera move, Q/E CameraRotate, Tab HighlightObjects. Combos — Shift+Q/E PrevTab/NextTab, Shift+A/D Prev/NextCharacter, Ctrl+X RotateNorth, Ctrl+F SearchInventory, Ctrl+A/Backspace SelectAll, Shift+Space Unpause. Space = Pause/SkipBark/EndTurn/CollectAllLoot (context). Return SkipCutscene. F-keys — F1-F4 Consumables, F5 QuickSave, **F8 QuickLoad**, F11 Screenshot, Shift+F10 ModWindow, Ctrl+F11 ToggleUI. Number row — Alpha1-0+Minus+Equals = Weapons; Shift+Alpha1-0 = Abilities 1-10; Ctrl+Alpha1-0 = Abilities 11-20; Alpha1-0 = Dialog choices (Dialog mode); Alt+Alpha1-6 = SelectCharacter.

**FREE keys (no binding in any context) — usable for the 10 console-UI buttons:** letters **K, O, U, Z**; **F6, F7, F9, F12** (F9/F10 currently used by RTAccess debug dumps only); nav cluster **PageUp, PageDown, Home, End, Insert, Delete**; punctuation **[ ] ; ' , . / \\**; all **Numpad** keys. (Arrows/Enter/Esc are taken by Rewired nav + Camera.)

**Caveat:** our added nav keys operate in gamepad/console UI mode; many gameplay hotkeys are World/CameraControls (not menu) but some are WorldFullscreenUI — safest to pick keys free in ALL modes (the free pool above). NOTE: RTAccess speech-test is on F12 (bare F12 is free — no collision); F8 is now the DEBUG-only scanner-interact diagnostic (`diag.debug_interact`).

**SCREEN-READER GOTCHA (important):** **Insert is NVDA's default modifier key** — NVDA captures it, so a standalone Insert binding NEVER reaches the game/mod for our blind users. Do NOT bind standalone Insert despite it being "free" in the game's own map. (Caps Lock is NVDA's other modifier — same risk.) Confirmed: the tile explorer ([[rt-tile-explorer]]) was originally toggled on Insert and silently did nothing (the cursor is now always-active, no toggle). Delete (tile re-announce) is safe (not an NVDA key).
