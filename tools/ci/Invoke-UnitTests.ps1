<#
.SYNOPSIS
  Runs the unit-test suites CI gates on, names every test that fails, and gives each failure one re-run so a
  flaky test is reported as a warning instead of failing a pull request it has nothing to do with.

.DESCRIPTION
  Each suite's test exe (Microsoft.Testing.Platform, the MSTest runner) writes a TRX report into
  -ResultsDirectory. The report, not the console, is what is read: every failed result is looked up by its
  fully qualified name (class + method), and the failed methods alone are run a second time, with --filter on
  those names, in the same job.

    failed, then passed   a flake. `::warning title=Flaky test::<name>`, the build stays green, and the test
                          goes into flakes.json beside the reports so it can be found and fixed later.
    failed both times     a failure. `::error title=<name>::<its message>`, and the run fails.
    no test to name       the exe exited non-zero but its report names no failed test (a crash, an abort, a
                          filter that matched nothing). The suite fails on its exit code, with nothing re-run.

  A test that fails is not annotated until its re-run has answered, so a flake never shows a red annotation on
  a green build. Retrying is how a flake is kept off unrelated pull requests, not how it is fixed: the fix is
  to make the test deterministic. See docs/testing.md -> Flaky tests in CI.

  Run locally it does the same, against your own build, with the workflow commands printed as plain lines.

.PARAMETER Suites
  Ordered map of test project name -> target framework, e.g. @{ 'Nexaflow.Tests.IO' = 'net10.0-windows' }.
  The exe is src/Nexaflow.Tests/<project>/bin/x64/<Configuration>/<tfm>/<project>.exe.

.PARAMETER ResultsDirectory
  Where the TRX reports and flakes.json are written; CI uploads this folder as the test-results artifact.

.PARAMETER Configuration
  Build configuration the exes were built in. CI builds Release.

.PARAMETER Filter
  The --filter every suite runs under. The re-run narrows it to the failed tests, never widens it.

.PARAMETER Commit
  The commit flakes.json records. CI passes the pull request's head, not the merge commit it tests.

.PARAMETER RunId
  The workflow run flakes.json records, so a flake leads back to its log.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [Collections.IDictionary] $Suites,
  [Parameter(Mandatory)] [string] $ResultsDirectory,
  [string] $Configuration = 'Release',
  [string] $Filter = 'TestCategory!=Desktop&TestCategory!=Interactive',
  [string] $Commit = $(if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { git rev-parse HEAD }),
  [string] $RunId = $env:GITHUB_RUN_ID
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot)
$failedOutcomes = 'Failed', 'Error', 'Timeout', 'Aborted'

# Workflow command values: % and line breaks always, and : and , too inside a property such as title.
function ConvertTo-CommandData([string] $Text) {
  $Text.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
}
function ConvertTo-CommandProperty([string] $Text) {
  (ConvertTo-CommandData $Text).Replace(':', '%3A').Replace(',', '%2C')
}

# MSTest's filter grammar treats these as operators; a name holding one must escape it.
function ConvertTo-FilterValue([string] $Text) {
  [regex]::Replace($Text, '[\\()&|=!~]', '\$0')
}

# A message on one line, for the log and the step summary; annotations and flakes.json keep its lines.
function ConvertTo-OneLine([string] $Text) {
  ($Text -split '\r?\n' | ForEach-Object Trim | Where-Object { $_ }) -join ' / '
}

# One row per result in a TRX report. Name is the fully qualified method; a data row's arguments are in
# DisplayName, and every row of a method shares its Name, which is what a re-run filters on. Message is whole:
# MSTest opens it with a generic line ("Assertion failed.") and the test's own words follow.
function Read-TrxResult([string] $Path) {
  [xml] $trx = Get-Content -LiteralPath $Path -Raw
  $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
  $methods = @{}
  foreach ($test in (Select-Xml -Xml $trx -XPath '/t:TestRun/t:TestDefinitions/t:UnitTest' -Namespace $ns).Node) {
    $methods[$test.id] = "$($test.TestMethod.className).$($test.TestMethod.name)"
  }
  foreach ($result in (Select-Xml -Xml $trx -XPath '/t:TestRun/t:Results/t:UnitTestResult' -Namespace $ns).Node) {
    $message = (Select-Xml -Xml $result -XPath 't:Output/t:ErrorInfo/t:Message' -Namespace $ns).Node.InnerText
    [pscustomobject]@{
      Name        = $methods[$result.testId]
      DisplayName = $result.testName
      Outcome     = $result.outcome
      Message     = if ($message) { $message.Trim() } else { $result.outcome }
    }
  }
}

# Runs one suite's exe into <Report>.trx and returns its exit code and the results its report holds.
function Invoke-Suite([string] $Exe, [string] $SuiteFilter, [string] $Report) {
  $trxPath = Join-Path $ResultsDirectory "$Report.trx"
  Remove-Item -LiteralPath $trxPath -ErrorAction SilentlyContinue
  # A response file, so a long list of re-run names never meets the command-line length limit.
  $rsp = Join-Path $ResultsDirectory "$Report.rsp"
  Set-Content -LiteralPath $rsp -Value '--filter', $SuiteFilter, '--report-trx', '--report-trx-filename', "$Report.trx", '--results-directory', $ResultsDirectory
  & $Exe "@$rsp"
  $exitCode = $LASTEXITCODE
  Remove-Item -LiteralPath $rsp
  [pscustomobject]@{
    ExitCode = $exitCode
    Results  = if (Test-Path -LiteralPath $trxPath) { @(Read-TrxResult $trxPath) } else { @() }
  }
}

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$flakes = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[object]]::new()
$brokenSuites = [Collections.Generic.List[string]]::new()

foreach ($project in $Suites.Keys) {
  $exe = Join-Path $repoRoot "src/Nexaflow.Tests/$project/bin/x64/$Configuration/$($Suites[$project])/$project.exe"
  Write-Host "::group::$project"
  $run = Invoke-Suite $exe $Filter $project
  Write-Host '::endgroup::'

  $failed = @($run.Results | Where-Object { $_.Outcome -in $failedOutcomes })
  if ($run.ExitCode -eq 0) { continue }
  if (-not $failed) {
    $brokenSuites.Add($project)
    Write-Host "::error title=$(ConvertTo-CommandProperty $project)::$(ConvertTo-CommandData "$project exited $($run.ExitCode) with no failed test in its report - see its log above")"
    continue
  }

  $names = @($failed.Name | Sort-Object -Unique)
  foreach ($result in $failed) { Write-Host "$project failed: $($result.DisplayName) [$($result.Name)] - $(ConvertTo-OneLine $result.Message)" }

  Write-Host "::group::$project - re-running $($names.Count) failed test(s)"
  $byName = ($names | ForEach-Object { "FullyQualifiedName=$(ConvertTo-FilterValue $_)" }) -join '|'
  $retry = Invoke-Suite $exe "($Filter)&($byName)" "$project.retry"
  Write-Host '::endgroup::'

  foreach ($name in $names) {
    $first = $failed | Where-Object Name -eq $name | Select-Object -First 1
    $again = @($retry.Results | Where-Object Name -eq $name)
    $passedAgain = $again.Count -gt 0 -and -not ($again | Where-Object { $_.Outcome -in $failedOutcomes })
    if ($passedAgain) {
      $flakes.Add([ordered]@{ test = $name; project = $project; commit = $Commit; runId = $RunId; message = $first.Message })
      Write-Host "::warning title=Flaky test::$(ConvertTo-CommandData "$name failed, then passed on a re-run.`n$($first.Message)")"
    }
    else {
      $failures.Add([ordered]@{ test = $name; project = $project; message = $first.Message })
      $label = if ($first.DisplayName -ne ($name -split '\.')[-1]) { "$($first.DisplayName): " } else { '' }
      Write-Host "::error title=$(ConvertTo-CommandProperty $name)::$(ConvertTo-CommandData "$label$($first.Message)")"
    }
  }
}

ConvertTo-Json -InputObject @($flakes) -Depth 3 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'flakes.json')

if ($env:GITHUB_STEP_SUMMARY -and ($flakes.Count -or $failures.Count -or $brokenSuites.Count)) {
  $summary = [Text.StringBuilder]::new()
  [void]$summary.AppendLine('### Unit tests').AppendLine()
  foreach ($f in $failures) { [void]$summary.AppendLine("- :x: ``$($f.test)`` ($($f.project)) - failed twice: $(ConvertTo-OneLine $f.message)") }
  foreach ($s in $brokenSuites) { [void]$summary.AppendLine("- :x: $s exited non-zero with no failed test in its report") }
  foreach ($f in $flakes) { [void]$summary.AppendLine("- :warning: ``$($f.test)`` ($($f.project)) - flaky, passed on a re-run: $(ConvertTo-OneLine $f.message)") }
  $summary.ToString() | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}

Write-Host "Unit tests: $($failures.Count) failed, $($flakes.Count) flaky, $($brokenSuites.Count) suite(s) without a report of what failed."
if ($failures.Count -or $brokenSuites.Count) { exit 1 }
