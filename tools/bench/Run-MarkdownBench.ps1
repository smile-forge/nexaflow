<#
.SYNOPSIS
  Times laying markdown out, step by step, over the sample corpus and docs/MarkdownSupport.md - and rich text (RTF), over
  its samples and one long generated document, in totals of its own. Run it by hand.

.DESCRIPTION
  Builds the Visuals test suite, runs MarkdownLayoutBench, and prints the JSON file the run wrote: per document what
  opening it and typing one character into it cost, and each step on its own - reading it, every stage the document is
  read by, the builder (nested languages and markdown's own), and painting - with corpus totals for every nested
  language, every stage of each language's pipeline, and every kind of block. Then what reading a long document costs once it
  is open - scrolling it a screen and a wheel turn at a time, jumping about it, pointing, dragging and picking everything out -
  on a surface in a window, over a generated lecture script full of formulas and docs/MarkdownSupport.md.

  And what writing costs as a reader pays for it, in a file of its own (markdown-writing-bench-*.json): keys go in as the
  window's do, to a surface in a window, each timed until the window is idle - a keystroke at the top, middle and end of
  generated prose and code at 50 KB, 1 MB and 50 MB, and in and after a 1 MB and a 5 MB code fence; taking back one step
  and fifty; pasting a megabyte; carrying a word down the screen; and what fifty steps of history hold. A size too slow to
  open, a step too slow to repeat and a history too large to hold are recorded as skipped with why, and a .log beside the
  run names each step as it starts.

  A timing is only worth comparing with another taken on the same machine in the same configuration. Release is the
  default because Debug checks every pipeline stage's output against its input, which is not what a user pays for.

.PARAMETER Label
  What this run is - "baseline", "typefaces on the style". Recorded in the run beside the commit.

.PARAMETER Out
  The folder runs are written to. Every run is a new file, so earlier runs stay to compare with.

.PARAMETER Configuration
  Release (default) or Debug.

.PARAMETER Part
  All (default); Layout - laying out and reading the corpus; or Writing - what writing in a document costs.

.PARAMETER Only
  Writing only: part of a document's name - "fence", "code-1024" - to measure only the documents whose names hold it.
#>
param(
    [string]$Label = "",
    [string]$Out = (Join-Path $env:LOCALAPPDATA 'Nexaflow\markdown-bench'),
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [ValidateSet('All', 'Layout', 'Writing')][string]$Part = 'All',
    [string]$Only = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $root 'src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Nexaflow.Tests.Visuals.csproj'

dotnet build $project -c $Configuration -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "the $Configuration build failed" }

$commit = (git -C $root rev-parse --short HEAD).Trim()
if (git -C $root status --porcelain) { $commit += '+changes' }

New-Item -ItemType Directory -Force $Out | Out-Null
$env:NEXAFLOW_MARKDOWN_BENCH = $Out
$env:NEXAFLOW_BENCH_LABEL = $Label
$env:NEXAFLOW_BENCH_COMMIT = $commit
$env:NEXAFLOW_WRITING_BENCH_ONLY = $Only

$exe = Join-Path $root "src/Nexaflow.Tests/Nexaflow.Tests.Visuals/bin/x64/$Configuration/net10.0-windows/Nexaflow.Tests.Visuals.exe"
$filter = switch ($Part) {
    'Layout'  { 'FullyQualifiedName~MarkdownLayoutBench.WhereLayingADocumentSpendsItsTime' }
    'Writing' { 'FullyQualifiedName~MarkdownLayoutBench.WhatWritingInADocumentCosts' }
    default   { 'FullyQualifiedName~MarkdownLayoutBench' }
}

$started = Get-Date
& $exe --filter $filter | Out-Null

function Latest($pattern) {
    Get-ChildItem $Out -Filter $pattern | Where-Object LastWriteTime -ge $started | Sort-Object LastWriteTime | Select-Object -Last 1
}

if ($Part -ne 'Writing') {
$written = Latest 'markdown-bench-*.json'
if (-not $written) { throw "the run wrote nothing to $Out" }

$run = Get-Content $written.FullName -Raw | ConvertFrom-Json
"{0}  open {1:N0} ms  opened as a page {2:N0} ms  edit {3:N0} ms  paint {4:N0} ms  ({5} documents, {6} {7})" -f `
    $written.FullName, $run.totals.open, $run.totals.opened, $run.totals.edit, $run.totals.paint, $run.documents.Count, $run.configuration, $run.commit
foreach ($read in $run.reading) {
    "reading {0} ({1:N0} KB): page down p90 {2:N0} ms  jump {3:N0} ms  wheel p90 {4:N1} ms  hover {5:N1} ms  drag {6:N1} ms  select all {7:N0} ms" -f `
        $read.name, $read.kb, $read.pageDown.p90, $read.jump.median, $read.wheel.'90%'.p90, $read.hover.median, $read.drag.median, $read.selectAll
}
if ($run.rtfTotals) {
    "rich text: open {0:N0} ms  opened as a page {1:N0} ms  edit {2:N0} ms  paint {3:N0} ms  a screen {4:N0} ms  held open {5:N0} KB" -f `
        $run.rtfTotals.open, $run.rtfTotals.opened, $run.rtfTotals.edit, $run.rtfTotals.paint, $run.rtfTotals.paintShown, $run.rtfTotals.retainedOpenedKb
}
}

if ($Part -ne 'Layout') {
    $writing = Latest 'markdown-writing-bench-*.json'
    if (-not $writing) { throw "the writing run wrote nothing to $Out" }

    $run = Get-Content $writing.FullName -Raw | ConvertFrom-Json
    "{0}  ({1} {2})" -f $writing.FullName, $run.configuration, $run.commit
    foreach ($doc in $run.documents) {
        $keys = ($doc.keystroke.PSObject.Properties | ForEach-Object {
            if ($_.Value -is [string]) { "{0} skipped" -f $_.Name } else { "{0} p90 {1:N1} ms (reached in {2:N0} ms)" -f $_.Name, $_.Value.p90, $_.Value.reachMs }
        }) -join '  '
        $deep = if ($doc.undoSteps -is [string]) { 'skipped' } else { "{0:N1} ms" -f $doc.undoSteps.p90 }
        $drag = if ($doc.drag -is [string]) { $doc.drag } else { "{0:N1} ms" -f $doc.drag.p90 }
        "writing {0} ({1:N0} KB): open {2:N0} ms  {3}  undo p90 {4:N1} ms  fifty back p90 {5}  paste p90 {6:N0} ms  carry p90 {7}" -f `
            $doc.name, $doc.kb, $doc.opened, $keys, $doc.undoLatest.p90, $deep, $doc.paste.p90, $drag
    }
    foreach ($skip in $run.skipped) { "skipped {0}: {1}" -f $skip.name, $skip.why }
}
