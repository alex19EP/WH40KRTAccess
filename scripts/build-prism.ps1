#requires -Version 5.1
<#
.SYNOPSIS
    Builds the native Prism speech library (third_party/prism) into build/native/prism.dll.

.DESCRIPTION
    Prism is a C++23 CMake project vendored as a git submodule. Its own dependencies are
    vendored under third_party/ (PRISM_DEPENDENCY_PROVIDER defaults to BUNDLED), so the
    build needs no network access — only a C++ toolchain, CMake and a generator.

    We build it ourselves rather than committing prism.dll because:
      * a source build with a static CRT (/MT) has no MSVCP140 / VCRUNTIME140 imports, so
        players do not need the VC++ redistributable installed;
      * the pinned submodule commit is the provenance record a committed binary never was.

    The build is short-circuited when build/native/prism.dll is already up to date: the
    stamp file records the submodule commit it was built from, so the common case costs a
    `git rev-parse` and nothing else. A dirty submodule worktree always re-runs CMake
    (a no-op incremental build is ~1s).

.PARAMETER Force
    Rebuild even when the stamp says the artifact is current.

.PARAMETER Clean
    Delete the CMake build tree and the built artifact, then exit.

.PARAMETER Configuration
    CMake build type. Defaults to Release — the mod's Debug build wants a fast, quiet
    prism.dll just as much as the player build does, and Prism is not what we debug.
#>
[CmdletBinding()]
param(
    [switch] $Force,
    [switch] $Clean,
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot  = Split-Path -Parent $PSScriptRoot
$PrismSrc  = Join-Path $RepoRoot 'third_party\prism'
$BuildTree = Join-Path $RepoRoot 'build\prism-build'
$OutDir    = Join-Path $RepoRoot 'build\native'
$OutDll    = Join-Path $OutDir 'prism.dll'
$StampFile = Join-Path $OutDir 'prism.stamp'

if ($Clean) {
    foreach ($p in @($BuildTree, $OutDir)) {
        if (Test-Path $p) { Remove-Item $p -Recurse -Force; Write-Host "Removed $p" }
    }
    exit 0
}

# --- The submodule has to actually be there ------------------------------------------------
if (-not (Test-Path (Join-Path $PrismSrc 'CMakeLists.txt'))) {
    throw "Prism sources not found at $PrismSrc. Run: git submodule update --init third_party/prism"
}

# --- Stamp: the submodule commit the current artifact was built from -----------------------
# A dirty submodule worktree is stamped separately so local source edits are never mistaken
# for the pinned commit's output.
$head = (& git -C $PrismSrc rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $head) { $head = 'unknown' }
$dirty = [bool](& git -C $PrismSrc status --porcelain 2>$null)
$stamp = "$($head.Trim())$(if ($dirty) { '+dirty' })|$Configuration"

if (-not $Force -and -not $dirty -and (Test-Path $OutDll) -and (Test-Path $StampFile)) {
    if ((Get-Content $StampFile -Raw).Trim() -eq $stamp) {
        Write-Host "prism.dll is up to date ($($head.Substring(0, [Math]::Min(12, $head.Length))), $Configuration)."
        exit 0
    }
}

# --- Locate MSVC and import its environment ------------------------------------------------
# Ninja needs cl.exe on PATH, so we import vcvars64 into this session the usual way: run it
# under cmd, dump the resulting environment, and copy it across.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe not found. Install Visual Studio Build Tools with the 'Desktop development with C++' workload."
}

$vsPath = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if (-not $vsPath) {
    throw "No Visual Studio installation with the MSVC x64 toolset was found. Install VS Build Tools with 'Desktop development with C++'."
}
$vsPath = ($vsPath | Select-Object -First 1).Trim()

$vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found under $vsPath." }

Write-Host "Using MSVC from $vsPath"
& "$env:ComSpec" /c "call `"$vcvars`" >nul 2>&1 && set" | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($Matches[1])" -Value $Matches[2] }
}

# --- CMake + generator ---------------------------------------------------------------------
# CMake must be on PATH (VS Build Tools only ships it with the optional "C++ CMake tools"
# component, so we do not assume it lives under the VS install).
if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    throw "cmake was not found on PATH. Install CMake 3.24+ (winget install Kitware.CMake)."
}

# Ninja is much faster than the VS generator, but is not guaranteed to be present, so fall
# back to the VS generator matching whichever toolset vswhere resolved.
if (Get-Command ninja -ErrorAction SilentlyContinue) {
    $generator = @('-G', 'Ninja')
} else {
    $vsMajor = (& $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationVersion | Select-Object -First 1).Split('.')[0]
    $vsGenerator = switch ($vsMajor) {
        '18'    { 'Visual Studio 18 2026' }
        '17'    { 'Visual Studio 17 2022' }
        default { throw "No Ninja on PATH and no known CMake generator for Visual Studio major version $vsMajor." }
    }
    Write-Host "Ninja not found; falling back to the '$vsGenerator' generator."
    $generator = @('-G', $vsGenerator, '-A', 'x64')
}

# --- Configure -----------------------------------------------------------------------------
# MultiThreaded == /MT: link the CRT statically so the shipped DLL imports OS libraries only
# and players need no VC++ redistributable. Everything Prism can optionally build (tests,
# demos, shims, the Godot extension) is off — we want the plain C ABI and nothing else.
$cmakeArgs = @(
    '-S', $PrismSrc
    '-B', $BuildTree
) + $generator + @(
    "-DCMAKE_BUILD_TYPE=$Configuration"
    '-DBUILD_SHARED_LIBS=ON'
    '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded'
    '-DPRISM_ENABLE_TESTS=OFF'
    '-DPRISM_ENABLE_DEMOS=OFF'
    '-DPRISM_ENABLE_SHIMS=OFF'
    '-DPRISM_ENABLE_GDEXTENSION=OFF'
)

Write-Host "Configuring Prism ($Configuration)..."
& cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed ($LASTEXITCODE)." }

Write-Host 'Building Prism...'
& cmake --build $BuildTree --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { throw "CMake build failed ($LASTEXITCODE)." }

# --- Collect -------------------------------------------------------------------------------
# Ninja drops prism.dll in the build root; the VS generator puts it in a per-config subdir.
$built = Get-ChildItem $BuildTree -Recurse -Filter 'prism.dll' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $built) { throw "Build succeeded but prism.dll was not found under $BuildTree." }

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
Copy-Item $built.FullName $OutDll -Force
Set-Content -Path $StampFile -Value $stamp -NoNewline -Encoding ascii

Write-Host ("Built {0} ({1:N0} bytes) from {2}" -f $OutDll, (Get-Item $OutDll).Length, $stamp)
