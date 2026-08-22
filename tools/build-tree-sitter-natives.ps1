<#
.SYNOPSIS
    Compile the tree-sitter runtime and the grammars Nexaflow registers into native DLLs.

.DESCRIPTION
    Nexaflow builds its tree-sitter natives from pinned submodule sources rather than taking the
    prebuilt ones from the TreeSitter.DotNet package, because the package's grammars are frozen at
    whatever its own submodules pointed at when it was published. Its C# grammar predates collection
    expressions and slice patterns, so `= []` and `[.. var rest]` were parse errors - and a single
    slice pattern cost Program.cs its ENTIRE parse (root ERROR, no type nodes, invisible to the graph).
    Building from source puts the version under our control for every language at once.

    Two kinds of artefact, same toolchain:
      * the runtime  - tree-sitter/lib/src/lib.c (the amalgamation), exports ts_* via tree-sitter.def
      * each grammar - <grammar>/<SourceDir>/parser.c (+ scanner.c when present), exports
                       tree_sitter_<id>, which is the symbol `new Language("<id>")` binds to

    The set of grammars comes from tools/tree-sitter-grammars.props - the single source of truth
    shared with Nexaflow.Syntax.csproj and tools/ensure-submodules.ps1.

    Idempotent: each artefact is recompiled only when one of its sources is newer, so a warm build
    costs nothing. Object files go to per-artefact folders under -IntermediateDir (never into a
    submodule, so the superproject never sees one as dirty) - per-artefact because every grammar
    compiles a file called parser.c and they would otherwise collide on parser.obj.

    Requires the MSVC C toolchain (Visual Studio "Desktop development with C++", or Build Tools).
#>
[CmdletBinding()]
param(
    # Repo (or worktree) root - the folder holding external/ and tools/.
    [Parameter(Mandatory)][string] $RepoRoot,
    # Where the finished DLLs go, e.g. <proj>/obj/native.
    [Parameter(Mandatory)][string] $OutputDir,
    # Where .obj/.lib/.exp land. Defaults to <OutputDir>/int.
    [string] $IntermediateDir
)

$ErrorActionPreference = 'Stop'

function ConvertTo-WinPath([string] $p) {
    return ([System.IO.Path]::GetFullPath($p)) -replace '/', '\'
}

$RepoRoot  = ConvertTo-WinPath $RepoRoot
$OutputDir = ConvertTo-WinPath $OutputDir
if (-not $IntermediateDir) { $IntermediateDir = Join-Path $OutputDir 'int' }
$IntermediateDir = ConvertTo-WinPath $IntermediateDir

$bindings = Join-Path $RepoRoot 'external\tree-sitter-dotnet-bindings'
$manifest = Join-Path $RepoRoot 'tools\tree-sitter-grammars.props'

if (-not (Test-Path (Join-Path $bindings 'src\TreeSitter.csproj'))) {
    throw "The tree-sitter-dotnet-bindings submodule is not initialised at '$bindings'. Run: powershell tools/ensure-submodules.ps1"
}

# --- what to build ------------------------------------------------------------------------------
$xml = [xml](Get-Content -LiteralPath $manifest -Raw)
$targets = @()

# The runtime first: everything else is useless without it, and a failure here is the clearest signal.
$tsRoot = Join-Path $bindings 'tree-sitter-native\tree-sitter'
$targets += [pscustomobject]@{
    Name     = 'tree-sitter'
    Sources  = @((Join-Path $tsRoot 'lib\src\lib.c'))
    Includes = @((Join-Path $tsRoot 'lib\include'), (Join-Path $tsRoot 'lib\src'))
    # Without the .def the DLL exports nothing and every P/Invoke fails at first call.
    Def      = Join-Path $bindings 'tree-sitter-native\tree-sitter.def'
}

foreach ($g in $xml.Project.ItemGroup.TreeSitterGrammar) {
    $id   = $g.Include
    $root = if ($g.Root) { Join-Path $RepoRoot ($g.Root -replace '/', '\') }
            else         { Join-Path $bindings ("tree-sitter-native\tree-sitter-$id") }
    $src  = Join-Path $root ($g.SourceDir -replace '/', '\')

    if (-not (Test-Path (Join-Path $src 'parser.c'))) {
        throw "Grammar '$id': no parser.c under '$src'. The submodule is not initialised - run: powershell tools/ensure-submodules.ps1"
    }

    # scanner.c is optional (json, java and embedded-template have none).
    $sources = @((Join-Path $src 'parser.c'))
    $scanner = Join-Path $src 'scanner.c'
    if (Test-Path $scanner) { $sources += $scanner }

    $targets += [pscustomobject]@{
        Name     = "tree-sitter-$id"
        Sources  = $sources
        Includes = @($src)
        Def      = $null
    }
}

# --- anything to do? ----------------------------------------------------------------------------
$stale = @($targets | Where-Object {
    $dll = Join-Path $OutputDir "$($_.Name).dll"
    if (-not (Test-Path $dll)) { return $true }
    $dllTime = (Get-Item $dll).LastWriteTimeUtc
    $inputs  = @($_.Sources) + @($_.Def) | Where-Object { $_ -and (Test-Path $_) }
    $newest  = ($inputs | ForEach-Object { (Get-Item $_).LastWriteTimeUtc } | Measure-Object -Maximum).Maximum
    return $dllTime -lt $newest
})

if ($stale.Count -eq 0) {
    Write-Host "tree-sitter natives: up to date ($($targets.Count) artefacts)"
    return
}

# --- locate the MSVC toolchain ------------------------------------------------------------------
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "Cannot find vswhere.exe. Nexaflow needs the MSVC C toolchain to build the tree-sitter natives. Install Visual Studio with the 'Desktop development with C++' workload (or Build Tools for Visual Studio)."
}
$vsRoot = & $vswhere -latest -products '*' `
                     -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
                     -property installationPath
if (-not $vsRoot) {
    throw "No Visual Studio installation with the C++ toolchain was found. Install the 'Desktop development with C++' workload (component Microsoft.VisualStudio.Component.VC.Tools.x86.x64), then rebuild."
}
$vcvars = Join-Path $vsRoot 'VC/Auxiliary/Build/vcvars64.bat'
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found under '$vsRoot'." }

New-Item -ItemType Directory -Force -Path $OutputDir, $IntermediateDir | Out-Null

# --- compile ------------------------------------------------------------------------------------
# One cmd session for the whole batch: vcvars64 takes seconds and would otherwise be paid per artefact.
#
# /O1 not /O2 on purpose. These are generated table-driven parsers, some enormous - the C# grammar's
# parser.c alone is 31 MB - and /O2 spends a very long time on them for no measurable parse-time win.
$lines = @(
    '@echo off'
    # vcvars64 prints a benign 'vswhere not recognized' warning and can leave a nonzero errorlevel,
    # so never gate on it - each cl's own exit code is the real signal.
    "call `"$vcvars`" >nul 2>&1"
)

foreach ($t in $stale) {
    $objDir = Join-Path $IntermediateDir $t.Name
    New-Item -ItemType Directory -Force -Path $objDir | Out-Null

    $incs = ($t.Includes | ForEach-Object { "/I `"$(ConvertTo-WinPath $_)`"" }) -join ' '
    $srcs = ($t.Sources  | ForEach-Object { "`"$(ConvertTo-WinPath $_)`"" }) -join ' '
    $dll  = ConvertTo-WinPath (Join-Path $OutputDir "$($t.Name).dll")
    $imp  = ConvertTo-WinPath (Join-Path $objDir "$($t.Name).lib")
    # /Fo means "directory" only with a trailing backslash, and cmd reads \" as an escaped quote -
    # hence the doubled backslash inside the quotes.
    $fo   = (ConvertTo-WinPath $objDir).TrimEnd('\') + '\'
    $def  = if ($t.Def) { " /DEF:`"$(ConvertTo-WinPath $t.Def)`"" } else { '' }

    $lines += "echo   $($t.Name)"
    $lines += "cl /nologo /O1 /W3 /LD /DNDEBUG $incs /Fo`"$fo\`" /Fe`"$dll`" $srcs /link /IMPLIB:`"$imp`"$def"
    $lines += "if errorlevel 1 exit /b 1"
}
$lines += 'exit /b 0'

$bat = Join-Path $IntermediateDir 'build-tree-sitter-natives.cmd'
$lines | Set-Content -LiteralPath $bat -Encoding ASCII

Write-Host "tree-sitter natives: compiling $($stale.Count) of $($targets.Count) artefact(s) -> $OutputDir"
$out = & cmd.exe /c "`"$bat`"" 2>&1
if ($LASTEXITCODE -ne 0) {
    $out | ForEach-Object { Write-Host $_ }
    throw "Failed to compile the tree-sitter natives (cl exit $LASTEXITCODE)."
}
$out | Where-Object { $_ -match '^\s{2}\S' } | ForEach-Object { Write-Host $_ }

foreach ($t in $stale) {
    $dll = Join-Path $OutputDir "$($t.Name).dll"
    if (-not (Test-Path $dll)) { throw "The compile reported success but '$dll' is missing." }
}
Write-Host "tree-sitter natives: done"
