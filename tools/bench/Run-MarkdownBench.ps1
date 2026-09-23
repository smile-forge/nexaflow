<#
.SYNOPSIS
  Times laying markdown out, step by step, over the sample corpus and docs/MarkdownSupport.md. Run it by hand.

.DESCRIPTION
  Builds the Visuals test suite, runs MarkdownLayoutBench, and prints the JSON file the run wrote: per document what
  opening it and typing one character into it cost, and each step on its own - Markdig, every stage the document is
  read by, the builder (nested languages and markdown's own), and painting - with corpus totals for every nested
  language, every stage of each language's pipeline, and every kind of block.

  A timing is only worth comparing with another taken on the same machine in the same configuration. Release is the
  default because Debug checks every pipeline stage's output against its input, which is not what a user pays for.

.PARAMETER Label
  What this run is - "baseline", "typefaces on the style". Recorded in the run beside the commit.

.PARAMETER Out
  The folder runs are written to. Every run is a new file, so earlier runs stay to compare with.

.PARAMETER Configuration
  Release (default) or Debug.
#>
param(
    [string]$Label = "",
    [string]$Out = (Join-Path $env:LOCALAPPDATA 'Nexaflow\markdown-bench'),
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
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

$exe = Join-Path $root "src/Nexaflow.Tests/Nexaflow.Tests.Visuals/bin/x64/$Configuration/net10.0-windows/Nexaflow.Tests.Visuals.exe"
& $exe --filter "FullyQualifiedName~MarkdownLayoutBench" | Out-Null

$written = Get-ChildItem $Out -Filter 'markdown-bench-*.json' | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $written) { throw "the run wrote nothing to $Out" }

$run = Get-Content $written.FullName -Raw | ConvertFrom-Json
"{0}  open {1:N0} ms  edit {2:N0} ms  paint {3:N0} ms  ({4} documents, {5} {6})" -f `
    $written.FullName, $run.totals.open, $run.totals.edit, $run.totals.paint, $run.documents.Count, $run.configuration, $run.commit
