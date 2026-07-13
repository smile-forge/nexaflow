<#
.SYNOPSIS
    Initialise any *uninitialised* git submodules for the repo/worktree this script lives in.

.DESCRIPTION
    A fresh `git worktree` starts with EMPTY submodule directories (external/DiscUtils,
    external/xaml-math), so the `ProjectReference`s into them point at .csproj files that do not
    exist yet and Visual Studio / `dotnet build` fail to resolve them. This populates any missing
    submodule so the references resolve and the build "just works".

    Safe and idempotent: it only initialises submodules that are NOT yet checked out (the ones
    `git submodule status` marks with a leading '-'). Already-checked-out submodules are left
    completely alone, so this never resets or clobbers in-progress submodule work.

    It is invoked automatically from two places (see docs/externals.md):
      * the committed `.githooks/post-checkout` hook, when a worktree is created;
      * the `EnsureSubmodulesInitialized` MSBuild target in Directory.Build.targets, as a build-time
        self-heal.
    You can also run it by hand from anywhere in the repo.
#>
$ErrorActionPreference = 'Stop'

# tools/ -> repo (or worktree) root.
$root = Split-Path -Parent $PSScriptRoot

$status = @(& git -C $root submodule status 2>$null)
if ($LASTEXITCODE -ne 0 -or $status.Count -eq 0) { return }

foreach ($line in $status) {
    # An uninitialised submodule is reported as "-<sha> <path>" (leading '-', no space after it).
    if ($line -match '^-\S+\s+(?<path>\S+)') {
        $path = $Matches['path']
        Write-Host "ensure-submodules: initialising '$path'..."
        # `--` guards the path; only this submodule is touched.
        & git -C $root -c protocol.file.allow=always submodule update --init -- $path
    }
}
