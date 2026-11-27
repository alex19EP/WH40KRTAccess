---
name: rt-wwise-3d-feasibility
description: "Wwise game-native 3D audio path for RT: REACHABLE (AkSoundEngine API + DefaultListener present) but RT banks are v145 not WoTR's v135, so WA's runtime WwiseBank byte-format needs re-derivation; WA defaults to classic so it's optional"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 3adfa53e-3a54-4be8-9de7-68b7506d7c29
  modified: 2026-07-24T14:33:53.917Z
---

Whether RT can adopt WrathAccess's **Wwise 3D engine path** (playing the mod's WAVs as REAL game-native 3D
emitters through the game's own listener/attenuation/SFX bus, instead of the NAudio stereo mixer). Verified
against the live RT install 2026-07-24. Relevant to any future decision to port WA's `IAudioEngine` /
`WwiseAudio` / `WwiseBank` / `WwiseEngine` / `ListenerAnchor`. Companion to [[rt-audio-pass]].

**REACHABLE — the runtime API is present and unchanged from WoTR:**
- `WH40KRT_Data/Managed` ships `AK.Wwise.Unity.API.dll`, `AK.Wwise.Unity.API.WwiseTypes.dll`,
  `AK.Wwise.Unity.MonoBehaviour.dll`, `Ak.Wwise.Api.WAAPI.dll`, `Kingmaker.Sound.Base.dll`.
- Static class is **`AkSoundEngine`** (NOT renamed to `AkUnitySoundEngine`). Every method WA's `WwiseAudio`
  calls resolves: `LoadBankMemoryCopy`, `PostEvent`, `RenderAudio`, `SetObjectPosition`,
  `SetGameObjectOutputBusVolume`, `StopPlayingID`, `IsInitialized` (some are SWIG `CSharp_*` bindings).
- `AkGameObj` present. `Kingmaker.Sound.DefaultListener` exists in `Code.dll` (same namespace/name as WoTR),
  resolvable via `ObjectRegistry<DefaultListener>.Instance?.MaybeSingle` exactly as WA does it.

**THE BLOCKER — bank binary format differs:** RT's soundbanks are **Wwise v145** (read the BKHD version of
`…/StreamingAssets/Audio/GeneratedSoundBanks/Windows/Init.bnk` = 145). WoTR is **v135**. WA's `WwiseBank.cs`
generates a bank at runtime with a byte-for-byte HIRC layout + game-specific IDs reverse-engineered from WoTR's
`Wrath_Main_Default` (bus `247557814`, attenuation `1001207880`, root 3D ActorMixer `422171592`). None of that
ports directly: the v145 HIRC object shapes must be re-derived AND RT's own bus / attenuation / mixer-template
IDs re-extracted from an RT bank. (An adversarial pass warned `AK_WrongBankVersion` = hard load failure, not a
soft fallback — so this is the real risk, and the "v135 ports unchanged" hope was wrong.)

**WHY IT'S OPTIONAL:** WA's Wwise path is **OFF by default** (`audio.engine = "classic"`). So what a maintainer
hears as "PF audio is better" is WA's **classic NAudio engine with a newer spatializer**, NOT Wwise. The
spatializer DSP was brought to WA parity 2026-07-24 ([[rt-audio-pass]]), which is what actually closes the gap
for a maintainer on classic. Pursue the Wwise port only if the maintainer explicitly runs WA on `wwise` and
wants true game-mixer 3D (the aarn maintainer confirmed they run WA on the default = classic).

**If ever ported, don't miss** (from the plan's adversarial review): `WwiseEngine.cs` (the `IAudioEngine` impl —
build-breaking if omitted); `ListenerAnchor`'s `audio.listener_height` offset + cutscene/mode gating + listener
choice (not just "snap to cursor"); rename the `wa_` event prefix + `"wrathaccess"` bank name to RT; a thin
`NAudioEngine` wrapper because RT's `AudioMixer` is a static singleton not an instantiable engine; add the Ak +
`Kingmaker.Sound.Base` refs to `RTAccess.csproj` AND the CI Refasmer package ([[rt-ci-game-assemblies]]).
