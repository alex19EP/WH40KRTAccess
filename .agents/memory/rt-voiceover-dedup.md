---
name: rt-voiceover-dedup
description: "RT voice-over facts — one PlayVoiceOver choke point, English-only VO vs 9 text locales, 5089/77691 voiced strings (barks are the biggest block); the mod defers speech to the actor"
metadata: 
  node_type: memory
  type: project
  originSessionId: 65693414-28bd-458e-95ec-c011ded7ae42
  modified: 2026-07-30T19:45:55.411Z
---

Verified 2026-07-30 while fixing the Discord report "dialogue is always spoken by TTS even when voiced".

**One choke point.** Every voiced line in the game — dialogue cues (`DialogVM.HandleOnCueShow`), book-event
paragraphs (`BookEventVM` queues one per cue and plays them in sequence), companion banter, cutscene subtitles
— goes through `Kingmaker.UI.Sound.VoiceOverPlayer.PlayVoiceOver`. It returns a `VoiceOverStatus`, or **null**
when nothing started: no recorded take (`LocalizedString.GetVoiceOverSound()` empty) or `SoundEventsManager.PostEvent`
returned 0. So "did the player actually hear this?" is answerable from the engine, not guessed. `VoiceOverStatus`
also exposes `IsEnded` (Wwise reports clip duration via an `AK_Duration` callback) and `Stop()`.

**The voice-over is English only** — `GeneratedSoundBanks/Windows/` holds a single `English(US)` language folder
— while the text ships in nine locales. `Locale.Sound` is one shared pack of event NAMES, not a per-language pack,
so `GetVoiceOverSound()` says nothing about the player's language. Any suppression must gate on
`LocalizationManager.Instance.CurrentLocale == enGB` or the voice stops matching what is on screen.

**Scale**: 5,089 voiced strings against 77,691 text strings (~6.5%), wildly uneven —
`BNTRS_` companion banter **2,060** (the biggest single block, and it is *barks*, not dialogue),
`Companions_` 1,378, `RMNC_`+`CompanionDialogue_` 664, `PRL_` prologue 346. That is why testers in the
prologue and companion talks reported it as affecting "every conversation".

**The game splits the line for us**: `CueVM.GetMechanicText()` = skill-check result + soul-mark shift,
`GetNarrativeText()` = the words. The take carries only the narrative — the skill-check result is never
recorded, and a sighted player reads it beside the portrait while the take runs. So that part stays spoken.

Barks carry no line identity into their handler (`IBarkHandler` gets a display string), so they correlate by
FRAME: `BarkPlayer` posts the voice-over and raises the bark event inside one synchronous call. Voiced barks
stay in the game's own log (`CombatLogBarkLogThread`, channel `Dialog`), so the log review screen keeps them.
Subtitle barks (`SubtitleBarkHandle`) do **not** reach the log — they only raise `ISubtitleBarkHandler`.

Implemented in `RTAccess/Accessibility/VoiceOver.cs`, behind the `speech.voiced_lines` setting (on by default).
Suppresses SPEECH ONLY — never remove text from the graph; see [[rt-label-mirror-visual]] and
[[rt-interrupt-speech-rule]].
