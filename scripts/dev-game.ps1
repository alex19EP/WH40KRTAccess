#requires -Version 5
<#
.SYNOPSIS
  RTAccess dev loop: close the game, rebuild (Debug deploys the mod + dev harness to the UMM folder),
  relaunch the game exe directly, and verify the dev server is up.

.DESCRIPTION
  The Debug build's Deploy target copies the mod into the game's UnityModManager folder, but the game
  must be CLOSED first (a running game locks RTAccess.dll). So the order is always kill -> build ->
  launch. The dev HTTP server (port 8772) is gated on the marker file this script keeps armed, so it
  comes up automatically on relaunch regardless of how the game is started or whether it inherits env vars.

  Launch runs the WH40KRT.exe directly (resolved from GamePath.props / Player.log / the default Steam
  path, or $env:RTGameExe): the `steam://rungameid` URL silently no-ops when the Steam client is idle,
  whereas the exe boots reliably. Steam must still be RUNNING (the game needs the Steam API); if the exe
  can't be resolved we fall back to the steam:// URL, which also cold-starts Steam.

.PARAMETER Action
  cycle   (default) kill -> build -> launch -> verify. The full "rebuild and rerun".
  build   kill (to unlock the DLL) -> build only. No launch.
  run     launch -> verify. No build (run whatever is currently deployed).
  restart kill -> launch -> verify. Relaunch the current build without rebuilding.
  kill    just close the game.
  status  report: process, dev-server /health, active screen, cheat-DB command count.
  cheat   run a game cheat command line (-Arg "checks_success"); result lands in the Console log.
  dump    dump live state by dotted path (-Arg "Game.Instance.Player") via StateCrawler.
  log     drain + print the game's Console log channel (cheat output/errors).

  cheat/dump/log wrap the in-process game-console surface (RTAccess/Dev/GameConsole.cs); they need the
  dev server up but NOT the game's own cheats enabled.

.PARAMETER Config   Build configuration. Default Debug (the only one with the dev harness).
.PARAMETER Port     Dev server port. Default 8772.
.PARAMETER WaitSeconds  How long to wait for the dev server after launch. Default 120.
.PARAMETER Arg      Payload for the cheat/dump actions (a command line, or a dotted state path).

.EXAMPLE
  pwsh scripts/dev-game.ps1                 # rebuild + rerun (the usual)
.EXAMPLE
  pwsh scripts/dev-game.ps1 run             # just launch the game
.EXAMPLE
  pwsh scripts/dev-game.ps1 build           # compile + deploy, don't launch
.EXAMPLE
  pwsh scripts/dev-game.ps1 status          # is it up?
.EXAMPLE
  pwsh scripts/dev-game.ps1 cheat -Arg "get_time"    # run a cheat, then: dev-game.ps1 log
.EXAMPLE
  pwsh scripts/dev-game.ps1 dump  -Arg "Game.Instance.Player"   # StateCrawler JSON tree
#>
param(
    [ValidateSet('cycle', 'build', 'run', 'restart', 'kill', 'status', 'cheat', 'dump', 'log')]
    [string]$Action = 'cycle',
    [ValidateSet('Debug', 'Release')]
    [string]$Config = 'Debug',
    [int]$Port = 8772,
    [int]$WaitSeconds = 120,
    [string]$Arg = ''
)

$ErrorActionPreference = 'Stop'

$AppId    = 2186680  # Steam appid — used only for the steam:// launch fallback.
$ProcName = 'WH40KRT'
$Root     = Split-Path $PSScriptRoot -Parent
$Solution = Join-Path $Root 'RTAccess.slnx'
$Marker   = Join-Path $env:USERPROFILE 'AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\RTAccess\devserver.enable'
$PlayerLog = Join-Path $env:USERPROFILE 'AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\Player.log'
$DefaultInstall = 'C:\Program Files (x86)\Steam\steamapps\common\Warhammer 40,000 Rogue Trader'
$Base     = "127.0.0.1:$Port"

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "    $m" -ForegroundColor Green }
function Info($m) { Write-Host "    $m" }

function Get-Game { Get-Process -Name $ProcName -ErrorAction SilentlyContinue }

function Probe([string]$path, [int]$timeout = 4) {
    try { return (curl.exe -s --max-time $timeout "$Base$path" 2>$null) } catch { return $null }
}

function Eval([string]$code, [int]$timeout = 30) {
    try { return (curl.exe -s --max-time $timeout --data $code "$Base/eval" 2>$null) } catch { return $null }
}

# In-process game-console surface (RTAccess/Dev/GameConsole.cs). --data-raw so payloads with @/&/spaces
# reach the body verbatim (a leading @ would otherwise make curl read a file).
function Post([string]$path, [string]$payload, [int]$timeout = 15) {
    try { return (curl.exe -s --max-time $timeout --data-raw $payload "$Base$path" 2>$null) } catch { return $null }
}

function Close-Game {
    Step "Closing $ProcName"
    $p = Get-Game
    if (-not $p) { Info 'not running'; return }
    $p | Stop-Process -Force
    for ($i = 0; $i -lt 30 -and (Get-Game); $i++) { Start-Sleep -Milliseconds 300 }
    if (Get-Game) { throw "$ProcName did not exit" }
    Ok 'closed'
}

function Build-Mod {
    Step "Building $Config"
    & dotnet build $Solution -c $Config --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }
    Ok 'built + deployed'
}

function Arm-Marker {
    New-Item -ItemType Directory -Force -Path (Split-Path $Marker) | Out-Null
    if (-not (Test-Path $Marker)) { New-Item -ItemType File -Path $Marker | Out-Null }
}

# Resolve WH40KRT.exe, preferring the same install-dir sources the build uses. Returns $null if none exist.
function Resolve-GameExe {
    $candidates = @()
    if ($env:RTGameExe) { $candidates += $env:RTGameExe }
    # GamePath.props (written by the build's GenerateCustomPropsFile target).
    $props = Join-Path $Root 'GamePath.props'
    if (Test-Path $props) {
        $m = [regex]::Match((Get-Content $props -Raw), '<RogueTraderInstallDir>(.*?)</RogueTraderInstallDir>')
        if ($m.Success -and $m.Groups[1].Value) { $candidates += (Join-Path $m.Groups[1].Value 'WH40KRT.exe') }
    }
    # The game's own Player.log "Mono path[0]" line — the same source the csproj derives the install dir from.
    if (Test-Path $PlayerLog) {
        try {
            $m = [regex]::Match((Get-Content $PlayerLog -Raw), "Mono path\[0\] = '(.*?)/WH40KRT_Data/Managed'")
            if ($m.Success) { $candidates += (Join-Path ($m.Groups[1].Value -replace '/', '\') 'WH40KRT.exe') }
        } catch { }
    }
    $candidates += (Join-Path $DefaultInstall 'WH40KRT.exe')
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    return $null
}

function Launch-Game {
    Arm-Marker
    $exe = Resolve-GameExe
    if (-not $exe) {
        Write-Warning "WH40KRT.exe not found (set `$env:RTGameExe); falling back to steam://rungameid/$AppId"
        Step "Launching via Steam (appid $AppId)"
        Start-Process "steam://rungameid/$AppId"
        return
    }
    if (-not (Get-Process -Name steam -ErrorAction SilentlyContinue)) {
        Write-Warning 'Steam is not running — the game needs the Steam API to boot. Start Steam first if launch fails.'
    }
    Step "Launching $exe"
    Start-Process $exe
}

function Wait-Server {
    Step "Waiting for dev server on $Base (up to ${WaitSeconds}s)"
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        if ((Probe '/health') -match 'ok') { Ok 'dev server UP'; return $true }
    }
    Write-Warning "dev server did not answer within ${WaitSeconds}s (still booting? check the game window)"
    return $false
}

function Show-Status {
    Step 'Status'
    $p = Get-Game
    Info ("game:        " + ($(if ($p) { "running (PID $($p.Id), since $($p.StartTime.ToString('HH:mm:ss')))" } else { 'not running' })))
    $h = Probe '/health'
    if ($h -notmatch 'ok') { Info "dev server:  down / unreachable on $Base"; return }
    Info "dev server:  UP on $Base"
    # Confirmed members only (Phase 0): main menu vs. the loaded area name.
    $where = Eval 'Kingmaker.Code.UI.MVVM.VM.MainMenu.MainMenuUI.IsActive ? "MainMenu" : (Kingmaker.Game.Instance?.CurrentlyLoadedArea?.name ?? "(no area)")'
    if ($where) { Info ("where:       " + (($where -replace '^=>\s*', '').Trim())) }
    # Game-console surface: count the cheat DB (Methods[]) as a liveness signal for GameConsole.
    $known = Probe '/known'
    if ($known) {
        $n = ([regex]::Matches($known, '"Name"')).Count
        Info ("cheat DB:    reachable (~$n known entries via /known)")
    }
}

function Require-Arg($what) {
    if ([string]::IsNullOrWhiteSpace($Arg)) { throw "action '$Action' needs -Arg <$what>" }
}

switch ($Action) {
    'cycle'   { Close-Game; Build-Mod; Launch-Game; [void](Wait-Server) }
    'build'   { Close-Game; Build-Mod }
    'run'     { Launch-Game; [void](Wait-Server) }
    'restart' { Close-Game; Launch-Game; [void](Wait-Server) }
    'kill'    { Close-Game }
    'status'  { Show-Status }
    'cheat'   { Require-Arg 'command line'; Step "cheat: $Arg"; (Post '/cheat' $Arg) | Write-Host }
    'dump'    { Require-Arg 'dotted state path'; Step "dump: $Arg"; (Post '/dumpstate' $Arg) | Write-Host }
    'log'     { Step 'Console log'; (Probe '/log') | Write-Host }
}

Step 'Done'
