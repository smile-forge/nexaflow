#!/usr/bin/env pwsh
# tools/graph.ps1 - friction-free front-end to the Nexaflow product knowledge-graph CLI.
#
# Works the same from the main checkout OR any git worktree. You never pass a root: it finds the
# tree that holds .product/ (the main checkout when you're in a worktree), locates the CLI exe (or
# falls back to `dotnet run`), and - key for worktrees - re-serves any dumped source (`code`/`cat`/
# `context`) from *your* working tree, so the content matches the branch you're actually on.
#
#   tools/graph.ps1 search cynefin
#   tools/graph.ps1 context product:cynefin
#   tools/graph.ps1 node code:src/.../WpfCynefinRenderer.cs#T:WpfCynefinRenderer
#   tools/graph.ps1 grep palette --from product:mermaid --hops 2 --mode content
#   tools/graph.ps1 build          # regenerate .product/graph.json after code changes
#   tools/graph.ps1 help           # full command list
#
# The graph's *structure* (nodes, edges, line numbers) is built from the main checkout. Emitted paths
# are repo-relative, so they resolve against YOUR tree when you Read them. Any dumped source
# (`code`/`cat`/`context`) whose file differs in your worktree is flagged so you Read the current copy
# rather than trust stale content. Regenerate with `build` after code changes.
#
# NOTE: keep this file ASCII-only. It must parse in Windows PowerShell 5.1 (the default powershell.exe)
# as well as pwsh 7; a no-BOM file with non-ASCII glyphs is mis-decoded by 5.1 and fails to parse.

[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $GraphArgs)

$ErrorActionPreference = 'Stop'
function Fail($m) { Write-Error $m; exit 2 }

# -- caller root: the tree you're working in ----------------------------------
$callerRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $callerRoot) { Fail 'not inside a git repository.' }
$callerRoot = ($callerRoot -replace '/', '\').TrimEnd('\')

# -- graph root: the caller if it holds .product/, else the main checkout ------
if (Test-Path (Join-Path $callerRoot '.product\graph.json')) {
    $graphRoot = $callerRoot
}
else {
    $commonDir = (git rev-parse --path-format=absolute --git-common-dir 2>$null)
    if (-not $commonDir) { Fail 'cannot resolve the main checkout (git-common-dir).' }
    $graphRoot = (Split-Path ($commonDir -replace '/', '\').TrimEnd('\') -Parent)
}
if (-not (Test-Path (Join-Path $graphRoot '.product\graph.json'))) {
    Fail "no .product\graph.json under '$graphRoot' - regenerate it with:  tools/graph.ps1 build"
}

# -- locate the CLI exe under the GRAPH ROOT (the main checkout), not $PSScriptRoot: tools/graph-cli is
# gitignored, so the published exe exists only in the main checkout - a worktree's own tools/ dir is empty
# and would wrongly fall through to `dotnet run`. Fall back to `dotnet run` only if it's genuinely absent.
# Refresh the published copy with tools/publish-graph-cli.ps1 (the setup build also drops it here).
$cliProj = Join-Path $graphRoot 'src\Nexaflow.Services.Initiatives.Cli'
$exe = Join-Path $graphRoot 'tools\graph-cli\nexaflow-initiatives.exe'

if (-not $GraphArgs -or $GraphArgs.Count -eq 0) { $GraphArgs = @('help') }
$full = @('graph') + $GraphArgs + @($graphRoot)     # append the resolved root as the trailing positional

if (Test-Path $exe) { $raw = & $exe @full 2>&1 }
else                { $raw = & dotnet run --project $cliProj -c Debug --verbosity quiet -- @full 2>&1 }
$exit = $LASTEXITCODE

# -- normalize any absolute graph-root prefix to a repo-relative (caller-valid) path --
$lines = @($raw | ForEach-Object { "$_" -replace [regex]::Escape("$graphRoot\"), '' -replace [regex]::Escape("$graphRoot/"), '' })

# -- worktree only: flag any dumped source block whose file differs in your tree --
if ($graphRoot -ne $callerRoot) {
    $rx = '(?<rel>[^\s:]+\.[A-Za-z0-9_]+):\d+-\d+'   # a `path:start-end` source-block header
    $differs = @{}
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        $out.Add($line)
        $m = [regex]::Match($line, $rx)
        if (-not $m.Success) { continue }
        $rel = $m.Groups['rel'].Value
        if (-not $differs.ContainsKey($rel)) {
            $cf = Join-Path $callerRoot ($rel -replace '/', '\')
            $gf = Join-Path $graphRoot  ($rel -replace '/', '\')
            $differs[$rel] = (Test-Path -LiteralPath $cf) -and (Test-Path -LiteralPath $gf) -and
                             ((Get-FileHash -LiteralPath $cf).Hash -ne (Get-FileHash -LiteralPath $gf).Hash)
        }
        if ($differs[$rel]) { $out.Add("      * worktree copy of $rel differs from the graphed (main) source - Read it for current content") }
    }
    $lines = $out
}

$lines | ForEach-Object { Write-Output $_ }
exit $exit
