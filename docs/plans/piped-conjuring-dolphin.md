# Interrupt-on-input speech rule

> **Revision (2026-06-06, post-review):** the first pass made focus interrupt
> unconditionally + a 3-path defer flag. Review found that `SetFocused` is the choke
> point for *all* focus (incl. automatic focus on every screen open / rebuild / return),
> so it over-interrupted. Replaced with a **navigation-burst gate** in `SetFocusedPatch`
> (interrupt only if a focus arrives within ~0.75s of the previous; otherwise queue), the
> defer flag and its 3 announcer calls removed, lead-in labels reverted to `interrupt:false`
> (the burst gate now keeps "Inventory. <first item>." in order), and one-shot
> confirmations/errors (F9/F10/F12, ConsoleMode, ExplorationNav) reverted to `false`.
> Kept interrupt: in-place adjust (CharGen stat, SettingsValueAnnounce) and read-now keys
> (F7, Ctrl+I, Ctrl+P, Home, party step). See the `rt-interrupt-speech-rule` memory.
>
> **Revision 2 (2026-06-06):** the burst gate was also rejected (heuristic, not clean). Final
> model is **provenance**: interrupt on any keypress-caused speech; queue automatic-focus and
> passive/event speech. New `NavInputProbe` prefixes `ConsoleNavigationBehaviour.HandlePressed`
> and frame-stamps directional input; `SetFocusedPatch.Announce` interrupts iff
> `!navigated || NavInputProbe.FiredThisFrame`. Lead-in labels + all key-driven confirmations go
> back to `interrupt:true` (the following auto-focus queues on its own, so labels are still heard
> in order — no defer flag). `ExplorationNav.Cycle` uses the same trick via
> `ExplorationEvents.MarkUserCycle()`. Only passive event streams stay `false`.

## Context

Today every `Speaker.Speak(...)` call passes `interrupt: false` (queue behind current
speech) per the original `rt-interrupt-speech-rule` memory. That rule was deliberately
conservative — nothing ever cut off in-progress speech. In practice this makes
navigation laggy: arrowing quickly through a list queues every item's full readout, so
the player cannot skim — they must wait through item 1 before hearing item 2.

The user wants to refine the rule: **speech that is the direct result of the user
pressing a button (keyboard or gamepad) to navigate or act should interrupt**, so the
latest action's feedback is heard immediately. **Passive/event-driven speech — barks,
dialogue auto-read, area/loading transitions, ambient surroundings — stays
`interrupt: false`** (queued) as before.

One nuance, confirmed with the user: short *orientation labels* spoken just before a
focus readout (opening Inventory → "Inventory" → first item; opening the party wheel →
"Party selector, 5 characters" → first character) must be **preserved**. The label
interrupts prior chatter, but the very next focus readout queues behind it, so the full
"Inventory. First item." is heard. Subsequent navigation interrupts as normal.

## Classification of every `Speaker.Speak` call site

**Interrupt (user pressed a button to navigate/act → `interrupt: true`):**
- `Main.cs` — F9 "Rewired config dumped.", F10 "Keybindings dumped.", F12 speech test.
- `SetFocusedPatch.cs` — `Announce` (focus moves as the user arrows/sticks through the
  UI; this is the core skim case), plus the F7 re-read and Ctrl+I details paths
  ("No focused element.", "Nothing focused.", the details line).
- `PartyHotkeys.cs` — selected character name (Shift+A/D, Alt+1..6).
- `ExplorationNav.cs` — "Not in exploration.", "No character selected." (PageUp/Dn/End).
- `ExplorationEvents.cs` — the **Home re-announce** path only ("Nothing nearby." and the
  re-spoken pick).
- `ConsoleMode.cs` — F6 mode toggle: "Game not ready.", mode name, "Mode switch failed."
- `CharGenAnnounce.cs` — stat readout after advance/retreat (button-driven), and the
  phase line (phase change via Next/Back/Confirm, and Ctrl+P re-announce).

**Queue (passive/event-driven → keep `interrupt: false`):**
- `BarkEvents.cs` — barks (notification stream).
- `DialogCuePatch.cs` — dialogue cue auto-read fallback.
- `ExplorationEvents.cs` — ambient surroundings announce while **walking**
  (`HandleSurrounding`), area transitions ("Entering X"), "Loading."

**Lead-in labels (interrupt prior chatter, then defer the next focus → `interrupt: true`
+ arm one-shot defer):**
- `ServiceWindowAnnounce.cs` — window name ("Inventory" / "Character" / ...).
- `WheelMenus.cs` — `Open` label ("Party selector, 5 characters", "Menu, 4 options", ...).
- `CharGenAnnounce.cs` — phase line (it is already in the interrupt bucket above; it also
  arms the defer so the first option focus that follows is heard after the orientation).

## Design

Keep the existing `Speaker.Speak(string, bool interrupt = false)` API unchanged — the
`false` default stays correct for the passive majority. Set `interrupt: true` explicitly
at the user-input sites listed above. No new enum or facade is introduced; this matches
the codebase's existing per-call `interrupt:` bool style.

### One-shot "defer next focus" mechanism (preserves orientation labels)

Add to `SetFocusedPatch` a one-shot flag plus a small public arming method:

```csharp
// A lead-in label (window / wheel / CharGen phase) was just spoken and the first focus
// that lands belongs with it — queue that one focus behind the label instead of cutting
// it off, so the orientation line is heard in full. Steady navigation after that
// interrupts as usual. One-shot: consumed by the next *spoken* focus.
private static bool _deferInterruptOnce;

/// <summary>Lead-in announcers (window/wheel/phase open) call this so the next focus that
/// lands queues behind their label rather than interrupting it.</summary>
public static void DeferNextFocusInterrupt() => _deferInterruptOnce = true;
```

In `SetFocusedPatch.Announce`, decide interrupt from the flag and consume it **only when a
line is actually spoken** (after the existing unlabeled-wheel-placeholder early-return, so
the silent centre placeholder does not eat the defer — the first *real* wheel entry does):

```csharp
if (!r.HasText && WheelMenus.ActiveWheel != null) return; // existing: skip wheel placeholder
bool interrupt = !_deferInterruptOnce;
_deferInterruptOnce = false;
Speaker.Speak(r.HasText ? r.Text : ("Unlabeled: " + r.Source), interrupt: interrupt);
```

Lead-in announcers each speak their label with `interrupt: true` and then call
`SetFocusedPatch.DeferNextFocusInterrupt()`:
- `ServiceWindowAnnounce.Postfix`
- `WheelMenus.Open`
- `CharGenAnnounce.Speak` (phase line)

This yields: open Inventory → (cuts prior chatter) "Inventory." → first focus queues →
"First item." → arrow → "Second item" (interrupts) → …  For wheels, the unlabeled centre
placeholder is skipped without consuming the flag, so the first labeled entry queues
behind "Party selector, 5 characters".

### `ExplorationEvents.Announce` gets an interrupt parameter

`Announce(EntityViewBase)` is shared by the ambient walking path (queue) and the Home
re-announce (interrupt). Change it to `Announce(EntityViewBase entity, bool interrupt)`:
- `HandleSurroundingInteractableObjectsCountChanged` → `Announce(entity, interrupt: false)`
- `ReannounceCurrent` → `Announce(_lastChosen, interrupt: true)` and the "Nothing nearby."
  fallback → `interrupt: true`.

## Files to modify

- `RTAccess/Accessibility/SetFocusedPatch.cs` — defer flag + `DeferNextFocusInterrupt()`;
  `Announce` interrupt logic; three direct Speaks → `true`.
- `RTAccess/Accessibility/ServiceWindowAnnounce.cs` — label → `true` + arm defer.
- `RTAccess/Accessibility/WheelMenus.cs` — `Open` label → `true` + arm defer; update the
  class-doc line that says it "queues, never interrupts".
- `RTAccess/Accessibility/CharGenAnnounce.cs` — stat readout → `true`; phase `Speak` →
  `true` + arm defer; update the `interrupt:false` line in the class doc.
- `RTAccess/Accessibility/ExplorationNav.cs` — two Speaks → `true`.
- `RTAccess/Accessibility/PartyHotkeys.cs` — one Speak → `true`.
- `RTAccess/Accessibility/ExplorationEvents.cs` — `Announce` gains `interrupt` param;
  Home path → `true`; walking/area/loading stay `false`; update the class-doc line.
- `RTAccess/Accessibility/ConsoleMode.cs` — three Speaks → `true`.
- `RTAccess/Main.cs` — F9/F10/F12 Speaks → `true`.
- Unchanged: `BarkEvents.cs`, `DialogCuePatch.cs`, `Speaker.cs`, `ISpeech.cs`,
  `PrismSpeech.cs`, `SpeechLog.cs` (its `[!]`/`[+]` markers already document the split).

## Memory update

Rewrite `memory/rt-interrupt-speech-rule.md`: the rule is no longer "interrupt only on
explicit user order." New rule — **interrupt:true for speech caused by a discrete
user input (navigation/action), interrupt:false for passive/event/notification speech;
lead-in orientation labels interrupt prior speech but defer the following focus via
`SetFocusedPatch.DeferNextFocusInterrupt()`.** Keep the `[[...]]` links.

## Verification

This is a Unity/Mono mod with no unit-test harness; verification is in-game plus a static
build + log check.

1. **Build**: compile the mod (the project's existing build/justfile path) and confirm no
   errors; check LSP diagnostics on every edited file.
2. **Speech log**: `speech_log.txt` prefixes interrupting lines with `[!]` and queued with
   `[+]`. After exercising the game, confirm:
   - Arrowing through any list / focus moves → `[!]`.
   - Opening Inventory → `[!] Inventory` immediately followed by `[+] <first item>`
     (label interrupts, first focus deferred), then further navigation `[!]`.
   - Opening the party/menu wheel → `[!] Party selector, N characters` then `[+] <first
     entry>`.
   - F6/F7/F9/F10/F12, Ctrl+I, Ctrl+P, Shift+A/D, Alt+1..6, PageUp/Dn/End/Home → `[!]`.
   - Barks, dialogue auto-read, "Entering <area>", "Loading", and ambient surroundings
     while walking → `[+]`.
3. **By ear** (screen reader running): fast-arrow a long list and confirm each item cuts
   the previous (skimmable); open Inventory and confirm "Inventory. <first item>." is
   heard in full.
