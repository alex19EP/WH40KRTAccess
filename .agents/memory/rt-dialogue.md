---
name: rt-dialogue
description: "Accessible in-game conversations — the transcript-style DialogueScreen, the spoken cue line (speaker + skill-check result + text), answer navigation + number quick-select, and the EventSystem answer-gate"
metadata: 
  node_type: memory
  type: project
  originSessionId: 2f822d1e-f036-4c1c-bca4-a9d4bc347439
---

In-game conversations are made accessible by **`Screens/DialogueScreen.cs`** (graph key `ctx.dialogue`), built on
the shared **`TranscriptScreen<DialogVM,CueVM>`** shell. It reads like a transcript: ONE silent, positions-off
graph scope holding the **scrollback** (the game's own `DialogVM.History` — past cues + chosen answers, rendered
via `IDialogShowData.GetText(UIConfig.DialogColors)`, rich-text stripped), then the **live cue row**, then the
**answers** — all arrow-navigable top-to-bottom (Down reaches answers/Continue, Up re-reads earlier lines).
Immediate mode: declared fresh from the live VM each render. `Vm()` = `RootUiContext.DialogVM.Value` (==
`RootUiContext.HasDialog`).

**Speak-once, always QUEUED (dialogue never interrupts).** A cue is announced once via `DialogText.BuildCueLine`.
**Delivery gate** = `DialogVM.IsVisible.Value && !_dialogFaded`: the engine drops `IsVisible` during
cutscene/escmenu/pause (RT's replacement for WOTR's `m_CutsceneScheduled`); `_dialogFaded` shadows
`DialogContextVM.ToggleDialogFadeCommand` (a designer mid-cue black screen). A queued Enter can't fire — nor a line
be spoken — into a cutscene-hidden cue. **Cue identity** keys on the stable `BlueprintCue` (fallback: the `CueVM`
instance for string-only cues), which guards against `DialogVM.HandleOnCueShow` re-firing a fresh `CueVM` for the
SAME line at dialog start (would otherwise re-home + double-announce the first cue).

**The spoken cue line** (`Accessibility/DialogText.cs`, `BuildCueLine(cue, includeSpeaker)`) = speaker +
skill-check result + cue text, rich-text stripped, in that order:
- text = `cue.RawText` ?? `DialogController.CurrentCue.DisplayText`.
- **skill-check RESULT prefix** ("Failed a Persuasion check") is composed by the game at runtime via
  `UIUtility.SkillCheckText(cue.SkillChecks, DialogColors)` — NOT part of `RawText`, so the narrative line alone
  drops it; prepend it as the sighted cue view does (honors the game's `ShowSkillcheckResult` toggle).
- speaker = `DialogController.CurrentSpeakerName`, prepended ONLY when `includeSpeaker` (so a run of cues from the
  same NPC isn't re-named every line).

**Answers.** `vm.Answers.Value` (real `AnswerVM` list) else `vm.SystemAnswer.Value` (the single "Continue") →
`DialogNodes.AnswerNode(a, deliverable)`; keys carry cue identity so a new cue drops old answers + adds new.
- **Enter-through-exposition:** when the ONLY way forward is the system Continue (no real answers) and the window
  is deliverable, Enter on the cue row itself presses it — a blind player holds Enter through exposition instead of
  arrowing to Continue every line.
- **Number quick-select:** a bare `1..9` (top row or numpad) picks the answer whose displayed `AnswerVM.Index`
  matches, from anywhere in the transcript. Confirmation-first: speaks the chosen line (interrupt) then advances; a
  shown-but-locked answer speaks "+ disabled" and does NOT fire. Bare digits only (Alt = party-select, Ctrl/Shift
  are other chords), gated on deliverable. This is the SOLE number path (the game's own `DialogChoiceN`/Alpha1-9
  bindings are blocked by the answer-gate); it routes through `DialogChoiceGate.Choose` so the guard counts it owned.
- **Space on the cue** = skill-check roll breakdown (`TooltipTemplateSkillCheckResult` from `cue.SkillChecks`) +
  inline glossary `<link>` terms harvested from `cue.RawText` (`GlossaryLinks.Gather`), via `TooltipChooser`.
- **Escape** opens the game's pause menu (`EventBus.RaiseEvent<IEscMenuHandler>(h => h.HandleOpen())`) — the screen
  owns `ui.back` and would otherwise swallow Escape mid-conversation.

**Answer-selection ownership gate.** The game's own dialogue view stays live under our parallel tree and fires
`AnswerVM.OnChooseAnswer` via Unity EventSystem Submit (a THIRD input path). `DialogChoiceGuard` (Harmony prefix on
`OnChooseAnswer`) allows only our `UI.Proxies.DialogChoiceGate.MineNow` call while `FocusMode.Active &&
RootUiContext.HasDialog`, fails open otherwise. This is path #3 of the four input paths — full story in
[[rt-input-system-verdict]]. (RT's `OnChooseAnswer` is silent, so the cue node keeps the default click sound, unlike WOTR.)

**Not read here:** in-dialogue notifications (reputation shifts, item/XP/Profit-Factor gains, companion ability/buff
gains) flow through the game log → voiced by `LogTap`; conviction (soul-mark) shifts, the one thing the game never
logs, via `ConvictionEvents` — see [[rt-message-log]]. Book events / epilogues are a SEPARATE screen
(`Screens/BookEventScreen.cs`). SpeechMod's original cue hook was `DialogVM.HandleOnCueShow` ([[rt-speechmod-reference]]).
