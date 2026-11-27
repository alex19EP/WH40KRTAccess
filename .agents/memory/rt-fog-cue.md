---
name: rt-fog-cue
description: "Fog-boundary audio cue (WA FogSystem port): keypress-driven tone as the planted cursor crosses the edge of party sight; FogProbe read is a render-thread-syncing ReadPixels so it MUST be movement-gated"
metadata:
  node_type: memory
  type: project
  originSessionId: a9cba596-a20b-48c6-9a57-8eb6599d6732
---

Port of WrathAccess's `FogSystem` overlay (the "party visible area" enter/exit ping). A one-shot tone plays when the
shared exploration cursor crosses the fog-of-war boundary — the edge of the party's CURRENT line of sight. Extends
[[rt-audio-pass]] (this is the plan's deferred Phase-G/I "Fog" system, built standalone). Uses `FogProbe` from
[[rt-scanner-explorer-audit]]. Compile-clean, ear-test pending. Compile trick [[rt-compile-check]].

**Files:**
- `Exploration/FogCue.cs` — driver, ticked from `Main.OnUpdate` right after `Sonar.Tick`. Samples
  `FogProbe.Classify(MapCursor.Position)` **only when the PLANTED cursor MOVED** (rate-capped `SampleSec=0.1`).
  **Why movement-gated:** RT's fog read is a synchronous 1×1 GPU `ReadPixels` that forces a render-thread sync (WotR's
  `FogOfWarController.IsInFogOfWar` was a cheap CPU query), so a default-on **continuous** poll would risk periodic
  micro-stutter clipping in-flight TTS. Because RT's cursor steps tile-by-tile, movement-gating makes the read
  **keypress-driven** — exactly FogProbe's design usage (its only other callers, `Scanner`/`InteractableDescriber`,
  are keypress one-shots). Movement gate via `_lastPos`+`_haveLast` (`sqrMagnitude<1e-4`, Sonar's idiom); unplanted →
  skip (cursor tracks the party = always in sight). **Trade-off:** the boundary sweeping over a PARKED cursor as the
  party advances is NOT cued (cursor didn't move) — accepted to keep readbacks event-driven; async `AsyncGPUReadback`
  is the upgrade path if that case is ever wanted. `fogged = (Explored || NeverSeen)`; visible = `Visible` (and
  `NoFow` = fog-off/off-map, treated not-fogged → no cue on ship decks, matches WA). Baseline `bool? _wasFogged`
  (null = don't fire on first sample); reset to null when inactive OR unplanted. try/catch like Sonar. Inherently
  visual-parity-safe (fog IS the sighted player's overlay — see [[rt-visual-parity]]); voices only the boundary,
  never what's beyond it.
- `Audio/Earcons.cs` — added `FogEnter()`/`FogExit()` + a private `PlayUngated` helper. **Sound matched to the PF
  wavs, not guessed:** analyzed WA's `fog_enter.wav` (0.349 s) + `fog_exit.wav` (0.458 s) — both are
  exponentially-decaying tones (the exact envelope Earcons' `Chime` makes), steady fundamentals **≈2080 Hz** (enter,
  higher/short) and **≈1590 Hz** (exit, lower/long). So `FogEnter=>Chime(2080,349)`, `FogExit=>Chime(1590,458)`
  reproduces PF faithfully with NO WAV shipped. `PlayUngated` is `Play` minus the `if(!Enabled) return` master gate —
  lets these two cues ship live while the rest of the deferred Earcons palette (`Earcons.Enabled=false`) stays off.
- `Main.cs` — registered `exploration.fog_cue` BoolSetting **default true (ON, no keybind)** beside `exploration.sonar`;
  wired the tick. Anchor is the movement cursor (`MapCursor`), which falls back to the party leader when unplanted
  (always Visible → no cue until planted+moved).
- `assets/locale/enGB/settings.json` — added `"exploration.fog_cue": "Fog boundary cue"` (the live `exploration.*`
  setting labels weren't localized there before — pre-existing gap for sonar/camera_follow/walltones siblings).

**PowerShell gotcha hit while analyzing the wavs:** variables are **case-insensitive**, so `$W` (count) and `$w` (loop
index) are the SAME variable — `for($w=0;$w -lt $W;...)` zeroed the count and ran 0 iterations. Rename to disambiguate.
