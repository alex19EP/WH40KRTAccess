---
name: rt-interrupt-speech-rule
description: Interrupt speech on any keypress that causes it; queue automatic-focus and passive/event speech. Provenance, not timing.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: abb9bd34-f479-4987-8bc1-8a5029aa0007
---

In RTAccess, `Speaker.Speak`'s `interrupt` flag follows one clean rule (settled 2026-06-06 after two rejected drafts — an unconditional-on-input version and a timing/"burst" heuristic version, both wrong):

**Interrupt (`true`) iff the speech was caused by a keypress. Queue (`false`) when focus was set automatically (by the mod's own screen build or by the game), or for passive/event speech.** Decided by *provenance* (what caused this line), never by timing. `Speaker.Speak(text, interrupt = false)` defaults to queue.

- **Direct key handlers → `interrupt:true` always:** everything we Speak from our own key-triggered paths — nav moves and re-reads, settings Left/Right, party step, tile explorer, scanner verbs, CharGen readouts, diagnostics, text edit/typing, loot/stash outcomes. These call sites pass `interrupt: true` explicitly.
- **The parallel Navigator carries provenance in the call itself — no probe needed.** Because the mod now OWNS its UI navigation (parallel tree, [[ui-paradigm-pivot]]), it knows at the call site whether a keypress caused the focus change. `Navigator.AnnounceDelta(oldPath, interrupt)` is the focus-change announcer (`RTAccess/UI/Navigator.cs`): a real focus MOVE (arrow/Tab, in `TraditionalNavigator`) passes `interrupt:true` (and plays the control-hover sound); a screen-entry readout or `Navigator.Focus(target)` (the screen inserting/setting focus itself) uses the default `interrupt:false` → QUEUES, so a preceding feedback line still plays ("Inventory. <first item>.").
- **This REPLACES the old console-nav mechanism.** The `ConsoleEntityExtensions.SetFocused` choke point + `SetFocusedPatch` + `NavInputProbe` (frame-stamp of `ConsoleNavigationBehaviour.HandlePressed`) are all **DELETED** — with the mod owning the nav there is no shared choke point between user-nav and auto-focus to disambiguate, so the probe is unnecessary.
- **Exploration keeps the frame-stamp trick** (the game still owns that trigger, and keyboard-cycle + passive walk-by converge on the same announce): `ExplorationEvents.MarkUserCycle()` stamps `Time.frameCount` before a keyboard cycle; the surrounding-object handler interrupts iff that frame is current, so a keyboard cycle interrupts while a passive walk-by queues. `MarkUserCycle` + the Home re-announce are still live (`ExplorationEvents.cs`).

**Queue (`false`):** mod-driven and game-driven automatic focus (handled by the default above), and passive/event speech — barks ([[rt-bark-system]]), combat/log events ([[rt-message-log]]), area/loading transitions, ambient walk-by surroundings, conviction.

**Why:** the user (a blind dev — see [[project-accessibility-mod]]) wants any keypress that produces speech to interrupt immediately (responsive), while speech the mod/game produces by auto-setting focus while building a screen must not clip what's playing.

**How to apply:** new `Speaker.Speak` from a key handler → `interrupt:true`. Passive/event → `false`. New screen-driven focus readout → let `Navigator`/`AnnounceDelta`'s default queue it; don't pass `true`. `speech_log.txt`: `[!]`=interrupt, `[+]`=queued. Relates to [[rt-speech-backend]], [[rt-speechmod-reference]].
</content>
