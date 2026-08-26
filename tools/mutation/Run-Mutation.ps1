<#
.SYNOPSIS
  Mutation-tests one Nexaflow leaf library with Stryker.NET. Run it by hand, occasionally.

.DESCRIPTION
  Mutation testing asks the question a green suite cannot: if this line were WRONG, would any test notice?
  Stryker rewrites one operator, literal or branch at a time, reruns the tests that cover it, and records
  whether they went red. A mutant nothing kills is a line the suite is watching but not guarding.

  This is deliberately NOT wired into anything. Not `dotnet build`, not the architecture guards, not ci.yml,
  and not the NexaflowSetup.slnx release gate. Those are seconds-scale pass/fail checks; a sweep here is
  minutes, and the answer barely moves between one commit and the next. It is a review tool: run it when you
  are thinking about a subsystem's test quality, read the survivors, decide what to do.

  Targets are the WPF-free leaves whose tests map cleanly onto them (tools/mutation/stryker-*.json).
  Feature ViewModels are not mutated: most of their mutable surface is binding glue, their tests need a
  pumped UI context, and Stryker's project analysis already fails on several net10.0-windows feature
  projects. See docs/testing.md -> Mutation testing.

.PARAMETER Target
  io-common | initiatives | search | all.  Omit to list the targets and what each is for.

.PARAMETER Since
  Only mutate code changed since this committish (e.g. 'origin/main'). Turns a full sweep into a
  branch-sized one, because untouched code is never mutated.

.PARAMETER Break
  Override the config's break-at threshold. The run exits non-zero below it. Nothing consumes that exit
  code today -- it is there for the day you want to.

.PARAMETER Concurrency
  How many mutants to test in parallel. Defaults to half the logical cores, not all of them: Stryker's own
  default is aggressive, and this is a tool you leave running while you work. Raise it on a spare machine.

.PARAMETER Cleanup
  Kill leftover MSBuild/test-host processes and stop, without running anything. See CLEANING UP below.

.NOTES
  CLEANING UP. Stryker rebuilds and re-runs per mutant, and it leaks: a sweep leaves dozens of MSBuild
  node-reuse workers and test hosts behind. That is harmless for a WPF-free target, but the 'search' target
  runs three UseWPF suites, and enough orphaned WPF hosts will exhaust the interactive session's desktop
  heap. The symptom is nasty because it does not look like a resource problem: unrelated WPF tests start
  failing with "Win32Exception: Not enough memory resources" out of HwndWrapper..ctor while the machine has
  tens of GB free, and it does not clear until you sign out. This script therefore runs the cleanup itself
  after every sweep. `-Cleanup` on its own does it without a run, for when a previous one was interrupted.

  Stryker can also finish with "Failed to restore output assembly ... Mutated assembly is still in place"
  if a handle is held -- that leaves a MUTATED dll in the test project's bin. The script checks for it and
  rebuilds if so, because the alternative is a build tree that quietly lies to you.

.EXAMPLE
  ./Run-Mutation.ps1
  Lists the targets.

.EXAMPLE
  ./Run-Mutation.ps1 -Target initiatives
  The one worth running first: SnaplinkValidator gates the installer build.

.EXAMPLE
  ./Run-Mutation.ps1 -Target all -Since origin/main
  Everything this branch touched, across all three targets.
#>
[CmdletBinding()]
param(
    [ValidateSet('io-common', 'initiatives', 'search', 'all')][string]$Target,
    [string]$Since,
    [int]$Break = -1,
    [int]$Concurrency = 0,
    [switch]$Cleanup
)

$ErrorActionPreference = 'Stop'

# Every path in the config files is repo-root-relative and Stryker resolves them against the CWD, so the
# script pins the CWD rather than asking the caller to. Run it from tools/mutation, the repo root, or a
# linked worktree -- it lands in the same place.
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

$Targets = [ordered]@{
    'io-common'   = 'Nexaflow.IO.Common      via Tests.IO           - pure byte/text functions. WPF-free: safe to run locally.'
    'initiatives' = 'Services.Initiatives    via Tests.Initiatives  - the product tree + the snaplink validator the installer gates on. WPF-free.'
    'search'      = 'Nexaflow.Search         via 3 Features suites  - query syntax + AQS. USES WPF SUITES: prefer a machine you are not using.'
}

function Write-Targets {
    Write-Host ''
    Write-Host '  Mutation targets' -ForegroundColor Cyan
    Write-Host '  ----------------'
    foreach ($k in $Targets.Keys) { Write-Host ('  {0,-12} {1}' -f $k, $Targets[$k]) }
    Write-Host ''
    Write-Host '  ./Run-Mutation.ps1 -Target initiatives              full sweep'
    Write-Host '  ./Run-Mutation.ps1 -Target all -Since origin/main   only what this branch changed'
    Write-Host '  ./Run-Mutation.ps1 -Cleanup                        kill leftovers from an interrupted run'
    Write-Host ''
}

# Orphans are identified by start time, not by name alone: a plain `Stop-Process -Name MSBuild` would also
# take out nodes belonging to a Visual Studio the user has open. Anything started before this script began
# is somebody else's.
function Invoke-Cleanup([datetime]$StartedAfter) {
    $orphans = @(Get-Process dotnet, MSBuild, VBCSCompiler, dotnet-stryker -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -gt $StartedAfter })

    # The documented way to retire build servers; it also handles the Razor/VBCS ones politely.
    Push-Location $RepoRoot
    try { dotnet build-server shutdown | Out-Null } catch { } finally { Pop-Location }

    $killed = 0
    foreach ($p in $orphans) {
        try { Stop-Process -Id $p.Id -Force -ErrorAction Stop; $killed++ } catch { }
    }
    if ($killed -gt 0) { Write-Host "cleanup: retired $killed leftover build/test-host process(es)" -ForegroundColor DarkGray }
}

# Stryker copies the mutated assembly into the test project's output and restores it afterwards -- unless a
# handle is still held, in which case it warns and leaves the mutant there. A rebuild is the fix.
function Repair-MutatedOutput([string]$ConfigPath) {
    $cfg = Get-Content $ConfigPath -Raw | ConvertFrom-Json
    foreach ($rel in $cfg.'stryker-config'.'test-projects') {
        $proj = Join-Path $RepoRoot $rel
        if (-not (Test-Path $proj)) { continue }
        Write-Host "verifying $(Split-Path $proj -Leaf) is not left holding a mutated assembly..." -ForegroundColor DarkGray
        dotnet build $proj -v q --nologo | Out-Null
    }
}

if ($Cleanup) {
    Invoke-Cleanup ([datetime]::Today)
    Write-Host 'done. If WPF tests are still failing with "Not enough memory resources", sign out and back in.' -ForegroundColor Yellow
    exit 0
}

if (-not $Target) { Write-Targets; exit 0 }

$run = if ($Target -eq 'all') { @('io-common', 'initiatives', 'search') } else { @($Target) }
$startedAt = Get-Date

Push-Location $RepoRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed (see .config/dotnet-tools.json)' }

    $results = @()
    foreach ($t in $run) {
        $config = "tools/mutation/stryker-$t.json"
        $outDir = "artifacts/mutation/$t"

        if ($t -eq 'search') {
            Write-Host ''
            Write-Host 'NOTE: this target runs three UseWPF suites. Thousands of short-lived WPF hosts can exhaust' -ForegroundColor Yellow
            Write-Host '      the desktop heap of the session you are sitting in. Cleanup runs afterwards, but if' -ForegroundColor Yellow
            Write-Host '      unrelated WPF tests then fail with "Not enough memory resources", sign out.' -ForegroundColor Yellow
        }

        $cores = if ($Concurrency -gt 0) { $Concurrency }
                 else { [Math]::Max(2, [int]([Environment]::ProcessorCount / 2)) }

        $strykerArgs = @('-f', $config, '-O', $outDir, '--concurrency', "$cores")
        if ($Since) { $strykerArgs += "--since:$Since" }
        if ($Break -ge 0) { $strykerArgs += @('--break-at', "$Break") }

        Write-Host ''
        Write-Host "=== $t ===" -ForegroundColor Cyan
        Write-Host "dotnet stryker $($strykerArgs -join ' ')" -ForegroundColor DarkGray

        dotnet stryker @strykerArgs
        $code = $LASTEXITCODE

        # The json report is the durable record; the html one is what you actually read.
        $score = $null
        $json = Join-Path $RepoRoot "$outDir/reports/mutation-report.json"
        if (Test-Path $json) {
            $report = Get-Content $json -Raw | ConvertFrom-Json
            $all = $report.files.PSObject.Properties.Value.mutants
            $killed = @($all | Where-Object status -eq 'Killed').Count
            $survived = @($all | Where-Object status -eq 'Survived').Count
            $nocov = @($all | Where-Object status -eq 'NoCoverage').Count
            $denom = $killed + $survived + $nocov
            $score = if ($denom -gt 0) { [math]::Round(100 * $killed / $denom, 1) } else { 0 }
            $results += [pscustomobject]@{
                Target = $t; Score = "$score%"; Killed = $killed; Survived = $survived
                Uncovered = $nocov; Exit = $code
                Report = (Join-Path $RepoRoot "$outDir/reports/mutation-report.html")
            }
        }
        else {
            $results += [pscustomobject]@{
                Target = $t; Score = 'n/a'; Killed = 0; Survived = 0; Uncovered = 0; Exit = $code; Report = '(no report)'
            }
        }

        Repair-MutatedOutput (Join-Path $RepoRoot $config)
    }

    Write-Host ''
    $results | Format-Table Target, Score, Killed, Survived, Uncovered, Exit -AutoSize
    Write-Host 'Survived = a test ran and did not notice. Uncovered = no test executes that line at all.'
    Write-Host 'They are different problems: the first wants a sharper assertion, the second wants a test.'
    Write-Host ''
    foreach ($r in $results) { Write-Host "  $($r.Target): $($r.Report)" -ForegroundColor Green }

    exit ($results | Measure-Object -Property Exit -Maximum).Maximum
}
finally {
    Pop-Location
    Invoke-Cleanup $startedAt
}
