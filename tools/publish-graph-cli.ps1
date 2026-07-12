#!/usr/bin/env pwsh
# Publishes the product knowledge-graph CLI into tools/graph-cli/ so tools/graph.ps1 has a fast,
# current exe. The checked-in main-branch bin can lag (the CLI is built by the setup solution, not a
# plain `dotnet build`), so refresh this after the CLI changes or when graph queries error on an old
# binary. tools/graph-cli/ is gitignored (a build artifact).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\Nexaflow.Services.Initiatives.Cli'
$out  = Join-Path $PSScriptRoot 'graph-cli'
& dotnet publish $proj -c Release -o $out --nologo -v minimal
if ($LASTEXITCODE -ne 0) { Write-Error 'publish failed'; exit 1 }
Write-Host "published graph CLI -> $out" -ForegroundColor Green
