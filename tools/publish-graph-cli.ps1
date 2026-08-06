#!/usr/bin/env pwsh
# Publishes the product knowledge-graph / initiatives CLI into tools/graph-cli/ as a fast, current exe.
# nfi.exe self-locates the .product tree (it follows a git worktree to its main checkout),
# so you invoke it directly from any checkout or worktree — no root arg, no wrapper. The checked-in
# main-branch bin can lag (the CLI is built by the setup solution, not a plain `dotnet build`), so refresh
# this after the CLI changes or when graph queries error on an old binary. tools/graph-cli/ is gitignored.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\Nexaflow.Services.Initiatives.Cli'
$out  = Join-Path $PSScriptRoot 'graph-cli'
# Clean first: publishing into an existing folder does an incremental copy that can leave a stale package
# DLL behind across a version bump (a newer-timestamped old file defeats the copy), so deps.json advances
# but the DLL doesn't — e.g. System.Reflection.MetadataLoadContext stuck at 10.0.0.0 vs a manifest pinning
# 10.0.0.10, which breaks `scan-tests`. A clean output guarantees the DLLs match the manifest.
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
# RID-specific: tree-sitter and libgit2 ship natives for EVERY runtime identifier, so a RID-less publish
# drops ~618 MB of linux/osx/arm payload here for a Windows-only tool. win-x64 prunes it to ~73 MB.
# --self-contained false because the CLI runs on the installed .NET 10 runtime, same as the installer's copy.
& dotnet publish $proj -c Release -r win-x64 --self-contained false -o $out --nologo -v minimal
if ($LASTEXITCODE -ne 0) { Write-Error 'publish failed'; exit 1 }
Write-Host "published graph CLI -> $out" -ForegroundColor Green
