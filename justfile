# Decompile Warhammer 40,000: Rogue Trader assemblies for accessibility-mod reference.
#
# Requires: `ilspycmd` (dotnet tool) and `just` on PATH.
#   dotnet tool install --global ilspycmd
#
# The whole `decompiled/` tree is .gitignored (regenerable from the game install),
# so this justfile is the source of truth for how to rebuild it. Each assembly lands
# in its own subfolder: `decompiled/<AssemblyName>/`.
#
# Usage:
#   just                       # list recipes
#   just support               # decompile the libs the mod needs most (the common case, fast)
#   just all                   # decompile EVERY assembly the solution references (slow)
#   just decompile <Name>      # a single assembly, e.g. just decompile Code
#   just decompile-glob 'Kingmaker*.dll'   # every assembly matching a glob
#   just managed='D:/path/...' all         # override the game's Managed dir
#
# `all` mirrors the <Reference> set in RTAccess/RTAccess.csproj (minus the Unity engine
# modules, which are native stubs) — keep the two in sync when references are added/removed.

set windows-shell := ["pwsh", "-NoLogo", "-NoProfile", "-Command"]

# Load a git-ignored .env at the repo root if present (supplies GH_PACKAGES_TOKEN for
# `just publish`). Absent .env is fine — recipes that don't need it are unaffected.
set dotenv-load := true

# Path to the game's Managed assemblies folder (override on the command line if it differs).
managed := "C:/Program Files (x86)/Steam/steamapps/common/Warhammer 40,000 Rogue Trader/WH40KRT_Data/Managed"

# Base output directory (one subfolder per assembly).
out := "decompiled"

# List available recipes.
default:
    @just --list

# Decompile a single assembly by name (no .dll suffix) into {{out}}/<Name>/.
decompile name:
    @echo "Decompiling {{name}}"
    ilspycmd "{{managed}}/{{name}}.dll" -o "{{out}}/{{name}}" -p

# Decompile every assembly matching a filename glob, e.g. just decompile-glob 'Kingmaker*.dll'.
# Each match lands in its own {{out}}/<Name>/ subfolder; `all` fans out over the csproj wildcards this way.
decompile-glob pattern:
    @Get-ChildItem -Path "{{managed}}" -Filter "{{pattern}}" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "Decompiling $($_.BaseName)"; ilspycmd $_.FullName -o "{{out}}/$($_.BaseName)" -p }

# Decompile UnityModManager.dll, which lives in the UMM folder under LocalLow, not the Managed dir.
umm:
    @$dll = Join-Path $env:LOCALAPPDATA "..\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\UnityModManager.dll"; if (Test-Path $dll) { Write-Host "Decompiling UnityModManager"; ilspycmd (Resolve-Path $dll).Path -o "{{out}}/UnityModManager" -p } else { Write-Host "SKIP UnityModManager (not found: $dll)" }

# Decompile the support libs the mod needs most (fast common case; a subset of `all`).
#   Owlcat.Runtime.UI     - ViewBase<T> + the console focus/navigation system (the screen-reader hook)
#   Owlcat.Runtime.Core   - reactive properties driving the MVVM bindings, base utils, input plumbing
#   Owlcat.Runtime.UniRx  - the reactive primitives behind those bindings
#   Owlcat.Runtime.Visual - render pipeline; holds the fog-of-war reveal mask (FogOfWarArea + Waaagh FogOfWar passes) the a11y "is this tile explored?" probe reads
#   RogueTrader.SharedTypes    - small shared types referenced everywhere
#   RogueTrader.ModInitializer - how the OwlcatModification mod loader boots
support: (decompile "Owlcat.Runtime.UI") (decompile "Owlcat.Runtime.Core") (decompile "Owlcat.Runtime.UniRx") (decompile "Owlcat.Runtime.Visual") (decompile "RogueTrader.SharedTypes") (decompile "RogueTrader.ModInitializer")

# Mirrors RTAccess.csproj <Reference> globs, minus the Unity* engine modules (native stubs).
# Decompile EVERY game/dependency assembly the solution references (slow; includes Code.dll + GameCore).
all: (decompile-glob "Kingmaker*.dll") (decompile-glob "Utility*.dll") (decompile-glob "Core*.dll") (decompile-glob "Owlcat*.dll") (decompile-glob "RogueTrader*.dll") (decompile-glob "Code.dll") (decompile-glob "LocalizationShared.dll") (decompile-glob "UniRx.dll") (decompile-glob "Rewired_Core.dll") (decompile-glob "ContextData.dll") (decompile-glob "StateHasher.dll") (decompile-glob "CountingGuard.dll") (decompile-glob "AstarPathfindingProject.dll") (decompile-glob "Newtonsoft.Json.dll") (decompile-glob "0Harmony.dll") umm

# List the assemblies available in the game's Managed folder.
list:
    @Get-ChildItem -Path "{{managed}}" -Filter *.dll | Select-Object -ExpandProperty Name | Sort-Object

# Verify the configured Managed folder exists.
check:
    @if (Test-Path "{{managed}}") { Write-Host "OK: {{managed}}" } else { Write-Host "MISSING: {{managed}}"; exit 1 }

# Run the graph-core unit tests. Invokes the tests csproj DIRECTLY — never the slnx,
# whose Deploy target (AfterTargets=Build) would fight the UMM-locked RTAccess.dll.
test:
    dotnet test tests/RTAccess.Tests.csproj

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
