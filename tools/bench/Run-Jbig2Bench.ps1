<#
.SYNOPSIS
  Times decoding the JBIG2 images in PDFs with Nexaflow.Media's decoder (a managed port of PDFBox's) beside jbig2dec,
  or holds every image's decode to jbig2dec's. Run it by hand.

.DESCRIPTION
  Fetches jbig2dec at a pinned release and builds it as a DLL (Release, x64, MSVC) when the checkout or its jbig2.dll
  is missing, builds the viewer test suite, runs Jbig2DecodeBench, and prints the JSON file the run wrote: per image
  what each decoder took and whether ours and jbig2dec decoded it to the same bits. jbig2dec is AGPL: it is built
  outside the repository and only measured against.

  A timing is only worth comparing with another taken on the same machine in the same configuration. Release is the
  default because Debug is not what a user pays for.

.PARAMETER Jbig2dec
  The jbig2dec checkout to measure against. Cloned at -Tag when it does not exist, and built when its jbig2.dll does
  not. Defaults to NEXAFLOW_JBIG2DEC_SOURCE.

.PARAMETER Tag
  The jbig2dec release a fresh checkout is cloned at — the one the test fixture's Jbig2Dec expects.

.PARAMETER Corpus
  Folders of PDFs. Defaults to NEXAFLOW_PDF_CORPUS, the baseline PDF corpus; PdfPig's own test documents when that is
  unset too.

.PARAMETER Compare
  Instead of timing a sample, decode every image with ours and jbig2dec and compare them (Jbig2ComparisonSweep). Each
  image they disagree on is kept as a file, with its globals, and the disagreements are printed grouped by outcome.

.PARAMETER PerSize
  How many images of each size (under 0.1 MP, 0.1-1, 1-4, 4 MP and over) are timed, spread from the smallest file to
  the largest; the same corpus always gives the same sample. 0 times every image.

.PARAMETER Only
  Measures only the images whose names hold one of these parts, separated by semicolons - "0966320.pdf#12;x.pdf" -
  instead of a sample of each size.

.PARAMETER Label
  What this run is - "baseline", "byte-wide contexts". Recorded in the run beside the commit.

.PARAMETER Out
  The folder runs are written to. Every run is a new file, so earlier runs stay to compare with.

.PARAMETER Configuration
  Release (default) or Debug.
#>
param(
    [string]$Jbig2dec = $env:NEXAFLOW_JBIG2DEC_SOURCE,
    [string]$Tag = '0.20',
    [string[]]$Corpus = @(),
    [switch]$Compare,
    [int]$PerSize = 40,
    [string]$Only = '',
    [string]$Label = '',
    [string]$Out = (Join-Path $env:LOCALAPPDATA 'Nexaflow\jbig2-bench'),
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $root 'src/Nexaflow.Tests/Nexaflow.Tests.Features.Viewers/Nexaflow.Tests.Features.Viewers.csproj'

if (-not $Jbig2dec) { throw 'name the jbig2dec checkout to measure against: -Jbig2dec <folder> or NEXAFLOW_JBIG2DEC_SOURCE' }

if (-not (Test-Path (Join-Path $Jbig2dec 'jbig2.h'))) {
    git clone --depth 1 --branch $Tag https://github.com/ArtifexSoftware/jbig2dec.git $Jbig2dec
    if ($LASTEXITCODE -ne 0) { throw "could not clone jbig2dec $Tag into $Jbig2dec" }
}

$build = Join-Path $Jbig2dec 'nexaflow-dll'
$library = Join-Path $build 'jbig2.dll'
if (-not (Test-Path $library)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs = if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
    if (-not $vs) { throw 'building jbig2dec needs MSVC: install Visual Studio with the C++ workload' }
    $vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'

    # The library alone - no command-line tool, no PNG output - exporting what Jbig2Dec calls.
    New-Item -ItemType Directory -Force $build | Out-Null
    @('LIBRARY jbig2', 'EXPORTS', '  jbig2_ctx_new_imp', '  jbig2_ctx_free', '  jbig2_data_in', '  jbig2_make_global_ctx',
      '  jbig2_global_ctx_free', '  jbig2_complete_page', '  jbig2_page_out', '  jbig2_release_page') | Set-Content (Join-Path $build 'jbig2.def')
    $sources = 'jbig2 jbig2_arith jbig2_arith_iaid jbig2_arith_int jbig2_huffman jbig2_hufftab jbig2_generic jbig2_refinement jbig2_halftone jbig2_image jbig2_segment jbig2_symbol_dict jbig2_text jbig2_mmr jbig2_page' -split ' ' |
        ForEach-Object { '"' + (Join-Path $Jbig2dec "$_.c") + '"' }
    Push-Location $build
    try {
        cmd /c "`"$vcvars`" >nul && cl -nologo -O2 -DNDEBUG -DHAVE_STRING_H=1 -D_CRT_SECURE_NO_WARNINGS -LD $($sources -join ' ') -Fejbig2.dll -link -DEF:jbig2.def"
        if ($LASTEXITCODE -ne 0) { throw 'building jbig2dec failed' }
    }
    finally { Pop-Location }
}

if ($Corpus.Count -eq 0 -and $env:NEXAFLOW_PDF_CORPUS) {
    if (-not (Test-Path $env:NEXAFLOW_PDF_CORPUS)) { throw "the PDF corpus $env:NEXAFLOW_PDF_CORPUS does not exist" }
    $Corpus = @($env:NEXAFLOW_PDF_CORPUS)
}

dotnet build $project -c $Configuration -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "the $Configuration build failed" }

$commit = (git -C $root rev-parse --short HEAD).Trim()
if (git -C $root status --porcelain) { $commit += '+changes' }

New-Item -ItemType Directory -Force $Out | Out-Null
$env:NEXAFLOW_JBIG2_BENCH = $Out
$env:NEXAFLOW_JBIG2DEC = (Resolve-Path $library).Path
$env:NEXAFLOW_JBIG2_CORPUS = $Corpus -join [IO.Path]::PathSeparator
$env:NEXAFLOW_JBIG2_PER_SIZE = $PerSize
$env:NEXAFLOW_JBIG2_ONLY = $Only
$env:NEXAFLOW_BENCH_LABEL = $Label
$env:NEXAFLOW_BENCH_COMMIT = $commit

$exe = Join-Path $root "src/Nexaflow.Tests/Nexaflow.Tests.Features.Viewers/bin/x64/$Configuration/net10.0-windows10.0.19041.0/Nexaflow.Tests.Features.Viewers.exe"
function Invoke-Measurement($test, $kind) {
    $since = Get-Date
    & $exe --filter "FullyQualifiedName~$test" | Out-Null
    $written = Get-ChildItem $Out -Filter "jbig2-$kind-*.json" | Where-Object LastWriteTime -ge $since | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $written) { throw "$test wrote nothing to $Out - its jbig2-$kind-*.log says how far it got" }
    $written.FullName
    Get-Content $written.FullName -Raw | ConvertFrom-Json
}

if ($Compare) {
    "comparing every image with jbig2dec - progress in a jbig2-compare-*.log in $Out"
    $file, $sweep = Invoke-Measurement 'EveryJbig2Image_DecodesAsJbig2decDoes' 'compare'
    $file
    "{0} images from {1} PDFs; jbig2dec {2}, {3} {4}" -f $sweep.corpus.sampled, $sweep.corpus.pdfs, $sweep.jbig2dec.path, $sweep.configuration, $sweep.commit
    foreach ($outcome in $sweep.outcomes.PSObject.Properties) { "  {0,7:N0}  {1}" -f $outcome.Value, $outcome.Name }
    if ($sweep.unread.Count -gt 0) { "  {0,7:N0}  batches could not be read" -f $sweep.unread.Count }
    if ($sweep.disagreements.Count -gt 0) {
        "disagreements:"
        $sweep.disagreements | Group-Object outcome | Sort-Object Count -Descending | ForEach-Object {
            "  {0,6:N0}  {1}   e.g. {2}: {3}" -f $_.Count, $_.Name, $_.Group[0].name, $_.Group[0].detail
        }
    }
    return
}

"timing - each image is logged to a jbig2-bench-*.log in $Out as it is measured"
$file, $run = Invoke-Measurement 'HowManagedJbig2DecodingComparesWithNative' 'bench'

$t = $run.totals
$file
"{0} of {1} images ({2:N1} MP, {3} with globals) from {4} PDFs; {5} decoded identically by both; {6} {7}" -f `
    $run.corpus.sampled, $run.corpus.images, $t.megapixels, $run.corpus.withGlobals, $run.corpus.pdfs, $run.corpus.identical, $run.configuration, $run.commit
# Both on one thread; ours with its globals read afresh each time, as jbig2dec's are.
"decoder       managed {0,9:N0} ms   jbig2dec {1,7:N0} ms   x{2}" -f $t.managedMs, $t.nativeMs, $t.ratio
"the reader    JBIG2Decode {0,5:N0} ms   jbig2dec {1,7:N0} ms   x{2}   ({3} images, globals kept between pages)" -f $t.filterMs, $t.filterNativeMs, $t.filterRatio, $t.filterImages
"allocated     decoder {0:N0} KB/MP   the reader {1:N0} KB/MP   ({2:N0} MB in all, {3} gen2 collections)" -f `
    $t.managedKbPerMp, $t.filterKbPerMp, ($t.managedKb / 1024), $t.managedGen2
$m = $run.memory
"memory        peak working set {0:N0} MB, GC paused {1:N0} ms, collections gen0 {2} gen1 {3} gen2 {4}" -f `
    $m.peakWorkingSetMb, $m.gcPauseMs, $m.gen0Collections, $m.gen1Collections, $m.gen2Collections
foreach ($size in $run.bySize) {
    "  {0,-14} {1,4} images   decoder x{2,-6} the reader x{3}" -f $size.size, $size.images, $size.ratio, $size.filterRatio
}
