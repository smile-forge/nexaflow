<#
.SYNOPSIS
    Install this repo's committed git hooks (.githooks/*) into the shared hooks directory so they
    run for the main checkout AND every git worktree. Run once per clone; idempotent.

.DESCRIPTION
    Worktrees share the *common* git directory's hooks (`<repo>/.git/hooks`), and `git worktree add`
    runs `post-checkout` from there — which is how a new worktree auto-initialises its submodules
    (see .githooks/post-checkout and docs/externals.md). A relative `core.hooksPath` is NOT used
    because git resolves it against the invoking checkout, so it wouldn't fire reliably for a
    freshly-added worktree.

    This script copies every file from `.githooks/` into the common hooks dir (normalising to LF so
    git-bash can execute them) and clears any leftover `core.hooksPath` so the default hooks dir is
    honoured. Safe to re-run.
#>
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot          # tools/ -> repo/worktree root
$sourceDir  = Join-Path $repoRoot '.githooks'
if (-not (Test-Path $sourceDir)) { throw "No .githooks directory at '$sourceDir'." }

# The hooks directory shared by the main checkout and all worktrees.
$commonGitDir = (& git -C $repoRoot rev-parse --path-format=absolute --git-common-dir).Trim()
$hooksDir     = Join-Path $commonGitDir 'hooks'
New-Item -ItemType Directory -Force -Path $hooksDir | Out-Null

foreach ($file in Get-ChildItem -File $sourceDir) {
    $lf   = (Get-Content -Raw $file.FullName) -replace "`r`n", "`n"
    $dest = Join-Path $hooksDir $file.Name
    [System.IO.File]::WriteAllText($dest, $lf)   # UTF-8, no BOM, LF preserved
    Write-Host "installed hook: $($file.Name) -> $dest"
}

# A stray core.hooksPath (e.g. a stale absolute path from a moved clone) would override the shared
# hooks dir and silently disable the hooks above. Clear it so the default location is used.
$existing = & git -C $repoRoot config --local --get core.hooksPath 2>$null
if ($LASTEXITCODE -eq 0 -and $existing) {
    & git -C $repoRoot config --local --unset core.hooksPath
    Write-Host "cleared stale core.hooksPath ('$existing')"
}

Write-Host "Done. New worktrees will now auto-initialise their submodules on creation."
