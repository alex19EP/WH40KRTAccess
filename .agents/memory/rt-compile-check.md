---
name: rt-compile-check
description: How to compile-verify RTAccess without deploying (safe while the game is running)
metadata: 
  node_type: memory
  type: project
  originSessionId: abf1c10a-b098-494d-b402-d1371ab4897c
  modified: 2026-07-24T15:44:36.997Z
---

**FIRST, don't ASSUME the game is running — CHECK.** "Safe while the game is running" below is a *conditional*, not a statement that the game IS up. Before choosing compile-only vs. a full deploy, run:

```
Get-Process WH40KRT -ErrorAction SilentlyContinue
```

- **No process → the game is CLOSED.** Just run the full `dotnet build RTAccess.slnx -c Debug` — it compiles *and* the `Deploy` target copies + zips to the UMM folder cleanly (nothing locks the DLL). Don't hedge with "your running game's DLL wasn't touched" — verify state first, then say what's true.
- **Process present → the game is UP** (and possibly driven by another session — see [[rt-live-game-shared-harness]]). Use the compile-only check below, or hot-stage, or close it.

To type/compile-check RTAccess **without** deploying to the live UMM mod folder (safe while the game is running and holding the DLL):

```
dotnet msbuild "E:\Games\modding\WH40KRTAccess\RTAccess\RTAccess.csproj" -t:Compile -p:Configuration=Debug "-p:SolutionDir=E:\Games\modding\WH40KRTAccess\" -v:m -nologo
```

**SHELL gotcha (cost round-trips 2026-07-03):** run this through the **PowerShell tool**, NOT the Bash tool. Git Bash/MSYS2 eats the `SolutionDir` backslashes — `-p:SolutionDir=E:\Games\modding\...\` arrived as `E:GamesmoddingWH40KRTAccess 2`, so `$(SolutionDir)GamePath.props` didn't exist, the import was skipped, `RogueTraderInstallDir` was empty, and EVERY `<Reference>` wildcard (Kingmaker/Unity/TMPro) expanded to nothing → a flood of CS0246 "type/namespace not found" that looks like broken code but is pure path mangling. Verify with `dotnet msbuild <csproj> -getProperty:RogueTraderInstallDir -getProperty:SolutionDir`. Fix: PowerShell + **forward-slash** SolutionDir (MSBuild normalizes slashes, no escaping hazard): `dotnet msbuild E:/Games/modding/WH40KRTAccess/RTAccess/RTAccess.csproj -t:Compile -p:Configuration=Debug -p:SolutionDir=E:/Games/modding/WH40KRTAccess/ -nologo`.

Two gotchas this avoids:
- The csproj has a `Deploy` target `AfterTargets="Build"` that copies output into `%LocalAppDataLow%\Owlcat Games\...\UnityModManager\RTAccess`. A running game locks that DLL, so a full `Build` fails (or half-deploys). `-t:Compile` runs CoreCompile (real csc + analyzers, reports `CS####`) but NOT `Build`, so `Deploy` never fires. The game loads from the UMM folder, not `bin/`, so `bin/` is never locked.
- Building the csproj directly leaves `$(SolutionDir)` = `*Undefined*`, so `GamePath.props` (repo root, sets `RogueTraderInstallDir`) isn't imported and the `GenerateCustomPropsFile` InitialTarget tries to write `*Undefined*GamePath.props` and errors. Pass `-p:SolutionDir=<repo root>\` (trailing backslash) to fix.

Check exit code / grep for `error CS`. **CAVEAT (cost me round-trips 2026-07-02):** `-t:Compile` writes ONLY the intermediate `obj/Debug/<tfm>/RTAccess.dll` — it does NOT run `CopyFilesToOutputDirectory`, so `bin/Debug/RTAccess.dll` stays STALE. It's a compile *check*, not a deployable build. To hot-stage a fix into the UMM folder **while the game runs**: run the FULL `dotnet build -c Debug` (it compiles + copies the fresh assembly to `bin/Debug` BEFORE the `Deploy` target runs), let `Deploy` fail on the locked native DLLs (`prism.dll`/`nvdaControllerClient64.dll` — MSB3021/3027, harmless, they're unchanged), then `Copy-Item bin/Debug/RTAccess.dll → UMM/RTAccess.dll` (managed DLL isn't locked) and SHA-verify. UMM loads mods at process start, so the running game still needs a **full restart** to pick it up. Full deploy+relaunch is otherwise the [[rt-dev-harness]] `/dev-game` skill (close game first). See also [[rt-live-game-shared-harness]].
