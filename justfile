# Decompile Owlcat 40K game assemblies for accessibility-mod reference.
#
# Two games are configured, selected with the `game` variable:
#   rt  Warhammer 40,000: Rogue Trader   (the shipping target — the default)
#   dh  Warhammer 40,000: Dark Heresy    (playtest beta; ARCHITECTURE RECON ONLY)
#
# Requires: `ilspycmd` (dotnet tool) and `just` on PATH.
#   dotnet tool install --global ilspycmd
#
# The whole `decompiled/` tree is .gitignored (regenerable from the game install),
# so this justfile is the source of truth for how to rebuild it. Output is namespaced
# per game — `decompiled/<game>/<AssemblyName>/` — because the two builds share many
# assembly names (Code.dll, Kingmaker.*, Owlcat.Runtime.Core, RogueTrader.SharedTypes)
# with different contents, so a flat tree would silently mix them.
#
# Usage:
#   just                       # list recipes
#   just support               # decompile the libs the mod needs most (the common case, fast)
#   just all                   # decompile every assembly of the selected game (slow)
#   just decompile <Name>      # a single assembly, e.g. just decompile Code
#   just decompile-glob 'Kingmaker*.dll'   # every assembly matching a glob
#   just game=dh support       # ...any recipe above, against the Dark Heresy build
#   just games                 # show both configured Managed dirs and whether they exist
#   just managed='D:/path/...' all         # override the Managed dir outright
#
# `test`, `refs` and `publish` are RT-only (they build the shipping mod / its CI refs
# package) and ignore `game`.

set windows-shell := ["pwsh", "-NoLogo", "-NoProfile", "-Command"]

# Load a git-ignored .env at the repo root if present (supplies GH_PACKAGES_TOKEN for
# `just publish`). Absent .env is fine — recipes that don't need it are unaffected.
set dotenv-load := true

# Which game to operate on: "rt" (Rogue Trader) or "dh" (Dark Heresy).
game := "rt"

# Per-game Managed folders. `managed` below can still be overridden directly.
rt_managed := "C:/Program Files (x86)/Steam/steamapps/common/Warhammer 40,000 Rogue Trader/WH40KRT_Data/Managed"
dh_managed := "D:/Warhammer.40000.Dark.Heresy.Playtest-InsaneRamZes/WH40KDH_Data/Managed"

# Path to the selected game's Managed assemblies folder.
managed := if game == "dh" { dh_managed } else { rt_managed }

# Per-game LocalLow folder name (holds Player.log, saves and the UMM mod folder).
lowdir := if game == "dh" { "WHDH" } else { "Warhammer 40000 Rogue Trader" }

# Base output directory (one subfolder per assembly, namespaced by game).
out := "decompiled" / game

# The support libs each game needs most — the fast common case, a subset of `all`.
#
# RT:
#   Owlcat.Runtime.UI     - ViewBase<T> + the console focus/navigation system (the screen-reader hook)
#   Owlcat.Runtime.Core   - reactive properties driving the MVVM bindings, base utils, input plumbing
#   Owlcat.Runtime.UniRx  - the reactive primitives behind those bindings
#   Owlcat.Runtime.Visual - render pipeline; holds the fog-of-war reveal mask (FogOfWarArea + Waaagh FogOfWar passes) the a11y "is this tile explored?" probe reads
#   RogueTrader.SharedTypes    - small shared types referenced everywhere
#   RogueTrader.ModInitializer - how the OwlcatModification mod loader boots
#
# DH plays the same roles from different assemblies (see .claude/memory/dh-framework-recon.md):
#   Owlcat.UI{,.Controls,.Input,.Navigation} - the split successor to Owlcat.Runtime.UI;
#                            ViewBase`1 / VMBase / IViewModel / IConsoleNavigation* all survive in Owlcat.UI
#   Owlcat.UI.DH          - the GAME's MVVM VMs/Views (Kingmaker.Code.UI.MVVM). In RT these live
#                           inside Code.dll; DH moved them out, so this is the screen-reading surface
#   R3                    - replaces UniRx as the reactive layer (.Value still exists)
#   UnityModManagerBridge - replaces RogueTrader.ModInitializer; UMM is first-party in DH
rt_support := "Owlcat.Runtime.UI Owlcat.Runtime.Core Owlcat.Runtime.UniRx Owlcat.Runtime.Visual RogueTrader.SharedTypes RogueTrader.ModInitializer"
dh_support := "Owlcat.UI Owlcat.UI.Controls Owlcat.UI.Input Owlcat.UI.Navigation Owlcat.UI.DH Owlcat.Runtime.Core R3 Owlcat.Runtime.Visual RogueTrader.SharedTypes UnityModManagerBridge"
support_libs := if game == "dh" { dh_support } else { rt_support }

# The full assembly set per game, as filename globs.
#
# The RT list mirrors the <Reference> set in src/RTAccess/RTAccess.csproj (minus the Unity engine
# modules, which are native stubs) — keep the two in sync when references are added/removed.
# The DH list has no csproj to mirror; it is the same framework families as observed in the
# playtest build, with the substituted layers (R3/ObservableCollections instead of UniRx,
# Unity.InputSystem instead of Rewired, UnityModManagerBridge for the mod loader).
rt_globs := "Kingmaker*.dll Utility*.dll Core*.dll Owlcat*.dll RogueTrader*.dll Code.dll LocalizationShared.dll UniRx.dll Rewired_Core.dll ContextData.dll StateHasher.dll CountingGuard.dll AstarPathfindingProject.dll Newtonsoft.Json.dll 0Harmony.dll"
dh_globs := "Kingmaker*.dll Utility*.dll Core*.dll Owlcat*.dll RogueTrader*.dll Code.dll LocalizationShared.dll R3*.dll ObservableCollections*.dll Unity.InputSystem*.dll UnityModManagerBridge.dll ContextData.dll StatefulRandom.dll StateHasher.dll CountingGuard.dll AstarPathfindingProject.dll Newtonsoft.Json.dll 0Harmony.dll"
all_globs := if game == "dh" { dh_globs } else { rt_globs }

# List available recipes.
default:
    @just --list

# Decompile a single assembly by name (no .dll suffix) into {{out}}/<Name>/.
decompile name:
    @echo "Decompiling {{name}} ({{game}})"
    ilspycmd "{{managed}}/{{name}}.dll" -o "{{out}}/{{name}}" -p

# Decompile every assembly matching a filename glob, e.g. just decompile-glob 'Kingmaker*.dll'.
# Each match lands in its own {{out}}/<Name>/ subfolder; `all` fans out over the glob list this way.
decompile-glob pattern:
    @Get-ChildItem -Path "{{managed}}" -Filter "{{pattern}}" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "Decompiling $($_.BaseName)"; ilspycmd $_.FullName -o "{{out}}/$($_.BaseName)" -p }

# Decompile UnityModManager.dll, which lives in the UMM folder under LocalLow, not the Managed dir.
# DH has no such folder unless UMM has been installed there — it ships UnityModManagerBridge.dll
# in Managed instead (covered by `all`), so the skip below is the expected DH outcome.
umm:
    @$dll = Join-Path $env:LOCALAPPDATA "..\LocalLow\Owlcat Games\{{lowdir}}\UnityModManager\UnityModManager.dll"; if (Test-Path $dll) { Write-Host "Decompiling UnityModManager"; ilspycmd (Resolve-Path $dll).Path -o "{{out}}/UnityModManager" -p } else { Write-Host "SKIP UnityModManager (not found: $dll)" }

# Decompile the support libs the selected game needs most (fast common case; a subset of `all`).
# Names absent from the selected build are skipped rather than failing the run.
support:
    @"{{support_libs}}" -split '\s+' | Where-Object { $_ } | ForEach-Object { $n = $_; $p = "{{managed}}/$n.dll"; if (Test-Path $p) { Write-Host "Decompiling $n ({{game}})"; ilspycmd $p -o "{{out}}/$n" -p } else { Write-Host "SKIP $n (not in the {{game}} build)" } }

# Decompile EVERY game/dependency assembly of the selected game (slow; includes Code.dll).
all:
    @"{{all_globs}}" -split '\s+' | Where-Object { $_ } | ForEach-Object { just game={{game}} decompile-glob $_ }
    @just game={{game}} umm

# List the assemblies available in the selected game's Managed folder.
list:
    @Get-ChildItem -Path "{{managed}}" -Filter *.dll | Select-Object -ExpandProperty Name | Sort-Object

# Verify the selected game's Managed folder exists.
check:
    @if (Test-Path "{{managed}}") { Write-Host "OK ({{game}}): {{managed}}" } else { Write-Host "MISSING ({{game}}): {{managed}}"; exit 1 }

# Show every configured game, its Managed dir, and whether that dir is present.
games:
    @foreach ($g in @(@{n="rt"; p="{{rt_managed}}"}, @{n="dh"; p="{{dh_managed}}"})) { $mark = if (Test-Path $g.p) { "OK     " } else { "MISSING" }; $sel = if ($g.n -eq "{{game}}") { "*" } else { " " }; Write-Host "$sel $($g.n)  $mark  $($g.p)" }

# Run the graph-core unit tests. Invokes the tests csproj DIRECTLY — never the slnx,
# whose Deploy target (AfterTargets=Build) would fight the UMM-locked RTAccess.dll.
test:
    dotnet test tests/Access.Core.Tests/Access.Core.Tests.csproj

# Rebuild the WH40KRT.GameRefs NuGet package (Refasmer-stripped game assemblies for CI).
# Version auto-detected from WH40KRT_Data/StreamingAssets/Version.info. Use `just publish` to push.
#   just refs                    # build build/gamerefs/out/*.nupkg at the installed game version
#   just refs 1.6.2.x            # override the version
refs version='':
    pwsh -NoProfile -File scripts/build-gamerefs.ps1 -Version "{{version}}"

# Build AND publish WH40KRT.GameRefs to GitHub Packages. Needs GH_PACKAGES_TOKEN
# (a PAT with the write:packages scope) in a .env file at the repo root — see .env.example.
# Version auto-detected from the install; GitHub Packages versions are immutable so each
# game build gets its own package version.
#   just publish                 # publish at the installed game version
#   just publish 1.6.2.x         # override the version
publish version='':
    pwsh -NoProfile -File scripts/build-gamerefs.ps1 -Version "{{version}}" -Push
