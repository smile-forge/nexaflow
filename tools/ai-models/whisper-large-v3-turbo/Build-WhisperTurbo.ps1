<#
.SYNOPSIS
    Builds the onnxruntime-genai exports of Whisper large-v3-turbo that Nexaflow downloads from
    huggingface.co/smile-forge/whisper-large-v3-turbo-onnx-genai.

.DESCRIPTION
    No genai-format export of large-v3-turbo is published upstream, so Nexaflow builds its own with onnxruntime-genai's
    model builder — once, offline — and publishes the result; the app never converts anything on a user's machine.
    The builder in onnxruntime-genai 0.17.1 fails on turbo (32 encoder layers, 4 decoder layers), so it is patched
    first (patch_builder.py). Two builds come out:

      <Out>\cuda   fp16, for the CUDA pack   (needs CUDA option sdpa_kernel=1 at run time)
      <Out>\cpu    int8, for the CPU runtime

    and beside them the model card (README.md), the patch and this script, so the published repository says how to
    rebuild itself. Upload with
      hf upload smile-forge/whisper-large-v3-turbo-onnx-genai <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the builds are written.

.PARAMETER Work
    Scratch space: the virtual environment, the checkpoint, the builder's cache.

.PARAMETER Python
    A Python 3.10+ to create the virtual environment from. The builder needs PyTorch; with -SystemSitePackages, one
    already installed for that Python is used rather than downloading another.

.PARAMETER Checkpoint
    A folder already holding the checkpoint at the pinned revision, instead of downloading it.
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [string] $Python = 'python',
    [string] $Checkpoint,
    [switch] $SystemSitePackages
)

$ErrorActionPreference = 'Stop'

# What the published builds were made from. Change these only together with a new upload.
$Model          = 'openai/whisper-large-v3-turbo'
$Revision       = '41f01f3fe87f28c78e2fbf8b568835947dd65ed9'
$Packages       = @('onnxruntime==1.26.0', 'onnxruntime-genai==0.17.1', 'onnx_ir==1.0.0', 'transformers==5.19.0', 'safetensors')
$Here           = $PSScriptRoot

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

if (-not $Checkpoint) {
    $Checkpoint = Join-Path $Work 'checkpoint'
    & $py -I -m huggingface_hub.commands.huggingface_cli download $Model --revision $Revision `
        --include '*.json' '*.safetensors' '*.txt' --local-dir $Checkpoint
    if ($LASTEXITCODE) { throw "Could not download $Model at $Revision." }
}

& $py -I (Join-Path $Here 'patch_builder.py')
if ($LASTEXITCODE) { throw 'Could not patch the builder.' }

$env:USE_TF = '0'
$cache = Join-Path $Work 'builder-cache'
foreach ($build in @(@{ Folder = 'cuda'; Precision = 'fp16'; Provider = 'cuda' }, @{ Folder = 'cpu'; Precision = 'int8'; Provider = 'cpu' })) {
    $target = Join-Path $Out $build.Folder
    & $py -I -m onnxruntime_genai.models.builder -m $Model -i $Checkpoint -o $target -p $build.Precision -e $build.Provider -c $cache
    if ($LASTEXITCODE) { throw "The $($build.Folder) build failed." }
}

foreach ($file in 'README.md', 'patch_builder.py', 'Build-WhisperTurbo.ps1') { Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force }

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
