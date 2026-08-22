<#
.SYNOPSIS
    Initialise any *uninitialised* git submodules for the repo/worktree this script lives in.

.DESCRIPTION
    A fresh `git worktree` starts with EMPTY submodule directories (external/DiscUtils,
    external/xaml-math, external/tree-sitter-xml), so the `ProjectReference`s into them point at
    .csproj files that do not exist yet - and the tree-sitter grammar has no C sources to compile -
    so Visual Studio / `dotnet build` fail. This populates any missing submodule so the references
    resolve and the build "just works".

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

# tree-sitter-dotnet-bindings carries the grammars as NESTED submodules - 29 of them, plus the runtime.
# Nexaflow compiles only the ones it registers, so init exactly those rather than --recursive: the rest are
# tens of megabytes of generated parser tables for languages nothing here opens. Shallow for the same reason
# (the pin is a commit on a branch we track, so depth 1 still resolves it).
$bindings = Join-Path $root 'external/tree-sitter-dotnet-bindings'
$manifest = Join-Path $root 'tools/tree-sitter-grammars.props'
if ((Test-Path (Join-Path $bindings '.git')) -and (Test-Path $manifest)) {
    $needed = @('tree-sitter-native/tree-sitter')   # the runtime
    foreach ($g in ([xml](Get-Content -LiteralPath $manifest -Raw)).Project.ItemGroup.TreeSitterGrammar) {
        # A grammar with its own Root lives in a different submodule (xml), not inside the bindings repo.
        if (-not $g.Root) { $needed += "tree-sitter-native/tree-sitter-$($g.Include)" }
    }

    $nested = @(& git -C $bindings submodule status 2>$null)
    foreach ($line in $nested) {
        if ($line -match '^-\S+\s+(?<path>\S+)' -and $needed -contains $Matches['path']) {
            $path = $Matches['path']
            Write-Host "ensure-submodules: initialising grammar '$path'..."
            & git -C $bindings -c protocol.file.allow=always submodule update --init --depth 1 -- $path
        }
    }
}
