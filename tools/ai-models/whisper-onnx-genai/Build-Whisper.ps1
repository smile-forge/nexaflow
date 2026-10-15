<#
.SYNOPSIS
    Builds the onnxruntime-genai exports of Whisper small and small.en that Nexaflow downloads from
    huggingface.co/smile-forge/whisper-onnx-genai.

.DESCRIPTION
    No genai-format export of these checkpoints is published upstream, so Nexaflow builds its own with onnxruntime-genai's
    model builder — once, offline — and publishes the result; the app never converts anything on a user's machine.
    Each model comes out in two builds, in a folder of its own:

      <Out>\<model>\cuda   fp16, for the CUDA pack   (needs CUDA option sdpa_kernel=1 at run time)
      <Out>\<model>\cpu    int8, for the CPU runtime

    and beside them the model card (README.md) and this script, so the published repository says how to rebuild
    itself. Upload with
      hf upload smile-forge/whisper-onnx-genai <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

    large-v3-turbo is built by tools/ai-models/whisper-large-v3-turbo and published on its own.

.PARAMETER Out
    Where the builds are written.

.PARAMETER Work
    Scratch space: the virtual environment, the checkpoints, the builder's cache.

.PARAMETER Python
    A Python 3.10+ to create the virtual environment from. The builder needs PyTorch; with -SystemSitePackages, one
    already installed for that Python is used rather than downloading another.
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [string] $Python = 'python',
    [switch] $SystemSitePackages
)

$ErrorActionPreference = 'Stop'

# What the published builds were made from. Change these only together with a new upload.
$Models = @(
    @{ Folder = 'small';    Model = 'openai/whisper-small';    Revision = '973afd24965f72e36ca33b3055d56a652f456b4d' },
    @{ Folder = 'small.en'; Model = 'openai/whisper-small.en'; Revision = 'e8727524f962ee844a7319d92be39ac1bd25655a' }
)
$Packages = @('onnxruntime==1.26.0', 'onnxruntime-genai==0.17.1', 'onnx_ir==1.0.0', 'transformers==5.19.0', 'safetensors', 'huggingface_hub==2.2.0')
$Here     = $PSScriptRoot

New-Item -ItemType Directory -Force $Out, $Work | Out-Null
$venv = Join-Path $Work 'venv'
if (-not (Test-Path (Join-Path $venv 'Scripts\python.exe'))) {
    $venvArgs = @('-I', '-m', 'venv') + $(if ($SystemSitePackages) { @('--system-site-packages') } else { @() }) + @($venv)
    & $Python @venvArgs
    if ($LASTEXITCODE) { throw 'Could not create the virtual environment.' }
}
$py = Join-Path $venv 'Scripts\python.exe'
& $py -I -m pip install --disable-pip-version-check @Packages
if ($LASTEXITCODE) { throw 'Could not install the builder.' }

$env:USE_TF = '0'
$cache = Join-Path $Work 'builder-cache'
foreach ($model in $Models) {
    $checkpoint = Join-Path $Work "checkpoint\$($model.Folder)"
    & $py -I -c 'import sys; from huggingface_hub import snapshot_download; snapshot_download(sys.argv[1], revision=sys.argv[2], local_dir=sys.argv[3], allow_patterns=sys.argv[4:])' `
        $model.Model $model.Revision $checkpoint '*.json' '*.safetensors' '*.txt'
    if ($LASTEXITCODE) { throw "Could not download $($model.Model) at $($model.Revision)." }

    foreach ($build in @(@{ Folder = 'cuda'; Precision = 'fp16'; Provider = 'cuda' }, @{ Folder = 'cpu'; Precision = 'int8'; Provider = 'cpu' })) {
        $target = Join-Path $Out "$($model.Folder)\$($build.Folder)"
        & $py -I -m onnxruntime_genai.models.builder -m $model.Model -i $checkpoint -o $target -p $build.Precision -e $build.Provider -c $cache
        if ($LASTEXITCODE) { throw "The $($model.Folder) $($build.Folder) build failed." }
    }
}

foreach ($file in 'README.md', 'Build-Whisper.ps1') { Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force }

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
