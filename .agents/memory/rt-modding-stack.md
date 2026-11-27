---
name: rt-modding-stack
description: "How RT is modded — Mono backend, bundled Harmony, OwlcatModification C# API; decompile via justfile"
metadata: 
  node_type: memory
  type: reference
  originSessionId: cf8b65b2-e0e5-4f9a-a1b4-f7dad217dbf6
---

WH40K Rogue Trader modding stack (confirmed from the install at `C:\Program Files (x86)\Steam\steamapps\common\Warhammer 40,000 Rogue Trader`):

- **Mono backend**, not IL2CPP (no `GameAssembly.dll`; 313 managed DLLs). So normal C# reflection + runtime patching work.
- **`0Harmony.dll` ships with the game** — Harmony patching is available to mods out of the box.
- **Official mod API**: `Kingmaker.Modding.OwlcatModification` loads a mod's `Assemblies\*.dll` via `Assembly.LoadFrom`, then invokes the one static method tagged `[OwlcatModificationEnterPoint]` (optionally taking an `OwlcatModification` arg). Mods also support blueprint patches/bundles/localization packs. `RogueTrader.ModInitializer.dll` bootstraps it.
- **UI is MVVM** (`Kingmaker.UI.MVVM`, 345 VMs / 551 Views, Views split PC vs Console) — read ViewModel data, not rendered visuals.

Decompiled reference source lives in `decompiled/` (gitignored, regenerable); each assembly lands in its own `decompiled/<Name>/` subfolder. Rebuild with the tracked **`justfile`**: `just support` (UI/Core/UniRx/SharedTypes/ModInitializer — fast common case), `just all` (EVERY game/dependency assembly the csproj references, minus the native-stub Unity engine modules; the full referenceable set, slow — includes Code+GameCore), `just decompile <Name>` / `just decompile-glob '<pat>'`. `all` mirrors the RTAccess.csproj `<Reference>` globs — keep in sync when references change. Requires `ilspycmd` + `just` on PATH.
