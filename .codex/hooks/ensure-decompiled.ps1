#requires -Version 5
# Codex SessionStart hook. Worktrees share the main checkout's git-ignored
# decompiled/ directory through a Windows junction (no administrator required).
# Existing paths are left alone; missing references and failures are non-fatal.
$ErrorActionPreference = 'Stop'

try {
    # Resolve from the script, not the session cwd, which can be a subdirectory.
    $projectRoot = git -C $PSScriptRoot rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0) { exit 0 }

    $link = Join-Path $projectRoot 'decompiled'
    if (Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue) { exit 0 }

    $commonGitDir = git -C $projectRoot rev-parse --path-format=absolute --git-common-dir 2>$null
    if ($LASTEXITCODE -ne 0) { exit 0 }
    $mainRoot = Split-Path -Parent $commonGitDir
    if ([IO.Path]::GetFullPath($mainRoot) -eq [IO.Path]::GetFullPath($projectRoot)) { exit 0 }

    $target = Join-Path $mainRoot 'decompiled'
    if (-not (Test-Path -LiteralPath $target -PathType Container)) { exit 0 }

    New-Item -ItemType Junction -Path $link -Target $target | Out-Null
    Write-Output "[RTAccess] linked decompiled/ -> $target"
}
catch {
    [Console]::Error.WriteLine("[RTAccess] Could not link decompiled/: {0}", $_.Exception.Message)
}
exit 0
