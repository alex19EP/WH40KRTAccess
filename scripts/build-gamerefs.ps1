<#
.SYNOPSIS
    Build (and optionally publish) the WH40KRT.GameRefs NuGet package.

.DESCRIPTION
    Runs JetBrains Refasmer over the game's Managed assemblies plus UnityModManager.dll to
    produce metadata-only reference assemblies (method bodies stripped), then packs them into
    a NuGet package consumed by CI (.github/workflows/build.yml) so the mod can be built
    without a local game install. Nothing game-derived is committed to the repo — this
    regenerates from the local install on demand (rerun it after a game patch).

    The reference assemblies keep ALL members (refasmer --all), including non-public ones, so
    the BepInEx publicizer in RTAccess.csproj can still expose them.

.PARAMETER Version
    Package version = the game version from WH40KRT_Data/StreamingAssets/Version.info (e.g.
    1.6.1.514). GitHub Packages versions are immutable, so bump this for every push.

.PARAMETER Managed
    The game's Managed directory. Defaults to the standard Steam install path.

.PARAMETER Push
    After packing, push the .nupkg to GitHub Packages under alex19EP. Requires -ApiKey.

.PARAMETER ApiKey
    A GitHub PAT with the write:packages scope. Only needed with -Push. Defaults to the
    GH_PACKAGES_TOKEN environment variable, which `just publish` loads from a .env file.

.EXAMPLE
    pwsh scripts/build-gamerefs.ps1 -Version 1.6.1.514                       # build only
    pwsh scripts/build-gamerefs.ps1 -Version 1.6.1.514 -Push -ApiKey ghp_xxx # build + publish
    just publish 1.6.1.514                                                   # build + publish via .env
#>
[CmdletBinding()]
param(
    [string]$Version = '1.6.1.514',
    [string]$Managed = 'C:/Program Files (x86)/Steam/steamapps/common/Warhammer 40,000 Rogue Trader/WH40KRT_Data/Managed',
    [switch]$Push,
    [string]$ApiKey = $env:GH_PACKAGES_TOKEN
)
$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$pkgDir  = Join-Path $repo 'build/gamerefs'
$asmDir  = Join-Path $pkgDir 'assemblies'
$ummDir  = Join-Path $pkgDir 'UnityModManager'
$outDir  = Join-Path $pkgDir 'out'
$ummDll  = Join-Path $env:LOCALAPPDATA '..\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\UnityModManager.dll'

if (-not (Test-Path $Managed))  { throw "Managed dir not found: $Managed" }
if (-not (Test-Path $ummDll))   { throw "UnityModManager.dll not found: $ummDll (enable UMM once in-game)" }

# Ensure Refasmer is available.
if (-not (Get-Command refasmer -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing JetBrains.Refasmer.CliTool...'
    dotnet tool install --global JetBrains.Refasmer.CliTool | Out-Null
    $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
}

# Fresh staging dirs.
foreach ($d in @($asmDir, $ummDir, $outDir)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Path $d -Force | Out-Null
}

Write-Host "Refasming Managed dir -> assemblies/ ..."
# -g expands the glob internally (avoids the Windows command-line length limit on ~315 paths).
refasmer -g --all -c -O $asmDir "$Managed/*.dll"
if ($LASTEXITCODE) { throw "refasmer failed on Managed dir ($LASTEXITCODE)" }

Write-Host "Refasming UnityModManager.dll -> UnityModManager/ ..."
refasmer --all -c -o (Join-Path $ummDir 'UnityModManager.dll') (Resolve-Path $ummDll).Path
if ($LASTEXITCODE) { throw "refasmer failed on UnityModManager.dll ($LASTEXITCODE)" }

Write-Host ("Refasmed {0} assemblies + UnityModManager.dll" -f (Get-ChildItem "$asmDir/*.dll").Count)

Write-Host "Packing WH40KRT.GameRefs $Version ..."
dotnet pack (Join-Path $pkgDir 'WH40KRT.GameRefs.csproj') -o $outDir "-p:Version=$Version" -v:m
if ($LASTEXITCODE) { throw "dotnet pack failed ($LASTEXITCODE)" }

$nupkg = Get-ChildItem "$outDir/*.nupkg" | Select-Object -First 1
Write-Host ("Built {0} ({1:N1} MB)" -f $nupkg.Name, ($nupkg.Length/1MB)) -ForegroundColor Green

if ($Push) {
    if (-not $ApiKey) { throw '-Push needs a token: pass -ApiKey, or set GH_PACKAGES_TOKEN (a write:packages PAT) in .env and use `just publish`' }
    Write-Host 'Pushing to GitHub Packages (alex19EP)...'
    dotnet nuget push $nupkg.FullName `
        --source 'https://nuget.pkg.github.com/alex19EP/index.json' `
        --api-key $ApiKey
    if ($LASTEXITCODE) { throw "nuget push failed ($LASTEXITCODE)" }
    Write-Host 'Pushed. Ensure the package is public (or granted to the repo) so CI GITHUB_TOKEN can read it.' -ForegroundColor Yellow
}
