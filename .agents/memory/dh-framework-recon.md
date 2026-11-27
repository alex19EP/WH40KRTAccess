---
name: dh-framework-recon
description: "Dark Heresy (Owlcat's next 40K CRPG) runs the SAME framework lineage as Rogue Trader — what ports as-is, the three type-substitution divergences, and the recon-only handling rule"
metadata:
  node_type: memory
  type: project
---

Reconnaissance on **Warhammer 40,000: Dark Heresy**, Owlcat's next 40K CRPG. Verified
**2026-08-10** by inspecting a playtest build's `WH40KDH_Data/Managed` (**351 assemblies**) at
`D:\Warhammer.40000.Dark.Heresy.Playtest-InsaneRamZes`. Process name `WH40KDH`;
`app.info` = "Owlcat Games / WHDH".

## HANDLING: architecture recon only — do not build on it yet
The build inspected is **Owlcat's own playtest beta**, distributed to customers who preorder the
launch edition on Owlcat's site (`startup.json` carries
`OverrideDisplayedGameVersion = "BETA DEVELOPMENT BUILD - SUBJECT TO CHANGE"`). **Not a leak and
not secret** — the constraint is pre-release churn, not confidentiality. So: **don't invest in DH
features and don't publish a DH mod until there's a public release build.** A mod compiled against
a beta breaks for users, and the API surface is still moving. Keeping DH notes in `.claude/memory/`
(gitignored) rather than `docs/` is a **churn-hygiene call, not a legal one**.

## HEADLINE
**DH runs the same framework lineage as RT.** The divergences are *type substitutions inside an
identical shape*, not architectural rewrites.

## Ports with little or no change
- **Unity 6000.3.11f1** (RT: 6000.0.64f1), **Mono** backend (`MonoBleedingEdge/`), **`0Harmony.dll`
  bundled** → fully decompilable and Harmony-patchable, exactly as [[rt-modding-stack]] describes for RT.
- `Code.dll` and the `Kingmaker.*` family; `Owlcat.Runtime.Core.dll`; `LocalizationShared.dll`;
  and **`RogueTrader.SharedTypes.dll`** — DH literally ships RT's shared-types assembly.
- `AstarPathfindingProject.dll` (the grid substrate the exploration layer sits on),
  `CountingGuard.dll` (the `Game.Keyboard.Disabled` guard), `ContextData.dll`,
  `StatefulRandom.dll`, `StateHasher.dll`.
- `Core.RestServer.dll` + `Core.StateCrawler.dll` + `Core.Cheats.dll` + `Core.Console.dll` →
  **the whole DEBUG dev harness ports** (`/eval`, `/dumpstate`, `/cheat` — see [[rt-dev-harness]]).
- **`UnityModManagerBridge.dll`: UMM is FIRST-PARTY in DH** — the game ships the bridge itself.
  Plus `Utility.ModsInfo.dll` / `Utility.ExtendedModInfo.dll`, so the native mods-list settings
  surface ([[rt-mod-settings-menu]]) likely ports too.

## The three real divergences (shape-preserving)
1. **UI framework split.** RT's single `Owlcat.Runtime.UI.dll` → `Owlcat.UI` / `.Controls` /
   `.Input` / `.Navigation` / `.DH`. **`ViewBase\`1`, `VMBase`, `IViewModel` and
   `IConsoleNavigationEntity`/`Owner`/`Scroll` all survive** (verified present in `Owlcat.UI.dll`):
   same MVVM architecture, relocated. **The namespace moved too**: `Owlcat.Runtime.UI.*` →
   `Owlcat.UI.*`, and RT's `ConsoleTools.NavigationTool` cluster is now
   `Owlcat.UI.FocusLayer` / `Owlcat.UI.Commands` (+ `.Commands.InputSystem`) / `Owlcat.UI.Navigation`.
2. **Reactive layer: UniRx → R3** (`R3.dll`, `R3.Unity.dll`, `ObservableCollections.dll`,
   `ObservableCollections.R3.dll`; **no `UniRx.dll` at all**). Reactive properties still expose
   `.Value`, so the **field-first VM-reading rule holds** — but it's a different type from a
   different assembly, so every `ReactiveProperty<T>` reference needs retargeting.
3. **Input: Rewired → Unity Input System** (`Unity.InputSystem.dll` present, **no `Rewired_*.dll`**).
   **Barely matters**: `UnityEngine.InputLegacyModule.dll` is present, so the raw `Input` poller
   still works and [[rt-input-system-verdict]] survives on the same reasoning — as does the
   mouse-mode / read-the-VMs stance of [[rt-a11y-architecture-verdict]].

## Corrections to the first-pass read (re-verified against the build)
- **"~40 split `Kingmaker.*` assemblies vs RT's monolith" is wrong** — there is no divergence here.
  RT ships **44** `Kingmaker.*` satellite assemblies, DH ships **34** (essentially a subset), and
  **both keep a monolithic `Code.dll`** (RT 14.2 MB, DH 11.0 MB). Same pattern, slightly fewer
  satellites in DH.
- **The real relocation is the game UI.** RT's `Kingmaker.Code.UI.MVVM` VM/View family — the exact
  surface RTAccess reads — lives *inside* `Code.dll`; DH moved it out into **`Owlcat.UI.DH.dll`
  (3.8 MB, the third-largest managed assembly)**. That's the assembly a DH port references for VMs.
- **The uikit was already a Unity package in RT** (`com.owlcatgames.uikit@571e7e55ebc8`, visible in
  RT's PDB paths); DH's is `com.owlcat.uikit@e0adba617622`. "Now a package" isn't the change — the
  rename + split + namespace move is.
- **DH does NOT ship `RogueTrader.GameCore.dll` or `RogueTrader.ModInitializer.dll`.** The native
  Owlcat mod bootstrapper is gone entirely, consistent with UMM being first-party. (It keeps
  `RogueTrader.SharedTypes`, `RogueTrader.Code.ShaderConsts`, `RogueTrader.QA.QAModeExceptionEvents`.)

## Consequence to act on
Because the divergences are type substitutions inside an identical shape, **the shareable surface
between an RT mod and a DH mod is far larger than the BCL-only core — but it cannot be shared by
referencing the same types.** Do not design the sharing seam up front. **Discover it by porting a
thin vertical slice: Speech + `GraphNavigator` + one trivial screen.** The same "authoritative prior
art, different loader" relationship RT has with WrathAccess ([[wotr-access-prior-art]]) is the
mental model for what RT will be to DH.
