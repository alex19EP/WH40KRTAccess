---
name: rt-ci-game-assemblies
description: CI builds the mod without a game install via a self-hosted Refasmer reference-assembly NuGet package (WH40KRT.GameRefs) on GitHub Packages
metadata: 
  node_type: memory
  type: project
  originSessionId: 8adda42b-ceba-416c-a8bf-affb3c6ac3ad
  modified: 2026-07-24T00:18:35.715Z
---

Building RTAccess in CI without a local game install — SHIPPED 2026-07-23
(GitHub Actions `.github/workflows/build.yml`, repo pushed to github.com/alex19EP/WH40KRTAccess).

**Approach: self-hosted reference assemblies** (the Krafs.Rimworld.Ref model).
- `scripts/build-gamerefs.ps1` + `build/gamerefs/WH40KRT.GameRefs.csproj` + `just refs [version]`.
- **JetBrains Refasmer** (`dotnet tool install -g JetBrains.Refasmer.CliTool`) with **`--all`**
  (ignore visibility, keep every member) strips method bodies but preserves non-public members —
  so the **BepInEx publicizer still works**. Verified: clean Release build, 0 warnings, against the
  stripped output only. `-g` internal glob expansion avoids the Windows cmdline-length limit on ~315 paths.
- Package layout mirrors the real install: `assemblies/` = the 315 game DLLs; `UnityModManager/UnityModManager.dll`
  in its own subfolder (the game's Managed dir has NO UMM — it lives under LocalLow). This split matters:
  `Unity*.dll` wildcard would otherwise double-reference UnityModManager.dll.
- Packed with `dotnet pack` (no nuget.exe needed); ~30 MB refs → ~10 MB nupkg. Version = the GAME version from
  `WH40KRT_Data/StreamingAssets/Version.info` (currently **1.6.1.514**, DLC3; NOT the PhysX "4.1.2" or Unity
  "6000.0.64f1" in Player.log — both red herrings). NU5100/NU5128 warnings are EXPECTED and desired (DLLs outside
  lib/ → not auto-referenced; csproj refs them explicitly by path). GitHub Packages versions are IMMUTABLE — bump
  `-Version` every push. CI uses `nuget install ... -DependencyVersion Highest`, so the package version MUST sort
  correctly (a mislabeled higher version would shadow all future ones).

**Why NOT xADDBx's `RogueTraderGameAssemblies`** (the first attempt): it's on GitHub Packages but
**private to xADDBx** — every working example (ToyBox, PlayableX) is xADDBx's OWN repo reading its OWN
package via its own repo token. No outside PAT (classic read:packages) could read it → persistent 403.
Self-hosting removed that dependency entirely.

**csproj change**: added overridable `RogueTraderManagedDir` (defaults to `$(RogueTraderInstallDir)\WH40KRT_Data\Managed`
via GamePath.props locally; CI overrides it to the package's `assemblies/`). Also pass `RogueTraderData`
(→ UnityModManager/) and a non-empty `RogueTraderInstallDir` (skips Player.log-derived GamePath.props gen).

**Consuming in CI needs NO secret**: the package's nuspec `RepositoryUrl` links it to the repo, so the
built-in `GITHUB_TOKEN` + `permissions: packages: read` reads the PRIVATE package — no repo-access grant,
no PAT. (The old `GH_PACKAGES_TOKEN` secret is now unused.) **Publishing** still needs a `write:packages`
PAT (user runs `scripts/build-gamerefs.ps1 -Push -ApiKey <PAT>`) — auth is the user's per [[git-auth-user-handles]].

**Workflow shape** (matches the request): tests (ubuntu, no game deps — see [[ui-paradigm-pivot]] graph core)
+ Release build (windows) on every push & `v*` tag; **rolling `dev` prerelease** recreated each push to main
(`gh release delete dev --cleanup-tag` then recreate, asset `RTAccess-dev.zip`); real release on `v*` tags
(zip versioned from the tag, `--generate-notes`). Dev zip verified: 44 entries, full speech backend, Mono.CSharp
correctly ABSENT from Release. **Tag→release path is wired but UNTESTED** (would mint a real v-release).

Regenerate after a game patch: `just refs 4.1.3` → `scripts/build-gamerefs.ps1 -Version 4.1.3 -Push -ApiKey <PAT>`,
then bump nothing in the workflow (nuget install grabs Highest).
