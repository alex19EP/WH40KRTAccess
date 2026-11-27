---
name: rt-speech-backend
description: Speech backends — prism.dll is BUILT from a submodule (not vendored), PrismConfig is now a 48-byte v3 struct, nvdaControllerClient64.dll is gone, and there are three backends (Prism → SAPI/COM → clipboard)
metadata: 
  node_type: memory
  type: project
  originSessionId: cf8b65b2-e0e5-4f9a-a1b4-f7dad217dbf6
  modified: 2026-08-10T10:59:05.353Z
---

Speech for RTAccess uses **Prism** (https://github.com/ethindp/prism) via a hand-rolled
`[DllImport("prism")]` binding to the native C ABI in `src/RTAccess/Speech/PrismSpeech.cs` — **Prismatoid
targets `net10.0`** and the game's Unity Mono runtime is `net481`, so the managed wrapper cannot load.

**Merged to `main` 2026-08-10** as `a696f78` ("merge: prism becomes a build, and a paused world still
speaks"); the speech commit itself is `cb1a84d` "speech: prism becomes a build, not a binary". The same
merge moved the tree to the `src/` layout (`src/RTAccess/`, `src/Access.Core/`), so pre-merge memories
quoting `RTAccess/…` paths are one level short.

## prism.dll is BUILT, not vendored

It used to be a committed 885 KB blob copied out of the sibling ONI/RimWorld mods. It is now **compiled
from the `third_party/prism` git submodule**, pinned to commit **`2d012de` (v0.17.3-25)**.
`build/Prism.targets` runs `scripts/build-prism.ps1` `BeforeTargets="Build"` and copies the result into
`$(TargetDir)`, where the existing wildcard `Deploy` target picks it up. `just prism [--force|--clean]`
drives it by hand.

**Build cost and requirements.** Prism is C++23/CMake, but its own dependencies are vendored under its
`third_party/` (`PRISM_DEPENDENCY_PROVIDER` defaults to `BUNDLED`), so **the build is offline**. Needs
**CMake 3.24+**, **VS Build Tools with the C++ workload**, and the Windows SDK's **`midl.exe`**. Measured:
**cold ~15 s, no-op ~0.15 s** — the script stamps the artifact with the submodule commit it came from, so
only a moved (or dirty) submodule re-runs CMake. CI caches `build/native` keyed on that same SHA.
Escape hatch **`RTACCESS_SKIP_PRISM=1` / `-p:SkipPrismBuild=true`**: Debug warns and falls through the
backend roster, **Release hard-errors** (a player zip must never ship mute).

Built with a **static CRT (`/MT`)**, so the DLL imports OS libraries only. The old committed blob imported
`MSVCP140` / `VCRUNTIME140` / `MSVCP140_ATOMIC_WAIT` — it had been silently requiring the VC++
redistributable on the player's machine.

## nvdaControllerClient64.dll is GONE (and was already dead weight)

Current Prism generates the NVDA RPC client stubs itself from `idl/nvdaController.idl` via MIDL at build
time and links `rpcrt4`. Verified: **neither the old committed prism.dll nor the new build imports or even
names that file** (checked import table + raw strings), and `acquire_best` still returns the NVDA backend
with it absent. The `LoadLibrary` preload in `PrismSpeech.TryCreate` had therefore been a no-op.

## PrismConfig ABI change — the old binding does not survive it

It is **no longer a 1-byte `{version}`** but a **48-byte struct on x64**. `PRISM_CONFIG_VERSION = 3`:

| offset | field |
| --- | --- |
| 0 | `version` |
| 8 | `registry` |
| 16 | `availability_callback` |
| 24 | `availability_userdata` |
| 32 | `availability_poll_interval_ms` |
| 36 | `availability_debounce_samples` |
| 40 | `availability_backoff_max_ms` |
| 44 | `availability_auto_power_manage` |

Mirror it at **natural alignment — never `Pack = 1`** — and use **`byte` not `bool`** for the tail field so
the struct stays blittable. **Build it in C# rather than calling `prism_config_init()`**: that returns the
struct **by value**, riding the x64 hidden-pointer path through Mono's struct-return marshalling, and all it
does is zero the struct and set `version`. A zeroed tail = global registry, no availability-polling thread.
Verified end-to-end outside the game: 48 bytes, offsets as above, `prism_init` OK, `acquire_best` → NVDA,
`speak` → Success.

`acquire_best` returns an **already-initialized** backend. `prism_backend_initialize` is only needed for
`registry_create`; calling it on an `acquire_best` backend returns `PRISM_ERROR_ALREADY_INITIALIZED`.

## Three backends, not one

`Speaker.Initialize` picks the first that loads: **`PrismSpeech` → `SapiSpeech` → `ClipboardSpeech`**.

- **SAPI 5** is driven over **raw COM via `ComDispatch`** (manual `IDispatch` — Unity's Mono implements
  neither `System.Speech`'s registry internals nor managed COM activation). Adapted from WrathAccess's
  `SapiHandler` minus the settings-driven params and the render-to-PCM path, with static
  `Rate`/`Volume`/`PreferredVoice` knobs awaiting a settings screen.
- **`ClipboardSpeech`** copies each line to the system clipboard and **always constructs**, so speech is
  never a total no-op.

Each `TryCreate` returns `null` rather than throwing. The winner is logged at boot as
`Speech backend: <name>` and exposed as `Speaker.ActiveBackend`.

## Still true

Text is marshalled as **NUL-terminated UTF-8** (ONI's `CharSet.Ansi` corrupts non-ASCII).
`Speaker.Speak` normalizes line breaks (`\r\n`/`\n`/`\r` → `". "`) so the synth pauses; that is the **ONLY**
text pre-processing — no lowercasing / date / quote / phonetic-dictionary transforms. See
[[rt-interrupt-speech-rule]] for when to pass `interrupt`. F12 in-game speaks a test phrase.

**Warning:** the prior-art repos under `E:\Games\modding\not-my\` (**OniAccess**, **RimWorldAccess**) still
pin much older Prism builds — **their `PrismConfig` shape must NOT be copied**.

See [[project-accessibility-mod]].
