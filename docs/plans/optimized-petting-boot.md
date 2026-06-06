# Radial/wheel menu accessibility for RTAccess

## Context

WH40K Rogue Trader's two HUD "wheel" menus — the **party/character selector** (gamepad hold L2) and the
**in-game menu** (gamepad hold R2) — plus the contextual **group changer** and **formation** editors are
unusable for a blind player today: they are gamepad-hold + stick radials with no keyboard binding, and
nothing announces what they contain.

Investigation of the decompiled UI settled two things:

1. **Per-entry announcement already works.** All four wheels drive focus through
   `ConsoleNavigationBehaviour.FocusOnEntity → entity.SetFocused(true)` — the exact
   `ConsoleEntityExtensions.SetFocused` choke point that `SetFocusedPatch` already hooks. The wheel item
   views (`IngameMenuItemConsoleView`, `PartySelectorItemConsoleView`, `GroupChangerCharacterConsoleView`,
   `FormationCharacterConsoleView`) carry TMP labels (`m_Label` / `m_CharacterName`), so `UiTextReader`
   reads them. This is code-verified; the user's existing `focus_log.txt` never exercised the wheels, so it
   needs **runtime confirmation**, but no new code is required for it.

2. **Keyboard operation of the radials is NOT wanted** (user decision). The radials stay gamepad-driven.
   Instead we make their *functions* reachable by keyboard in console mode, and add the missing
   **announce-on-open** for when a gamepad is used.

So the deliverable is two-sided ("Both"):
- **Radials (gamepad):** announce name + entry count on open. Per-entry already announces.
- **Keyboard (console mode):** reach the same functions without the radial — character selection (what L2
  gives) and the in-game-menu-only windows (what R2 gives beyond the existing I/C/J/M).

The build publicizes `Code` / `Owlcat.Runtime.UI` (`BepInEx.AssemblyPublicizer`, `Publicize="true"` in
`RTAccess/RTAccess.csproj`), so patches read the game's private/protected view fields directly — same as
`DialogNavAugmentor.cs`. All speech honors [[rt-interrupt-speech-rule]] (`interrupt:false`).

## Approach

### 1. Announce wheels on open (name + count) — `RTAccess/Accessibility/WheelMenus.cs` (NEW)

A small static tracker plus Harmony patches on the four wheel views. The tracker exposes
`ActiveWheel` (a friendly name string, or null) set when a wheel binds and cleared when it is destroyed;
`SetFocusedPatch` uses it (see §2).

Announce **before** the wheel's first focus fires, so "name + count" precedes the first entry (speech is
queued, `interrupt:false`). For the two main radials the items are bound in `BindViewImplementation`
*before* `CreateNavigation`, so a **prefix on `CreateNavigation`** is the clean injection point with the
count already available:

- `IngameMenuConsoleView.CreateNavigation` (prefix): count = active `IngameMenuItemConsoleView` children of
  `m_Content` (the same list the method itself builds). Announce `"Menu, N options"`; set tracker.
- `PartySelectorConsoleView.CreateNavigation` (prefix): count = `m_Characters.Count(c => c.IsBinded)`.
  Announce `"Party selector, N characters"`; set tracker.
- `GroupChangerConsoleView` / `FormationConsoleView` (postfix on `BindViewImplementation`, best-effort
  ordering): announce `"Group changer, N"` / `"Formation, N"` from `m_Characters` count; set tracker.
- Each view's `DestroyViewImplementation` (postfix): clear the tracker.

Counts and item lists are read via publicized fields (e.g. `__instance.m_Characters`,
`__instance.m_Content`). Friendly names are hardcoded English, consistent with `WindowHotkeys`
(entry text itself reads in the game locale, Russian, via TMP).

Patch style mirrors `DialogNavAugmentor.cs` (`[HarmonyPatch(typeof(View), "Method")]`, `try/catch`,
`Main.Log` on failure).

### 2. Suppress the in-game-menu placeholder — `RTAccess/Accessibility/SetFocusedPatch.cs` (EDIT)

`IngameMenuConsoleView` initially focuses `m_FirstSelection` (an unlabeled `SimpleConsoleNavigationEntity`
placeholder), which would read as "Unlabeled: ...". In `Announce`, when `WheelMenus.ActiveWheel != null`
and the reading has no text, **skip speaking** (still write the focus log). Wheel entries are always
labeled, so this only hides the placeholder, without masking coverage gaps on other screens.

### 3. In-game-menu-only windows by keyboard — `RTAccess/Accessibility/WindowHotkeys.cs` (EDIT)

Extend the existing console-mode-gated updater (reuses `Open(label, Action<INewServiceWindowUIHandler>)`):

| Key | Window | Handler call |
|-----|--------|--------------|
| `L` | Encyclopedia | `h.HandleOpenEncyclopedia()` |
| `Y` | Colony management | `h.HandleOpenColonyManagement()` |
| `V` | Ship customization | `h.HandleOpenShipCustomization()` |
| `B` | Cargo management | `h.HandleOpenCargoManagement()` |
| `U` | Level up | `h.HandleOpenCharacterInfoPage(CharInfoPageType.LevelProgression, current unit)` |

`L/Y/V/B` reuse the game's own mouse-mode window keys (inactive in console mode — the same premise that
makes the existing `I/C/J/M` safe); `U` is a free key (mnemonic "Up"). Existing `I/C/J/M` are unchanged.

### 4. Character selection by keyboard — `RTAccess/Accessibility/PartyHotkeys.cs` (NEW)

Console-mode-gated updater (called from `Main.OnUpdate`, like `WindowHotkeys.Update`). Replaces the L2
party selector's core function — picking the active character — and announces the new name:

- `Shift+D` → next, `Shift+A` → previous: cycle `Game.Instance.Player.Party` filtered to
  `IsDirectlyControllable()`, relative to the current `Game.Instance.SelectionCharacter.SelectedUnit.Value`
  (fallback `SelectedUnitInUI.Value` / `FirstSelectedUnit`), then `SetSelected(unit)`.
- `Alt+1..6` → select that party slot directly.
- After selecting, `Speaker.Speak(unit.CharacterName)`.

Reuses the game's documented `PrevCharacter`/`NextCharacter`/`SelectCharacter[n]` combos so there is nothing
new to memorize. `Alt+digit` does not collide with console nav's `Alt+Arrow` (right-stick) mapping.

### 5. Wire-up — `RTAccess/Main.cs` (EDIT)

Add `PartyHotkeys.Update();` in `OnUpdate` next to `WindowHotkeys.Update();`. `WheelMenus` is patch-based
and auto-registers via the existing `HarmonyInstance.PatchAll`.

### 6. Memory update

Update `rt-console-ui-actions` (and a note in `rt-a11y-architecture-verdict`) with the finding that all four
wheels expose selection through the same `FocusOnEntity → SetFocused` path (so per-entry TTS is free), the
hold-radials vs contextual-window distinction, and the keyboard-function-substitution decision.

## Files

- NEW `RTAccess/Accessibility/WheelMenus.cs` — tracker + 4 wheel-view open/close patches (announce name+count).
- NEW `RTAccess/Accessibility/PartyHotkeys.cs` — keyboard character selection in console mode.
- EDIT `RTAccess/Accessibility/SetFocusedPatch.cs` — suppress unlabeled reads while a wheel is active.
- EDIT `RTAccess/Accessibility/WindowHotkeys.cs` — add L/Y/V/B/U window keys.
- EDIT `RTAccess/Main.cs` — call `PartyHotkeys.Update()`.
- Reused as-is: `Speech/Speaker.cs`, `Accessibility/UiTextReader.cs`, `Accessibility/FocusLog.cs`.

## Verification

Build (must target the `.slnx`, per [[rt-a11y-architecture-verdict]]):

```
dotnet build RTAccess.slnx -c Release
```

It auto-deploys to `%LocalAppDataLow%\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\RTAccess`.
Close the game first so native DLLs unlock. Then in-game (console mode is forced by default), with logs at
the mod folder (`speech_log.txt` / `focus_log.txt`, reset each launch):

1. **Per-entry (gamepad):** hold L2, move the stick — each character should be spoken. Hold R2, move —
   each menu option spoken. (Confirms the code-verified `SetFocused` path at runtime.)
2. **Announce-on-open (gamepad):** opening L2 says "Party selector, N characters" then the first character;
   opening R2 says "Menu, N options" with no "Unlabeled" placeholder before the first real option.
3. **Keyboard character select:** `Shift+D` / `Shift+A` cycle and speak the selected character's name;
   `Alt+1..6` selects and speaks slot N.
4. **Keyboard windows:** `L`/`Y`/`V`/`B`/`U` open Encyclopedia/Colony/Ship/Cargo/Level-up and speak the
   label; existing `I`/`C`/`J`/`M` still work.
5. Group changer / formation, when they appear via game flow, speak name + count on open and each entry on
   navigation.

I cannot drive the GUI; the user (blind dev) runs steps 1–5 and reports speech output. The `focus_log.txt`
also records each focused wheel entity for offline inspection.

## Open / best-effort

- "Item N of M" position was left optional (the original brief marks it optional; radial float-nav has no
  linear order). Can be added later via the wheel tracker if wanted.
- The `L/Y/V/B`/`Shift+A·D`/`Alt+digit` reuse assumes those game binds are inactive in console mode (same
  assumption that makes existing `I/C/J/M` work); step 4/3 verifies. If any double-fires, switch that one to
  a free key (`K`/`O`/`Z`).
