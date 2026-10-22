<#
.SYNOPSIS
  Times decoding the JPEG images in PDFs with Nexaflow.Media's decoder (a managed port of libjpeg-turbo) beside
  libjpeg-turbo itself and Windows' own codec, or holds every image's decode to libjpeg-turbo's. Run it by hand.

.DESCRIPTION
  Fetches libjpeg-turbo at a pinned tag and builds it (Release, x64, MSVC, its SIMD code assembled with NASM) when the
  checkout or its turbojpeg.dll is missing, builds the viewer test suite, runs DctDecodeBench, and prints the JSON file
  the run wrote: per image what each decoder took, whether ours and libjpeg-turbo decoded it to the same samples, and
  whether WIC did too.

  A timing is only worth comparing with another taken on the same machine in the same configuration. Release is the
  default because Debug is not what a user pays for.

.PARAMETER LibJpegTurbo
  The libjpeg-turbo checkout to measure against. Cloned at -Tag when it does not exist, and built when its
  turbojpeg.dll does not. Defaults to NEXAFLOW_LIBJPEG_TURBO.

.PARAMETER Tag
  The libjpeg-turbo release a fresh checkout is cloned at — the one Nexaflow.Media's decoder was ported from.

.PARAMETER Corpus
  Folders of PDFs. Defaults to NEXAFLOW_PDF_CORPUS, the baseline PDF corpus; PdfPig's own test documents when that is
  unset too.

.PARAMETER Compare
  Instead of timing a sample, decode every image with ours and libjpeg-turbo and compare them (DctComparisonSweep).
  Each image they disagree on is kept as a file, and the disagreements are printed grouped by outcome.

.PARAMETER PerSize
  How many images of each size (under 0.1 MP, 0.1-1, 1-4, 4 MP and over) are timed, spread from the smallest file
  to the largest; the same corpus always gives the same sample. 0 times every image.

.PARAMETER Only
  Measures only the images whose names hold one of these parts, separated by semicolons - "0966320.pdf#12;x.pdf" -
  instead of a sample of each size.

.PARAMETER Label
  What this run is - "baseline", "vectorised upsampling". Recorded in the run beside the commit.

.PARAMETER Out
  The folder runs are written to. Every run is a new file, so earlier runs stay to compare with.

.PARAMETER Configuration
  Release (default) or Debug.
#>
param(
    [string]$LibJpegTurbo = $env:NEXAFLOW_LIBJPEG_TURBO,
    [string]$Tag = '3.1.1',
    [string[]]$Corpus = @(),
    [switch]$Compare,
    [int]$PerSize = 40,
    [string]$Only = '',
    [string]$Label = '',
    [string]$Out = (Join-Path $env:LOCALAPPDATA 'Nexaflow\dct-bench'),
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $root 'src/Nexaflow.Tests/Nexaflow.Tests.Features.Viewers/Nexaflow.Tests.Features.Viewers.csproj'

if (-not $LibJpegTurbo) { throw 'name the libjpeg-turbo checkout to measure against: -LibJpegTurbo <folder> or NEXAFLOW_LIBJPEG_TURBO' }

if (-not (Test-Path (Join-Path $LibJpegTurbo 'CMakeLists.txt'))) {
    git clone --depth 1 --branch $Tag https://github.com/libjpeg-turbo/libjpeg-turbo.git $LibJpegTurbo
    if ($LASTEXITCODE -ne 0) { throw "could not clone libjpeg-turbo $Tag into $LibJpegTurbo" }
}

$build = Join-Path $LibJpegTurbo 'build'
$library = Join-Path $build 'Release/turbojpeg.dll'
if (-not (Test-Path $library)) {
    $cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
    if (-not $cmake) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (Test-Path $vswhere) {
            $cmake = & $vswhere -latest -products * -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' | Select-Object -First 1
        }
    }
    if (-not $cmake) { throw 'building libjpeg-turbo needs CMake and MSVC: install Visual Studio with the C++ workload, or put cmake on PATH' }

    # Without NASM libjpeg-turbo builds without its SIMD code, and the timing would be of a decoder nobody ships.
    # winget installs it per user, off PATH until a new shell.
    $nasm = (Get-Command nasm -ErrorAction SilentlyContinue).Source
    if (-not $nasm) {
        $nasm = @((Join-Path $env:LOCALAPPDATA 'bin/NASM/nasm.exe'), (Join-Path $env:ProgramFiles 'NASM/nasm.exe')) |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $nasm) { throw 'building libjpeg-turbo with its SIMD code needs NASM: winget install nasm' }

    & $cmake -S $LibJpegTurbo -B $build -A x64 -DENABLE_SHARED=ON -DWITH_TURBOJPEG=ON -DREQUIRE_SIMD=ON "-DCMAKE_ASM_NASM_COMPILER=$nasm"
    if ($LASTEXITCODE -ne 0) { throw 'configuring libjpeg-turbo failed' }
    & $cmake --build $build --config Release --parallel
    if ($LASTEXITCODE -ne 0) { throw 'building libjpeg-turbo failed' }
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
$env:NEXAFLOW_DCT_BENCH = $Out
$env:NEXAFLOW_TURBOJPEG = (Resolve-Path $library).Path
$env:NEXAFLOW_DCT_CORPUS = $Corpus -join [IO.Path]::PathSeparator
$env:NEXAFLOW_DCT_PER_SIZE = $PerSize
$env:NEXAFLOW_DCT_ONLY = $Only
$env:NEXAFLOW_BENCH_LABEL = $Label
$env:NEXAFLOW_BENCH_COMMIT = $commit

$exe = Join-Path $root "src/Nexaflow.Tests/Nexaflow.Tests.Features.Viewers/bin/x64/$Configuration/net10.0-windows10.0.19041.0/Nexaflow.Tests.Features.Viewers.exe"
function Invoke-Measurement($test, $kind) {
    $since = Get-Date
    & $exe --filter "FullyQualifiedName~$test" | Out-Null
    $written = Get-ChildItem $Out -Filter "dct-$kind-*.json" | Where-Object LastWriteTime -ge $since | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $written) { throw "$test wrote nothing to $Out - its dct-$kind-*.log says how far it got" }
    $written.FullName
    Get-Content $written.FullName -Raw | ConvertFrom-Json
}

if ($Compare) {
    "comparing every image with libjpeg-turbo - progress in a dct-compare-*.log in $Out"
    $file, $sweep = Invoke-Measurement 'EveryJpegImage_DecodesAsLibjpegTurboDoes' 'compare'
    $file
    "{0} images from {1} PDFs; libjpeg-turbo {2}, {3} {4}" -f $sweep.corpus.sampled, $sweep.corpus.pdfs, $sweep.libjpegTurbo.path, $sweep.configuration, $sweep.commit
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

"timing - each image is logged to a dct-bench-*.log in $Out as it is measured"
$file, $run = Invoke-Measurement 'HowManagedJpegDecodingComparesWithNative' 'bench'

$t = $run.totals
$file
"{0} of {1} images ({2:N1} MP) from {3} PDFs; {4} decoded identically by both; {5} {6}" -f `
    $run.corpus.sampled, $run.corpus.images, $t.megapixels, $run.corpus.pdfs, $run.corpus.identical, $run.configuration, $run.commit
# libjpeg-turbo decodes on one thread; ours threaded is what the reader runs.
"serial        managed {0,9:N0} ms   libjpeg-turbo {1,7:N0} ms   x{2}" -f $t.managedSingleMs, $t.nativeMs, $t.singleThreadRatio
"threaded      managed {0,9:N0} ms   libjpeg-turbo {1,7:N0} ms   x{2}   ({3} processors)" -f $t.managedMs, $t.nativeMs, $t.threadedRatio, $run.processors
"the reader    DCTDecode {0,7:N0} ms   libjpeg-turbo {1,7:N0} ms   x{2}   ({3} images)" -f $t.filterMs, $t.filterNativeMs, $t.filterRatio, $t.filterImages
"WIC           WIC {0,13:N0} ms   libjpeg-turbo {1,7:N0} ms   x{2}   ({3} grey or RGB images, {4} identical to libjpeg-turbo)" -f $t.wicMs, $t.wicNativeMs, $t.wicRatio, $t.wicImages, $t.wicIdentical
"              managed threaded {0,4:N0} ms   WIC {1,13:N0} ms   x{2}" -f $t.wicManagedMs, $t.wicMs, $t.threadedToWicRatio
"allocated     serial {0:N0} KB/MP   threaded {1:N0} KB/MP   the reader {2:N0} KB/MP   ({3:N0} MB in all serially, {4} gen2 collections)" -f `
    $t.managedSingleKbPerMp, $t.managedKbPerMp, $t.filterKbPerMp, ($t.managedSingleKb / 1024), $t.managedSingleGen2
$m = $run.memory
"memory        peak working set {0:N0} MB, GC paused {1:N0} ms, collections gen0 {2} gen1 {3} gen2 {4}" -f `
    $m.peakWorkingSetMb, $m.gcPauseMs, $m.gen0Collections, $m.gen1Collections, $m.gen2Collections
foreach ($size in $run.bySize) {
    "  {0,-14} {1,4} images   serial x{2,-6} threaded x{3,-6} WIC x{4,-6} threaded/WIC x{5}" -f `
        $size.size, $size.images, $size.singleThreadRatio, $size.threadedRatio, $size.wicRatio, $size.threadedToWicRatio
}
