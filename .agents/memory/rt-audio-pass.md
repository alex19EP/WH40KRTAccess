---
name: rt-audio-pass
description: "Spatial-audio pass — Phase F primitives, WallTones cardinal bed, and Sonar (per-type WAV stems as live-tracked 3D sources w/ ITD + rear low-pass); all gated OFF, ear-tested by maintainer"
metadata: 
  node_type: memory
  type: project
  originSessionId: cb151c2f-81f5-49b8-ab93-74d09a316308
  modified: 2026-07-24T21:07:32.157Z
---

The **audio pass** of [[rt-map-viewer-port-plan]] (`docs/plans/echoing-charting-lovelace.md`).

**POLICY CHANGE — audio now ships ON by default (2026-07-25, maintainer's call, reverses the old "gated OFF"
law).** The soundscape is a core accessibility feature and has been ear-tested, so it's no longer gated off.
Enabled defaults (`Settings/Defaults.cs`): `exploration.sonar` = **When moving**, `exploration.walltones` =
**When moving** (deliberately NOT Continuous — an always-on bed is fatiguing), `exploration.sonar_review_sound`
= **review**; fog cue + the `audio.itd`/`front_back_filter`/`head_shadow` refinements were already on;
`audio.master_volume` = 100. NOTE the change only affects FRESH configs — a persisted `settings.json` wins, so
the maintainer's own already-on config is unchanged. Residual of the old rationale: audio quality is still
un-self-verifiable by the harness, so BRAND-NEW/experimental audio should still land OFF and get an ear-test
before its default is flipped on — "on by default" means "on once tuned", not "ship untested audio live".
Sequencing: F (engine primitives) → E (overlay framework, later) → G/H/I.

**Phase F primitives:** `Audio/Spatializer.cs` (compass-stable pan `Clamp(dxEast/Max(dist,panWidth),-1,1)`
+ `VolumeFor` + `PlayAt` reading `MapCursor` + fixed +Z north), `Audio/AudioMixer.cs` (WAV decode+cache
`LoadFile`/`PlayFile`; resident live-mutable looping voice `PlayLoop`/`SetVoice`/`Remove` with click-free
ramp), `Audio/AudioProbe.cs` (DEBUG `/eval` ear-bench: `Left/Right/Sweep/LoopStart/LoopPan/LoopStop/Bearing`).
Ear-validated (pan sweep L→R correct, loop glides click-free). Kept `AudioMixer` pure (cursor-frame read lives
in `Spatializer.PlayAt`); the `IAudioEngine`/Wwise selector is deferred but REACHABLE in RT — see
[[rt-wwise-3d-feasibility]] (WA's Wwise path is off by default anyway, so the audible "PF is better" was the
classic-engine DSP, now ported below).

**Key insight (from a WA soundscape study):** WA's sonar is ALSO a standing-still Continuous sweep by default
— the cadence isn't the problem. What makes WA feel like a *place*: (1) per-type recorded WAV stems, (2)
live-tracked 3D sources re-panned every frame vs the moving cursor + object's nearest edge, (3) real 3D (ITD
+ front/back low-pass, not pan-only), (4) it's one thin layer under a spoken tiled walk + a continuous
wall-tone bed. (An initial lean synth-ping sweep was rejected for having none of these.)

**WallTones — `Exploration/WallTones.cs`:** 4 looping cardinal voices (N/S/E/W); volume rises as a wall
nears (grid raycast via `GetNeighbourAlongDirection(dir, checkConnectivity:true)` → null on cut edge/off-grid,
dist=(cells−0.5)×1.35, curve t*t, Range 5m); fixed compass pan (E right/W left/N-S centred by timbre). Rides
the Phase F LoopVoice. `Audio/AudioAssets.cs` path helpers. Copied WA's whole `assets/audio` tree into RTAccess
(2 walltone sets + 17 interactable stems + earcons; csproj `assets\**\*.wav` deploy rule). Settings
`exploration.walltones` + volume + set. **Ctrl+F1** cycles Off→When moving→Continuous (spoken, persisted,
`WallTones.ToggleMode` — same chord WrathAccess uses; maintainer wants Pathfinder keys). ~0.5s volume slew:
`_curVol[]` glides to `Curve(dist)*vol` via exp smoothing `k=1-exp(-dt/0.16)`; not-playing/off-grid FADE to
silence (no click); `Teardown` clears `_curVol`. Defaults OFF + volume 25.

**Sonar — `Exploration/Sonar.cs`** (ticked from `Main.OnUpdate` after `WorldModel.Tick`; settings
`exploration.sonar` Off/When-moving/Continuous default Off + volume): **per-type recorded WAV stems** fired
as **live-tracked 3D sources**. `Sonar.StemFor` maps RT taxonomy → `interactables/*.wav` (WA default stems:
units-enemy/ally/neutral, loot-generic/-corpse, door, transition, unknown, trap, mechanism, hazard-zone,
buff-zone; scenery=silent). Sonar's gate uses `CurrentlySeen` (not `DetectableFrom`); the LOS-smart `DetectableFrom` fix IS shipped — in `ScanItem`, wired into the scanner review cycles' `GroupList`, not into Sonar ([[rt-scanner-consistency]]).
Primitives: `Spatializer.cs` +`SpatialCue`/`ISpatialVoice`/`Cue()`; `AudioMixer.cs` +`PlaySpatial()` + nested
`PositionalEmitter` — ported from WA `NAudioEngine.PositionalEmitter`; `Audio/SpatialSources.cs`
(main-thread registry re-places every live voice vs the moving cursor + item.NearestPoint each frame, `Tick`
from Main.OnUpdate after `Sonar.Tick`, `Clear` on area change). **Ctrl+F2** cycles sonar mode.

**DSP brought to WA parity (2026-07-24, this is the "make our sound like PF" fix).** RT had drifted onto an
OLDER generation of WA's spatializer: raw cos/sin pan (far ear → silence at hard pan, "inside one ear"),
rear BiQuad **lowpass + dry/wet blend** (comb-filters/hollows bright stems), **linear** ITD, no head shadow.
Now matches WA's current model — `SpatialCue` fields changed `{LowpassHz,WetMix}`→`{RearShelfDb,FarShadowDb}`:
**capped-ILD `PanGains`** (`MaxIldDb 12`, far ear keeps signal → externalises), **`PanExponent 0.82`**
centre-expansion, **Woodworth ITD** (θ+sinθ), minimum-phase **high-shelf rear cue** (`RearCornerHz 3000`,
`RearMaxCutDb 10`) replacing the lowpass, and a NEW **far-ear head-shadow shelf** (`ShadowCornerHz 1500`,
`ShadowMaxDb 8`). Hand-rolled retunable RBJ `HighShelf` struct in AudioMixer (no per-block alloc, identity at
0 dB). Three A/B toggles `audio.itd`/`audio.front_back_filter`/**`audio.head_shadow`** (all default on). Kept
RT's `Spatializer.Pan` (AudioProbe/PlayAt use it; WA has no standalone `Pan`). **Upstream sync: track WA
`Spatializer.cs` + `NAudioEngine.PositionalEmitter`.** Compile-clean; awaiting maintainer ear-test.

**Master volume (2026-07-24).** `AudioMixer.Master` (volatile float) scales the whole mod mix at a final
`MasterStage` provider between mixer and device; `AudioMixer.RefreshMaster()` reads `audio.master_volume`
(IntSetting, default 100 = no change) each frame from `Main.OnUpdate`. One global attenuation over EVERY voice
(earcons/walltones/sonar). Also fixed `WallTones.Volume` fallback `?? 50`→`?? 25` (matches registry default).

**Sonar cadence made tunable + radius tightened (2026-07-24).** Maintainer ear-test: RT's object sweep felt
gappier than PF ("pauses between sweeps"). VERIFIED the sweep-loop code + cadence values are byte-IDENTICAL to
WA (`SonarSystem`): the "pause" is `RestSec` (400 ms after the last ping of each sweep), and the maintainer's
own PF `settings.json` has NO `overlay.sonar.*` overrides → PF runs the same 400 ms rest. Real divergences were
(a) RT HARDCODED the cadence while WA exposes it, and (b) RT sensed 2× farther (`MaxDist 25 m`/`RefDist 5 m` vs
WA `40 ft≈12 m`/`10 ft≈3 m`) → sparser, quieter, gappier sweeps. Fix: `Sonar.cs` cadence/range consts →
live settings `exploration.sonar_{rest,gap_min,gap_max,ref_dist,max_dist}` (`Defaults.cs` + locale), defaults =
WA's (rest 400/gaps 100–200 ms) with the **radius tightened to 12 m / 3 m**. Maintainer can now set
`sonar_rest`→0 by ear (REPL: `ModSettings.GetSetting<IntSetting>("exploration.sonar_rest").Set(0)`) for a
continuous sweep. `SpreadSec`/`MinVol`/`PanWidth`/`MoveGrace` stay const (WA keeps them const too).

**Review-cursor ping SHIPPED (2026-07-24, compile-only).** `Sonar.PlayReview(item, from)` — a FROZEN positional
one-shot at the item you land on while cycling the scanner (the review origin `from`, NOT cursor-tracked; "this
thing, relative to where you looked"). Hooked at the single `Scanner.Select` chokepoint (covers StepItem/
StepCategory/Review) + `ReSpeakSelection` (O). Uses a root-level cue wav via `AudioMixer.PlaySpatial` (fire-and-
forget, ignore the voice). Setting `exploration.sonar_review_sound` ChoiceSetting {silent, review, tracking},
**default silent** (off-by-default audio law — WA defaults "review", but RT keeps the soundscape silent until
opted in). NOT gated on the sonar mode (selection feedback, plays even with the sweep off). Ported from WA
`SonarSystem.PlayReview`. Enable via REPL: `…GetSetting<ChoiceSetting>("exploration.sonar_review_sound").Set("review")`.
(Still MISSING vs WA: the user-configurable per-TYPE sweep-sound tree — RT hardcodes `Sonar.StemFor` — fits the
future mod settings menu [[rt-mod-settings-menu]].)

**Sonar sweep now gates on `DetectableFrom` (2026-07-24, maintainer request "remember fogged objects too").**
Swept the Snapshot gate `CurrentlySeen`→`DetectableFrom(c)` and FirePing `CurrentlySeen`→`IsVisible`, matching WA's
`SonarSystem` and RT's own scanner review cycles. Effect: remembered persistent OBJECTS under fog (chests/doors/
mechanisms — `ProxyMapObject`/`ProxyDestructible` keep `IsVisible` reveal-latched, lose `CurrentlySeen`) ping again
IF the cursor has clear LOS to them. **Visual-parity law holds automatically** — fogged CREATURES are still excluded
because `ProxyUnit.IsVisible => IsPlayerFaction || IsVisibleForPlayer` and the game clears `IsVisibleForPlayer` under
fog, so `DetectableFrom` short-circuits false at its `!IsVisible` guard; objects behind a wall are excluded by the
LOS check. Closes the Sonar half of the [[rt-scanner-consistency]] divergence. (Maintainer runs RT sonar in
"when_moving" mode, which adds its own movement-tied silences on top.)

**Status:** audio now ON by default (see policy change above). DSP-parity port + master volume + tunable/wider
sonar + fogged-objects gate + review ping SHIPPED compile-only (2026-07-24) and DEPLOYED + live-verified
(2026-07-25: settings register, review.wav loads, review-ping was silent only because its default was silent —
now flipped on). Awaiting full maintainer ear-test: off-centre sonar pings should sit OUTSIDE the head (capped
ILD), rear pings should darken cleanly (no comb/phasey), A/B the three `audio.*` toggles; walltones slew/Ctrl+F1
+ sonar Ctrl+F2. Open: the Wwise
3D engine path (deferred — see [[rt-wwise-3d-feasibility]]; maintainer runs WA on classic so DSP port already
matches what they hear), optional gain-staging tuning (lower hot per-feature defaults toward WA's 40%?), WA's
Shift+F1/F2 play-while-held,
the user-configurable per-TYPE sonar sound tree (StemFor hardcoded); Phase E overlay framework once H/I coexist;
Phase I LogSystem (reconcile with [[rt-message-log]] first). (Review-cursor ping now SHIPPED — see above.)
