#!/usr/bin/env pwsh
# Publishes the product knowledge-graph / initiatives CLI into tools/graph-cli/ as a fast, current exe.
# nfi.exe self-locates the .product tree (it follows a git worktree to its main checkout),
# so you invoke it directly from any checkout or worktree — no root arg, no wrapper. The checked-in
# main-branch bin can lag (the CLI is built by the setup solution, not a plain `dotnet build`), so refresh
# this after the CLI changes or when graph queries error on an old binary. tools/graph-cli/ is gitignored.
#
# Two things this script has to get right, both of which it used to get wrong SILENTLY — the failure that
# matters here is not an error, it is a green line over an unchanged binary:
#
#   1. It must publish the MAIN checkout's copy. tools/graph-cli/ is gitignored, so a linked worktree has
#      its own empty one; run from there and the publish lands in the worktree, reports success, and leaves
#      the exe everyone else uses exactly as stale as it was. Refused below.
#   2. It must not touch the live folder until the new build exists AND the old one has been moved aside
#      WHOLE. It used to delete first, which on a machine running parallel agents did nothing useful: a
#      concurrent nfi.exe holds its own image and DLLs open, so `Remove-Item -Recurse` removed a few files,
#      hit the first locked one, threw, and stopped the script BEFORE the publish — leaving the old exe
#      minus whatever had already gone. The same first-delete meant a failed compile left no exe at all.
$ErrorActionPreference = 'Stop'
$root  = Split-Path $PSScriptRoot -Parent
$proj  = Join-Path $root 'src\Nexaflow.Services.Initiatives.Cli'
$out   = Join-Path $PSScriptRoot 'graph-cli'
$stage = Join-Path $PSScriptRoot 'graph-cli.staging'

# A linked worktree's .git is a file pointing at the main checkout's, so the parent of the common git dir
# IS the main checkout. Nothing to compare against means this is not a git checkout at all — publish anyway
# rather than refuse over a check that could not run.
$common = & git -C $root rev-parse --path-format=absolute --git-common-dir 2>$null
if ($LASTEXITCODE -eq 0 -and $common) {
    $mainRoot = [IO.Path]::GetFullPath((Split-Path $common.Trim() -Parent))
    if ($mainRoot -ne [IO.Path]::GetFullPath($root)) {
        Write-Host ''
        Write-Host "NOT PUBLISHED — tools/graph-cli is gitignored, so this worktree's copy is not the one anyone runs." -ForegroundColor Red
        Write-Host "  publish from the main checkout: pwsh $mainRoot\tools\publish-graph-cli.ps1" -ForegroundColor Yellow
        Write-Host '  to test THIS branch instead, build its own: dotnet build src/Nexaflow.Services.Initiatives.Cli' -ForegroundColor Yellow
        exit 1
    }
}

# A previous run that could not delete the folder it displaced leaves it behind on purpose; by now it is
# only held by a process that has since exited, so clearing it here costs nothing and stops them piling up.
# Best-effort: one still in use is the next run's problem, not this run's failure.
Get-ChildItem $PSScriptRoot -Directory -Filter 'graph-cli.old-*' -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

# Staging is built clean rather than published over: an incremental copy can leave a stale package DLL
# behind across a version bump (a newer-timestamped old file defeats the copy), so deps.json advances but
# the DLL doesn't — e.g. System.Reflection.MetadataLoadContext stuck at 10.0.0.0 against a manifest pinning
# 10.0.0.10, which breaks `scan-tests`. A clean output guarantees the DLLs match the manifest.
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

# RID-specific: tree-sitter and libgit2 ship natives for EVERY runtime identifier, so a RID-less publish
# drops ~618 MB of linux/osx/arm payload here for a Windows-only tool. win-x64 prunes it to ~73 MB.
# --self-contained false because the CLI runs on the installed .NET 10 runtime, same as the installer's copy.
& dotnet publish $proj -c Release -r win-x64 --self-contained false -o $stage --nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    Write-Error "publish failed — $out is untouched and still the previous build."
    exit 1
}

# Rename, not delete, to displace the old folder: a rename is all-or-nothing, so a locked folder leaves the
# working exe exactly as it was rather than half-emptied. This is the step that fails when another nfi.exe
# is running, and it is the one that has to fail loudly.
if (Test-Path $out) {
    $displaced = Join-Path $PSScriptRoot ('graph-cli.old-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    try {
        Rename-Item $out $displaced -ErrorAction Stop
    }
    catch {
        $holders = Get-Process -ErrorAction SilentlyContinue |
                   Where-Object { $_.Path -and $_.Path.StartsWith($out, [StringComparison]::OrdinalIgnoreCase) } |
                   ForEach-Object { "$($_.ProcessName) (pid $($_.Id))" }

        Write-Host ''
        Write-Host "NOT PUBLISHED — $out is in use, so it was left exactly as it was." -ForegroundColor Red
        if ($holders) {
            Write-Host ('  held by: ' + ($holders -join ', ')) -ForegroundColor Red
        } else {
            Write-Host '  no process image sits under it, so something holds one of its DLLs — an agent mid-query, or the app.' -ForegroundColor Red
        }
        Write-Host "  the new build is ready in $stage — re-run once those exit and it swaps straight in." -ForegroundColor Yellow
        exit 1
    }
}

Rename-Item $stage 'graph-cli'

# Prove it runs before calling it published. A swap that lands a broken tree — a missing native, a runtime
# the machine does not have — otherwise reads as success and is found later by whoever trusted it.
& (Join-Path $out 'nfi.exe') graph help *> $null
if ($LASTEXITCODE -ne 0) { Write-Error "the published exe does not run (exit $LASTEXITCODE) — see $out"; exit 1 }

Get-ChildItem $PSScriptRoot -Directory -Filter 'graph-cli.old-*' -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "published graph CLI -> $out" -ForegroundColor Green
