# Custom accessibility content via virtual navigation items

## Context

RTAccess currently has **two uncoordinated speech sources** with no shared notion of a
screen's content or order:

1. `Accessibility/DialogCuePatch.cs` — event-driven: speaks the NPC cue once on
   `DialogVM.HandleOnCueShow`.
2. `Accessibility/SetFocusedPatch.cs` + `UiTextReader.cs` — focus-driven: speaks each
   answer button as the game's console focus lands on it.

The cue (the actual dialogue line) is **not a navigable thing** — it is a fire-and-forget
announcement that lives outside the arrow-key focus ring. So a blind player cannot arrow
back to re-read it, and "the dialogue text comes before the options" is only true by luck
of announcement timing, not by structure.

We want **our own accessibility content woven into the game's own navigation**: the cue
should be the first *focusable stop* in the same arrow-key ring as the answer options,
read first, and re-readable at will.

**Chosen approach (per user):** rather than build a parallel cursor or a separate
read-augmentation layer, **inject our own synthetic focusable items into the game's
existing `ConsoleNavigationBehaviour` ring.** The game's arrow keys then move through real
widgets *and* our virtual items in one unified ring; activation and input stay the game's.
This is exactly how the game itself adds navigable widgets (public `Insert/AddEntity*`
API), so we are working with the engine, not around it.

### Why this is sound (verified in the decompile)

- The dialog answer list is held by a `GridConsoleNavigationBehaviour` on
  `SurfaceDialogBaseView` (field `NavigationBehaviour`, `decompiled/.../SurfaceDialog/SurfaceDialogBaseView.cs:152`),
  populated in `CreateNavigation()` via `SetEntitiesVertical(Answers)` (`:503`,`:510`).
- `GridConsoleNavigationBehaviour` navigates by **list order, not geometry**
  (`decompiled/Owlcat.Runtime.UI.ConsoleTools.NavigationTool/GridConsoleNavigationBehaviour.cs`,
  `GetUp/DownValidEntity` iterate row indices) and exposes public
  `InsertVertical(int index, IConsoleEntity)` (`:141`). → A synthetic item needs only
  list membership; **no on-screen RectTransform required.**
- Arrow moves route through `ConsoleNavigationBehaviour.FocusOnEntity` (`:1164`), which
  calls `newFocus.SetFocused(true)` (`:1179`) — the **same static choke point**
  (`ConsoleEntityExtensions.SetFocused`, `decompiled/Owlcat.Runtime.UI.ConsoleTools/ConsoleEntityExtensions.cs:10`)
  that `SetFocusedPatch` already hooks. `SetFocused` works for any `IConsoleEntity` that
  implements `IConsoleNavigationEntity` (`:12`). → Our item is read by the existing reader
  with **zero new input plumbing.**
- Confirm is opt-out: `IConfirmClickHandler.CanConfirmClick()` returning `false` makes an
  item a pure read-only stop (`ConsoleEntityExtensions.cs:99`).
- Answers commit through the answer view's own VM command
  (`AnswerVM.OnChooseAnswer()` → `GameCommandQueue.DialogAnswer` → `DialogController.SelectAnswer`),
  which we do **not** touch — real answer rows keep working exactly as today.

## Architecture

A small, reusable **virtual-nav-item** framework plus one per-screen "augmentor" (dialogue
first). No router, no parallel cursor, no new keybindings.

```
                game ConsoleNavigationBehaviour (arrow keys, Confirm)
                          │  list order: [ cueItem ][ answer1 ][ answer2 ]…
   DialogNavAugmentor ────┘  (Harmony postfix on CreateNavigation: InsertVertical(0, cueItem))
        │ builds
        ▼
   VirtualNavItem : IConsoleNavigationEntity, IConsoleEntity,
                    IConfirmClickHandler, IAccessibleTextProvider
        │ GetAccessibleText() → lazy cue text (speaker + narrative + check/alignment tag)
        ▼
   UiTextReader.Describe()  ── new first check: if (entity is IAccessibleTextProvider p) …
        ▼
   SetFocusedPatch → Speaker.Speak(text, interrupt: false)   (unchanged path)
```

### New, reusable pieces

- **`IAccessibleTextProvider`** (new, tiny): `string GetAccessibleText();`. Any synthetic
  entity that wants to be spoken implements it. `UiTextReader.Describe` checks it **first**,
  before TMP-scrape/VM-fallback.
- **`VirtualNavItem`** (new): a lightweight synthetic entity implementing
  `IConsoleNavigationEntity` (`SetFocus`/`IsValid`), `IConsoleEntity`,
  `IConfirmClickHandler` (default `CanConfirmClick() => false`; optional `onConfirm`
  delegate + hint for actionable items), and `IAccessibleTextProvider`. Constructed with a
  `Func<string>` text provider so a re-read always reflects current state. It is **not** a
  `Component` — it holds no GameObject, which is fine for list-order grids. (For future
  geometry-based screens it would also implement `IFloatConsoleNavigationEntity.GetPosition()`.)

### Per-screen augmentor (dialogue)

- **`DialogNavAugmentor`** (new): Harmony **postfix** on
  `SurfaceDialogBaseView<DialogAnswerConsoleView>.CreateNavigation()` (the closed generic
  used in forced-console mode). After the base has run `SetEntitiesVertical(Answers)`:
  1. Build a `VirtualNavItem` whose text provider formats the current cue
     (speaker + narrative, with a skill-check/soul-mark prefix when present).
  2. `NavigationBehaviour.InsertVertical(0, cueItem)` so the cue is the first stop.
  3. Set initial focus to the cue item so a new cue is announced cue-first via the normal
     focus-read (see "Initial focus / auto-read" below).
  - `NavigationBehaviour` is `protected`; access it via the project's publicizer
    (`IgnoresAccessChecksTo`, already in use) or `AccessTools` as fallback. The DialogVM is
    `__instance.ViewModel`.

### Shared cue formatting

- Extract cue-line formatting into a small **`DialogText`** helper (`speaker + narrative`,
  optional `UIUtility.SkillCheckText` / `SoulMarkShiftsText` prefix per HOOKMAP subsystem 1)
  used by both the virtual item and (if retained) `DialogCuePatch`, so cue wording has a
  single source.

## Files

Create:
- `RTAccess/Accessibility/IAccessibleTextProvider.cs` — the marker interface (or co-locate
  in `UiTextReader.cs`).
- `RTAccess/Accessibility/VirtualNavItem.cs` — the synthetic entity.
- `RTAccess/Accessibility/DialogNavAugmentor.cs` — the dialogue injection patch.
- `RTAccess/Accessibility/DialogText.cs` — shared cue-line formatter (small).

Modify:
- `RTAccess/Accessibility/UiTextReader.cs` — at the top of `Describe`, add
  `if (entity is IAccessibleTextProvider p) return new FocusReading(p.GetAccessibleText(), "VirtualNavItem");`.
- `RTAccess/Accessibility/DialogCuePatch.cs` — retire its auto-read, or refactor to reuse
  `DialogText` (decision in "Initial focus / auto-read"). Keep its voice-acted-line guard
  logic if the auto-read is retained.

Reuse (unchanged): `Speech/Speaker.cs` (`interpret: false` per the interrupt rule),
`SetFocusedPatch.cs`, `ConsoleMode.cs` (console mode is already forced, which is what makes
the focus ring active).

## Initial focus / auto-read (the one empirical unknown)

Goal: on each **new** cue, the player hears the cue first, then arrows down into options,
and can arrow back up to re-hear it — with **no double-read**.

- **Target:** make the injected cue item the initial focus on rebuild
  (`NavigationBehaviour.FocusOnEntityManual(cueItem)` or equivalent), and **retire
  `DialogCuePatch`'s separate auto-read** so the focus-read is the single cue announcement.
- **Risk:** the game may set initial focus to the first answer later in
  `AnswerPartUpdateCoroutine` (`SurfaceDialogBaseView.cs:420`), overriding our focus. This
  is the only thing not provable from the decompile — it must be checked at runtime.
- **Fallback if the game wins the focus race:** keep `DialogCuePatch` as the first-announce
  source and make the cue item a pure re-read affordance, **deduped** so that if focus lands
  on the cue item in the same cue-change it is not spoken twice (e.g. suppress the item's
  focus-read when the cue was just auto-spoken for the same `BlueprintCue`).

## Edge cases to handle

- **Cue re-fire:** `HandleOnCueShow`/answer rebuilds can repeat for one cue; dedupe on the
  `BlueprintCue` (the existing `DialogCuePatch` pattern) so the cue item is rebuilt/announced
  once per actual cue.
- **System answer (Continue):** when `DialogVM.SystemAnswer` is set (single "continue"),
  the answer list is null; still inject the cue item so the line is readable before the
  continue prompt.
- **PC vs console closed generic:** console mode is forced, so patch
  `SurfaceDialogBaseView<DialogAnswerConsoleView>.CreateNavigation`. If a build ever runs the
  PC answer view, also patch `SurfaceDialogBaseView<DialogAnswerPCView>` (note it; verify the
  live type once).
- **Dialog teardown:** the injected item is owned by the per-cue navigation rebuild and the
  `DialogVM`'s own dispose (`DisposeImplementation`), so no manual cleanup is needed; the
  item holds no GameObject and goes away with the nav behaviour. Confirm no leak across cues.
- **Book-event dialogs** use a different view (`BookEventCueView`); out of scope for v1 —
  the cue there is already spoken via the existing `BookEventView` hook (HOOKMAP subsystem 1).

## Extensibility (the general "our own accessibility UI")

The same two primitives generalize beyond dialogue:
- `VirtualNavItem` + a per-screen augmentor can inject a **screen heading/summary** stop, a
  **status line**, or **group headers** into any list-order (`GridConsoleNavigationBehaviour`)
  screen — inventory, character sheet, journal — purely additive, riding the game's nav.
- For **geometry-based** screens (`FloatConsoleNavigationBehaviour`: sector/warp map,
  formation), the same item must also implement `IFloatConsoleNavigationEntity.GetPosition()`
  to be reachable; note this when those screens are tackled.

## Verification

1. Build: `dotnet build RTAccess.slnx -c Release` (build the **.slnx**, not the .csproj —
   `$(SolutionDir)` is `*Undefined*` otherwise; see project notes).
2. In-game, enter any dialogue (console mode is forced by default). Expect:
   - On a new cue: the cue line is spoken first (speaker + text), before any option.
   - Arrow Down from the cue lands on option 1 ("1. …"), Down again option 2, etc.
   - Arrow **Up** from option 1 returns to the cue and **re-reads** it (the new capability).
   - Pressing Confirm on the cue stop does nothing (read-only); Confirm on an option selects
     it exactly as before.
   - No double-read of the cue when it appears.
3. Check `focus_log.txt` / `speech_log.txt` (via `Logs`/`FocusLog`/`SpeechLog`): the cue
   should appear as a focusable read with source `VirtualNavItem`, ordered before the answers.
4. Confirm a multi-cue conversation: each new cue rebuilds with its own cue stop; no stale
   cue text, no leftover items accumulating in the ring.
5. Regression: other screens (inventory/settings) still read via the generic
   `SetFocusedPatch` path unchanged.
