<#
.SYNOPSIS
    Initialise any *uninitialised* git submodules for the repo/worktree this script lives in.

.DESCRIPTION
    A fresh `git worktree` starts with EMPTY submodule directories (external/DiscUtils,
    external/AngouriMath, external/tree-sitter-xml, external/PdfPig, …), so the `ProjectReference`s into them point at
    .csproj files that do not exist yet - and the tree-sitter grammar has no C sources to compile -
    so Visual Studio / `dotnet build` fail. This populates any missing submodule so the references
    resolve and the build "just works".

    Safe and idempotent: it only touches submodules whose checkout never completed - not initialised (the
    ones `git submodule status` marks with a leading '-'), or cloned by a run stopped before it wrote their
    files. Every other submodule is left completely alone, so this never resets or clobbers in-progress
    submodule work.

    Fast, because whatever creates a worktree stops waiting for its post-checkout hook after half a minute or
    so, and fetching every submodule from GitHub one after another takes over a minute - so the ones last in
    line would be left empty. A linked worktree clones each submodule from the main checkout's own copy of it,
    which needs no network, then points its origin back at the URL .gitmodules gives; only a pin the main
    checkout does not hold yet is fetched. Every pass runs its clones side by side.

    One run at a time per checkout. The hook and every project of a parallel build can arrive together, and
    two `submodule update`s in one checkout fail on each other's locks; the later ones wait, then find nothing
    left to do.

    A run that completes, with every submodule checked out, leaves external/.submodules-populated; one with
    anything to do removes it first, so a run stopped part-way leaves none. The build's self-heal runs this
    when that stamp is missing.

    It is invoked automatically from two places (see docs/externals.md):
      * the committed `.githooks/post-checkout` hook, when a worktree is created;
      * the `EnsureSubmodulesInitialized` MSBuild target in tools/ensure-submodules.targets, as a
        build-time self-heal, before a project's or the solution's restore.
    You can also run it by hand from anywhere in the repo. It exits 1, naming each submodule still not checked
    out at its pin, when any could not be.
#>
$ErrorActionPreference = 'Stop'

# tools/ -> repo (or worktree) root.
$root = Split-Path -Parent $PSScriptRoot

$gitDir = @(& git -C $root rev-parse --path-format=absolute --git-dir 2>$null)
if ($LASTEXITCODE -ne 0 -or $gitDir.Count -eq 0) { return }
$gitDir    = $gitDir[0].Trim()
$commonDir = (& git -C $root rev-parse --path-format=absolute --git-common-dir).Trim()
$stamp     = Join-Path $root 'external/.submodules-populated'

# Long paths, because PdfPig's test documents carry names that run past MAX_PATH once they are under a worktree,
# and a checkout that cannot write one fails. The file protocol, for a clone from the main checkout's copy.
# --force, because every submodule given to an update here is one whose checkout never completed: one stopped
# after its clone already has its HEAD at the pin, and only a forced checkout writes its files.
$gitOptions = @('-c', 'protocol.file.allow=always', '-c', 'core.longpaths=true')
$update     = @('submodule', 'update', '--force', '--jobs', '8')

# The repository of the submodule checked out at $dir, from its .git file (or folder); null when it has none.
function Get-SubmoduleGitDir([string] $dir) {
    $dotGit = Join-Path $dir '.git'
    if (Test-Path -LiteralPath $dotGit -PathType Container) { return $dotGit }
    if (-not (Test-Path -LiteralPath $dotGit -PathType Leaf)) { return $null }
    $target = ((Get-Content -LiteralPath $dotGit -TotalCount 1) -replace '^gitdir:\s*', '').Trim()
    if ([System.IO.Path]::IsPathRooted($target)) { $target } else { Join-Path $dir $target }
}

<#
Whether the submodule at $dir finished a checkout. Its status alone does not say: one cloned from the main
checkout's copy starts out with its HEAD already at the pin, so one stopped between its clone and its checkout
reads " <sha>" with no files. Its index is written last, when the checkout completes.
#>
function Test-SubmoduleCheckedOut([string] $dir) {
    $repository = Get-SubmoduleGitDir $dir
    $repository -and (Test-Path -LiteralPath (Join-Path $repository 'index'))
}

# The submodules of $repo whose checkout never completed - not cloned, or cloned without an index. One with an
# index is somebody's, whatever its state ('+' work on another commit, '-' switched off), and is left alone.
function Get-UnpopulatedSubmodules([string] $repo) {
    foreach ($line in @(& git -C $repo submodule status 2>$null)) {
        if ($line -notmatch '^[-+ ]\S+\s+(?<path>\S+)') { continue }
        $path = $Matches['path']
        if (-not (Test-SubmoduleCheckedOut (Join-Path $repo $path))) { $path }
    }
}

# Those of $paths not checked out at the commit $repo pins - " <sha> <path>" with an index is the only one that is.
function Get-SubmodulesOffPin([string] $repo, [string[]] $paths) {
    foreach ($line in @(& git -C $repo submodule status -- @paths 2>$null)) {
        if ($line -notmatch '^(?<state>.)\S+\s+(?<path>\S+)') { continue }
        $path = $Matches['path']
        if ($Matches['state'] -ne ' ' -or -not (Test-SubmoduleCheckedOut (Join-Path $repo $path))) { $path }
    }
}

<#
Check out $paths, submodules of $repo, at their pins. $stores is where the main checkout keeps the repositories
of $repo's submodules (null for the main checkout itself); $shallow asks the network for the pinned commit alone.
Returns the paths still not checked out at their pin.
#>
function Update-Submodules([string] $repo, [string[]] $paths, [string] $stores, [bool] $shallow) {
    if ($paths.Count -eq 0) { return }

    # What a stopped run leaves, put right. Every one of $paths has had no checkout, so none holds any work.
    foreach ($path in $paths) {
        $dir        = Join-Path $repo $path
        $repository = Get-SubmoduleGitDir $dir
        if (-not $repository) { continue }
        & git -C $dir rev-parse --verify --quiet HEAD *> $null
        if ($LASTEXITCODE -ne 0) {
            # Stopped mid-clone: its HEAD is still git's placeholder, so there is no commit to check out. It goes,
            # .git with it, and the submodule is cloned from the start.
            Write-Host "ensure-submodules: discarding the unfinished clone of '$path'"
            Remove-Item -LiteralPath $repository -Recurse -Force
            Remove-Item -LiteralPath (Join-Path $dir '.git') -Force -ErrorAction SilentlyContinue
        } else {
            # Stopped mid-checkout: its index lock is left behind, and git will not check out under it. It is
            # stale - this run holds the checkout's lock.
            Remove-Item -LiteralPath (Join-Path $repository 'index.lock') -Force -ErrorAction SilentlyContinue
        }
    }

    # Registered first, from .gitmodules, so the URL each one keeps is the real one whatever it is cloned from.
    & git -C $repo submodule init -- @paths | Out-Host

    $local = @(if ($stores) { $paths | Where-Object { Test-Path -LiteralPath (Join-Path $stores "$_/objects") } })
    if ($local.Count -gt 0) {
        # The URL is overridden for one command, so the clone reads the main checkout's copy; git keys it by the
        # submodule's name, which .gitmodules maps from its path.
        $names = @{}
        foreach ($line in @(& git -C $repo config --file .gitmodules --get-regexp '^submodule\..+\.path$')) {
            if ($line -match '^submodule\.(?<name>.+)\.path\s+(?<path>\S+)$') { $names[$Matches['path']] = $Matches['name'] }
        }
        # A copy with its whole history is cloned by path, which hard-links its objects. A shallow one cannot be
        # hard-linked - git falls back to packing everything it holds - so it is fetched over file:// at depth 1,
        # which packs the pinned tree alone.
        $full    = @($local | Where-Object { -not (Test-Path -LiteralPath (Join-Path $stores "$_/shallow")) })
        $partial = @($local | Where-Object { $full -notcontains $_ })
        foreach ($set in @(@{ Paths = $full; Scheme = ''; Depth = @() }, @{ Paths = $partial; Scheme = 'file:///'; Depth = @('--depth', '1') })) {
            if ($set.Paths.Count -eq 0) { continue }
            $urls = foreach ($path in $set.Paths) { '-c'; "submodule.$($names[$path]).url=$($set.Scheme)$((Join-Path $stores $path) -replace '\\', '/')" }
            Write-Host "ensure-submodules: cloning from the main checkout: $($set.Paths -join ', ')"
            & git -C $repo @gitOptions @urls @update @($set.Depth) -- @($set.Paths) | Out-Host
        }
        # Each clone's origin is the main checkout's copy until this points it where .gitmodules says.
        & git -C $repo submodule sync --quiet -- @local | Out-Host
    }

    # Whatever is still not at its pin: no copy in the main checkout, or a pin newer than the one it holds.
    $remote = @(Get-SubmodulesOffPin $repo $paths)
    if ($remote.Count -gt 0) {
        $depth = if ($shallow) { @('--depth', '1') } else { @() }
        Write-Host "ensure-submodules: fetching: $($remote -join ', ')"
        & git -C $repo @gitOptions @update @depth -- @remote | Out-Host
    }

    Get-SubmodulesOffPin $repo $paths
}

# Keyed by the git dir, so two worktrees populate side by side and one checkout never twice at once.
$sha  = [System.Security.Cryptography.SHA256]::Create()
$key  = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($gitDir.ToLowerInvariant()))).Replace('-', '')
$lock = [System.Threading.Mutex]::new($false, "Global\Nexaflow.EnsureSubmodules.$($key.Substring(0, 32))")
try { [void]$lock.WaitOne() }
catch [System.Threading.AbandonedMutexException] { }   # its holder was killed mid-run; the lock is ours now

try {
    # The main checkout keeps its submodules' repositories under <common>/modules/<path>, a linked worktree its
    # own under <common>/worktrees/<name>/modules - so only a linked worktree has another copy to clone from.
    $stores = if ($gitDir -ne $commonDir) { Join-Path $commonDir 'modules' } else { $null }

    $paths = @(Get-UnpopulatedSubmodules $root)
    if ($paths.Count -gt 0) { Remove-Item -LiteralPath $stamp -Force -ErrorAction SilentlyContinue }
    $failed = @(Update-Submodules $root $paths $stores $false)

    # tree-sitter-dotnet-bindings carries the grammars as NESTED submodules - 29 of them, plus the runtime.
    # Nexaflow compiles only the ones it registers, so init exactly those rather than --recursive: the rest are
    # tens of megabytes of generated parser tables for languages nothing here opens. Shallow for the same reason
    # (the pin is a commit on a branch we track, so depth 1 still resolves it).
    $bindingsPath = 'external/tree-sitter-dotnet-bindings'
    $bindings     = Join-Path $root $bindingsPath
    $manifest     = Join-Path $root 'tools/tree-sitter-grammars.props'
    if ((Test-SubmoduleCheckedOut $bindings) -and (Test-Path $manifest)) {
        $needed = @('tree-sitter-native/tree-sitter')   # the runtime
        foreach ($g in ([xml](Get-Content -LiteralPath $manifest -Raw)).Project.ItemGroup.TreeSitterGrammar) {
            # A grammar with its own Root lives in a different submodule (xml), not inside the bindings repo.
            if (-not $g.Root) { $needed += "tree-sitter-native/tree-sitter-$($g.Include)" }
        }

        $grammars = @(Get-UnpopulatedSubmodules $bindings | Where-Object { $needed -contains $_ })
        if ($grammars.Count -gt 0) { Remove-Item -LiteralPath $stamp -Force -ErrorAction SilentlyContinue }
        $nested   = if ($stores) { Join-Path $stores "$bindingsPath/modules" } else { $null }
        $failed  += @(Update-Submodules $bindings $grammars $nested $true | ForEach-Object { "$bindingsPath/$_" })
    }

    if ($failed.Count -eq 0 -and -not (Test-Path -LiteralPath $stamp)) {
        [System.IO.File]::WriteAllText($stamp, "Written by tools/ensure-submodules.ps1 when every submodule the build needs is checked out.`n")
    }
}
finally {
    $lock.ReleaseMutex()
    $lock.Dispose()
}

if ($failed.Count -gt 0) {
    [Console]::Error.WriteLine("ensure-submodules: $($failed.Count) submodule(s) are not checked out at their pin - git's reason is above:`n  " + ($failed -join "`n  "))
    exit 1
}
