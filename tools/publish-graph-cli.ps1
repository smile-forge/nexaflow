#!/usr/bin/env pwsh
# Publishes the product knowledge-graph / initiatives CLI into tools/graph-cli/ as a fast, current exe.
# nexaflow-initiatives.exe self-locates the .product tree (it follows a git worktree to its main checkout),
# so you invoke it directly from any checkout or worktree — no root arg, no wrapper. The checked-in
# main-branch bin can lag (the CLI is built by the setup solution, not a plain `dotnet build`), so refresh
# this after the CLI changes or when graph queries error on an old binary. tools/graph-cli/ is gitignored.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\Nexaflow.Services.Initiatives.Cli'
$out  = Join-Path $PSScriptRoot 'graph-cli'
& dotnet publish $proj -c Release -o $out --nologo -v minimal
if ($LASTEXITCODE -ne 0) { Write-Error 'publish failed'; exit 1 }
Write-Host "published graph CLI -> $out" -ForegroundColor Green
