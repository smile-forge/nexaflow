<#
.SYNOPSIS
  Times decoding the JPEG 2000 images in PDFs with Nexaflow.Media's decoder (OpenJPEG.net, a managed port of
  OpenJPEG) beside libopenjp2, the C library it was ported from, and samples where the managed decode spends its
  time. Run it by hand.

.DESCRIPTION
  Fetches OpenJPEG at a pinned tag and builds it (Release, x64, MSVC) when the checkout or its build is missing,
  builds the viewer test suite, runs JpxDecodeBench, and prints the JSON file the run wrote: per image what each
  library took on one thread and threaded, whether both decoded it to the same samples, and two stage profiles of
  the managed decode — the reader's whole JPXDecode filter, for where its wall time goes, and the decode on one
  thread, for where the work is.

  A timing is only worth comparing with another taken on the same machine in the same configuration. Release is the
  default because Debug is not what a user pays for.

.PARAMETER OpenJpeg
  The OpenJPEG checkout to measure against. Cloned at -Tag when it does not exist, and built when its openjp2.dll
  does not. Defaults to NEXAFLOW_OPENJPEG.

.PARAMETER Tag
  The OpenJPEG release a fresh checkout is cloned at.

.PARAMETER Corpus
  Folders of PDFs, in place of the two below.

.PARAMETER JpxDocuments
  Where the JPX test documents are fetched to: the 151 PDFs of the JPX filter Nexaflow used to build, kept on GitHub
  as smile-forge/PdfPig.Filters.Jpx.OpenJpeg. Fetched at -JpxDocumentsCommit when absent, documents only. Defaults to
  jpx-test-documents beside the OpenJPEG checkout.

.PARAMETER JpxDocumentsCommit
  The commit those documents are fetched at, so the same corpus is measured run after run.

.PARAMETER PdfCorpus
  A further folder of PDFs measured with them — the baseline PDF corpus. Defaults to NEXAFLOW_PDF_CORPUS.

.PARAMETER PerSize
  How many images of each size (under 0.1 MP, 0.1-1, 1-4, 4 MP and over) are timed, spread from the smallest file
  to the largest; the same corpus always gives the same sample. 0 times every image.

.PARAMETER Label
  What this run is - "baseline", "pooled code-block buffers". Recorded in the run beside the commit.

.PARAMETER Out
  The folder runs are written to. Every run is a new file, so earlier runs stay to compare with.

.PARAMETER Configuration
  Release (default) or Debug.
#>
param(
    [string]$OpenJpeg = $env:NEXAFLOW_OPENJPEG,
    [string]$Tag = 'v2.5.4',
    [string[]]$Corpus = @(),
    [string]$JpxDocuments = '',
    [string]$JpxDocumentsCommit = '206bfaaddfe2c33d122b15638dff0d85e6bcd679',
    [string]$PdfCorpus = $env:NEXAFLOW_PDF_CORPUS,
    [int]$PerSize = 6,
    [string]$Label = '',
    [string]$Out = (Join-Path $env:LOCALAPPDATA 'Nexaflow\jpx-bench'),
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $root 'src/Nexaflow.Tests/Nexaflow.Tests.Features.Viewers/Nexaflow.Tests.Features.Viewers.csproj'

if (-not $OpenJpeg) { throw 'name the OpenJPEG checkout to measure against: -OpenJpeg <folder> or NEXAFLOW_OPENJPEG' }

if (-not (Test-Path (Join-Path $OpenJpeg 'CMakeLists.txt'))) {
    git clone --depth 1 --branch $Tag https://github.com/uclouvain/openjpeg.git $OpenJpeg
    if ($LASTEXITCODE -ne 0) { throw "could not clone OpenJPEG $Tag into $OpenJpeg" }
}

$build = Join-Path $OpenJpeg 'build'
$library = Join-Path $build 'bin/Release/openjp2.dll'
if (-not (Test-Path $library)) {
    $cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
    if (-not $cmake) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (Test-Path $vswhere) {
            $cmake = & $vswhere -latest -products * -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' | Select-Object -First 1
        }
    }
    if (-not $cmake) { throw 'building OpenJPEG needs CMake and MSVC: install Visual Studio with the C++ workload, or put cmake on PATH' }

    & $cmake -S $OpenJpeg -B $build -A x64 -DBUILD_SHARED_LIBS=ON -DBUILD_CODEC=ON -DBUILD_THIRDPARTY=ON -DBUILD_TESTING=OFF
    if ($LASTEXITCODE -ne 0) { throw 'configuring OpenJPEG failed' }
    & $cmake --build $build --config Release --parallel
    if ($LASTEXITCODE -ne 0) { throw 'building OpenJPEG failed' }
}

if ($Corpus.Count -eq 0) {
    if (-not $JpxDocuments) { $JpxDocuments = Join-Path (Split-Path -Parent $OpenJpeg) 'jpx-test-documents' }
    $documents = 'UglyToad.PdfPig.Filters.Jpx.OpenJpeg.Tests/Documents'
    if (-not (Test-Path (Join-Path $JpxDocuments $documents))) {
        New-Item -ItemType Directory -Force $JpxDocuments | Out-Null
        git -C $JpxDocuments init -q
        git -C $JpxDocuments remote add origin https://github.com/smile-forge/PdfPig.Filters.Jpx.OpenJpeg.git
        git -C $JpxDocuments sparse-checkout set $documents
        git -C $JpxDocuments -c core.longpaths=true fetch -q --depth 1 origin $JpxDocumentsCommit
        if ($LASTEXITCODE -ne 0) { throw "could not fetch the JPX test documents at $JpxDocumentsCommit" }
        git -C $JpxDocuments -c core.longpaths=true checkout -q FETCH_HEAD
        if ($LASTEXITCODE -ne 0) { throw "could not check out the JPX test documents into $JpxDocuments" }
    }
    $Corpus = @(Join-Path $JpxDocuments $documents)
    if ($PdfCorpus) {
        if (-not (Test-Path $PdfCorpus)) { throw "the PDF corpus $PdfCorpus does not exist" }
        $Corpus += $PdfCorpus
    }
}

dotnet build $project -c $Configuration -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "the $Configuration build failed" }

$commit = (git -C $root rev-parse --short HEAD).Trim()
if (git -C $root status --porcelain) { $commit += '+changes' }

New-Item -ItemType Directory -Force $Out | Out-Null
$env:NEXAFLOW_JPX_BENCH = $Out
$env:NEXAFLOW_OPENJP2 = (Resolve-Path $library).Path
$env:NEXAFLOW_JPX_CORPUS = $Corpus -join [IO.Path]::PathSeparator
$env:NEXAFLOW_JPX_PER_SIZE = $PerSize
$env:NEXAFLOW_BENCH_LABEL = $Label
$env:NEXAFLOW_BENCH_COMMIT = $commit

$exe = Join-Path $root "src/Nexaflow.Tests/Nexaflow.Tests.Features.Viewers/bin/x64/$Configuration/net10.0-windows10.0.19041.0/Nexaflow.Tests.Features.Viewers.exe"
function Invoke-Measurement($test, $kind) {
    $since = Get-Date
    & $exe --filter "FullyQualifiedName~JpxDecodeBench.$test" | Out-Null
    $written = Get-ChildItem $Out -Filter "jpx-$kind-*.json" | Where-Object LastWriteTime -ge $since | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $written) { throw "$test wrote nothing to $Out - its jpx-$kind-*.log says how far it got" }
    $written.FullName
    Get-Content $written.FullName -Raw | ConvertFrom-Json
}

"timing - each image is logged to a jpx-bench-*.log in $Out as it is measured"
$file, $run = Invoke-Measurement 'HowManagedJpxDecodingComparesWithNative' 'bench'

# The profile runs in a process of its own with the framework jitted, so every frame it samples is named.
"profiling - in its own process, with ReadyToRun off"
$env:DOTNET_ReadyToRun = '0'
try { $profileFile, $profiled = Invoke-Measurement 'WhereManagedJpxDecodingSpendsItsTime' 'profile' }
finally { Remove-Item Env:DOTNET_ReadyToRun }

$t = $run.totals
$file
"{0} of {1} images ({2:N1} MP) from {3} PDFs; {4} decoded identically by both; libopenjp2 {5}, {6} {7}" -f `
    $run.corpus.sampled, $run.corpus.images, $t.megapixels, $run.corpus.pdfs, $run.corpus.identical, $run.libopenjp2.version, $run.configuration, $run.commit
# The managed decoder hands each code block to the thread pool even told not to thread; serial, it hands out one at a time.
"serial        managed {0,9:N0} ms   libopenjp2 {1,7:N0} ms   x{2}" -f $t.managedSingleMs, $t.nativeSingleMs, $t.singleThreadRatio
"threaded      managed {0,9:N0} ms   libopenjp2 {1,7:N0} ms   x{2}   ({3} processors)" -f $t.managedMs, $t.nativeMs, $t.threadedRatio, $run.processors
"the reader    JPXDecode {0,9:N0} ms   libopenjp2 {1,7:N0} ms   x{2}   ({3} images)" -f $t.filterMs, $t.filterNativeMs, $t.filterRatio, $t.filterImages
foreach ($size in $run.bySize) {
    "  {0,-14} {1,4} images   serial x{2,-6} threaded x{3}" -f $size.size, $size.images, $size.singleThreadRatio, $size.threadedRatio
}

function Show-Work($title, $profile) {
    $title
    $all = [double][Math]::Max(1, $profile.workSamples)
    foreach ($stage in $profile.work | Select-Object -First 9) {
        "  {0,5:P0}  {1,-52} {2,7:N0} samples" -f ($stage.samples / $all), $stage.stage, $stage.samples
    }
    "  the calling thread spent {0:P0} of its {1:N0} samples blocked or outside managed code" -f `
        ($profile.callerWaiting / [double][Math]::Max(1, $profile.callerSamples)), $profile.callerSamples
}
$profileFile
Show-Work 'where the reader''s decode works, every thread, waits taken out' $profiled.appProfile
Show-Work 'where the serial decode works, waits taken out' $profiled.decodeProfile
