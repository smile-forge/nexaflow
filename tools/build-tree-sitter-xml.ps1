<#
.SYNOPSIS
    Compile the tree-sitter XML grammar (external/tree-sitter-xml) into tree-sitter-xml.dll.

.DESCRIPTION
    TreeSitter.DotNet ships ~30 prebuilt native grammars but no xml/xaml one, so Nexaflow builds that
    single grammar from the pinned `external/tree-sitter-xml` submodule. The result is a plain native
    DLL exporting `tree_sitter_xml()`, loaded by id exactly like the packaged grammars
    (see CodeHighlighter.TryCreate -> new Language("xml")).

    Idempotent: recompiles only when a source file is newer than the DLL, so a normal incremental
    build pays nothing. Object files go to -IntermediateDir, never into the submodule, so the
    superproject never sees the submodule as dirty (which is why this submodule, unlike DiscUtils,
    needs no `ignore = untracked` in .gitmodules).

    Requires the MSVC C toolchain (Visual Studio "Desktop development with C++", or Build Tools).
    Invoked from the BuildTreeSitterXml target in Nexaflow.Syntax.csproj; safe to run by hand.
#>
[CmdletBinding()]
param(
    # The submodule root, i.e. <repo>/external/tree-sitter-xml
    [Parameter(Mandatory)][string] $GrammarRoot,
    # Full path of the DLL to produce, e.g. <proj>/obj/native/tree-sitter-xml.dll
    [Parameter(Mandatory)][string] $OutputDll,
    # Where .obj/.lib/.exp land. Defaults to the DLL's own folder.
    [string] $IntermediateDir
)

$ErrorActionPreference = 'Stop'

$src     = Join-Path $GrammarRoot 'xml/src'
$parser  = Join-Path $src 'parser.c'
$scanner = Join-Path $src 'scanner.c'
$common  = Join-Path $GrammarRoot 'common/scanner.h'   # scanner.c includes ../../common/scanner.h

if (-not (Test-Path $parser)) {
    throw "tree-sitter-xml sources not found at '$src'. The submodule is not initialised - run: pwsh tools/ensure-submodules.ps1"
}

if (-not $IntermediateDir) { $IntermediateDir = Split-Path -Parent $OutputDll }
New-Item -ItemType Directory -Force -Path $IntermediateDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputDll) | Out-Null

# --- up to date? -------------------------------------------------------------------------------
$inputs = @($parser, $scanner, $common) | Where-Object { Test-Path $_ }
if (Test-Path $OutputDll) {
    $dllTime = (Get-Item $OutputDll).LastWriteTimeUtc
    $newest  = ($inputs | ForEach-Object { (Get-Item $_).LastWriteTimeUtc } | Measure-Object -Maximum).Maximum
    if ($dllTime -ge $newest) {
        Write-Host "tree-sitter-xml: up to date ($OutputDll)"
        return
    }
}

# --- locate the MSVC toolchain ----------------------------------------------------------------
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "Cannot find vswhere.exe. Nexaflow needs the MSVC C toolchain to build the tree-sitter XML grammar. Install Visual Studio with the 'Desktop development with C++' workload (or Build Tools for Visual Studio)."
}

$vsRoot = & $vswhere -latest -products '*' `
                     -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
                     -property installationPath
if (-not $vsRoot) {
    throw "No Visual Studio installation with the C++ toolchain was found. Install the 'Desktop development with C++' workload (component Microsoft.VisualStudio.Component.VC.Tools.x86.x64), then rebuild."
}

$vcvars = Join-Path $vsRoot 'VC/Auxiliary/Build/vcvars64.bat'
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found under '$vsRoot'." }

# --- compile ------------------------------------------------------------------------------------
# /Fo needs a trailing backslash to mean 'directory', and cmd treats \" as an escaped quote -
# so the batch below emits it doubled. cl is a native tool: hand it Windows-style paths and drive it through vcvars in one cmd session.
# Windows PowerShell 5.1 compatible - Directory.Build.targets invokes these scripts with `powershell`.
function ConvertTo-WinPath([string] $p) {
    return ([System.IO.Path]::GetFullPath($p)) -replace '/', '\'
}
$objDir = (ConvertTo-WinPath $IntermediateDir).TrimEnd('\') + '\'
$outDll = ConvertTo-WinPath $OutputDll
$impLib = ConvertTo-WinPath (Join-Path $IntermediateDir 'tree-sitter-xml.lib')
$srcW   = ConvertTo-WinPath $src

$bat = Join-Path $IntermediateDir 'build-tree-sitter-xml.cmd'
@(
    '@echo off'
    # vcvars64 prints a benign 'vswhere not recognized' warning and can leave a nonzero errorlevel,
    # so never gate on it - cl's own exit code (propagated below) is the real signal.
    "call `"$vcvars`" >nul 2>&1"
    ("cl /nologo /O2 /W3 /LD /DNDEBUG /I `"$srcW`" /Fo`"$objDir\`" /Fe`"$outDll`" " +
     "`"$srcW\parser.c`" `"$srcW\scanner.c`" /link /IMPLIB:`"$impLib`"")
    'exit /b %ERRORLEVEL%'
) | Set-Content -LiteralPath $bat -Encoding ASCII

Write-Host "tree-sitter-xml: compiling grammar -> $outDll"
$out = & cmd.exe /c "`"$bat`"" 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $OutputDll)) {
    $out | ForEach-Object { Write-Host $_ }
    throw "Failed to compile the tree-sitter XML grammar (cl exit $LASTEXITCODE)."
}
Write-Host "tree-sitter-xml: built $((Get-Item $OutputDll).Length) bytes"
