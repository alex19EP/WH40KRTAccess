# Decompile Warhammer 40,000: Rogue Trader assemblies for accessibility-mod reference.
#
# Requires: `ilspycmd` (dotnet tool) and `just` on PATH.
#   dotnet tool install --global ilspycmd
#
# The whole `decompiled/` tree is .gitignored (regenerable from the game install),
# so this justfile is the source of truth for how to rebuild it.
#
# Usage:
#   just                       # list recipes
#   just support               # decompile the libs the mod needs (the common case)
#   just all                   # support + the big main assemblies
#   just decompile <Name>      # decompile a single assembly, e.g. just decompile Code
#   just managed='D:/path/...' support   # override the game's Managed dir

set windows-shell := ["pwsh", "-NoLogo", "-NoProfile", "-Command"]

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
    ilspycmd "{{managed}}/{{name}}.dll" -o "{{out}}" -p

# Decompile the support libs the mod needs (UI focus system, reactive bindings, mod loader).
#   Owlcat.Runtime.UI     - ViewBase<T> + the console focus/navigation system (the screen-reader hook)
#   Owlcat.Runtime.Core   - reactive properties driving the MVVM bindings, base utils, input plumbing
#   Owlcat.Runtime.UniRx  - the reactive primitives behind those bindings
#   Owlcat.Runtime.Visual - render pipeline; holds the fog-of-war reveal mask (FogOfWarArea + Waaagh FogOfWar passes) the a11y "is this tile explored?" probe reads
#   RogueTrader.SharedTypes    - small shared types referenced everywhere
#   RogueTrader.ModInitializer - how the OwlcatModification mod loader boots
support: (decompile "Owlcat.Runtime.UI") (decompile "Owlcat.Runtime.Core") (decompile "Owlcat.Runtime.UniRx") (decompile "Owlcat.Runtime.Visual") (decompile "RogueTrader.SharedTypes") (decompile "RogueTrader.ModInitializer")

# The large main assemblies (slow). Code.dll is the 13 MB game core.
core: (decompile "Code") (decompile "RogueTrader.GameCore")

# Everything used as reference material.
all: support core

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
