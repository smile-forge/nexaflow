<#
.SYNOPSIS
    Builds the ONNX graphs of Z-Image-Turbo that Nexaflow draws with, published at
    huggingface.co/smile-forge/z-image-turbo-onnx.

.DESCRIPTION
    No ONNX build of Z-Image-Turbo is published upstream for ONNX Runtime on CUDA, so Nexaflow makes its own — once,
    offline, on a machine with an NVIDIA GPU — and publishes the result; the app never converts anything on a user's
    machine. build_z_image.py traces the PyTorch pipeline into three graphs and check_z_image.py draws twelve prompts
    with them and with PyTorch, so a build is never published unchecked:

      <Out>\encoder    the prompt encoder (Qwen3-4B to its 35th layer), 8-bit weights, fp32 arithmetic, + tokenizer.json
      <Out>\denoiser   the diffusion transformer, 8-bit weights, fp16 arithmetic
      <Out>\decoder    the VAE decoder, fp16

    and beside them the model card (README.md) and the recipe, so the published repository says how to rebuild itself.
    Upload with
      hf upload smile-forge/z-image-turbo-onnx <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the published files are written.

.PARAMETER Work
    Scratch space: the virtual environment, the checkpoint, the full-precision graphs, the check's pictures.

.PARAMETER Python
    A Python 3.12 to create the virtual environment from.

.PARAMETER Checkpoint
    A folder already holding the checkpoint at the pinned revision, instead of downloading it.
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [string] $Python = 'python',
    [string] $Checkpoint
)

$ErrorActionPreference = 'Stop'

# What the published graphs were made from. Change these only together with a new upload.
$Model    = 'Tongyi-MAI/Z-Image-Turbo'
$Revision = 'f332072aa78be7aecdf3ee76d5c247082da564a6'
$Torch    = @('torch==2.11.0', 'torchvision==0.26.0')
$Packages = @('diffusers==0.41.0', 'transformers==5.19.0', 'accelerate==1.15.0', 'safetensors==0.8.0', 'onnx==1.23.2',
              'onnx-ir==1.0.0', 'onnxscript==0.7.2', 'onnxruntime-gpu==1.31.0', 'numpy==2.5.2', 'pillow', 'huggingface_hub')
$Here     = $PSScriptRoot

New-Item -ItemType Directory -Force $Out, $Work | Out-Null
$venv = Join-Path $Work 'venv'
if (-not (Test-Path (Join-Path $venv 'Scripts\python.exe'))) {
    & $Python -I -m venv $venv
    if ($LASTEXITCODE) { throw 'Could not create the virtual environment.' }
}
$py = Join-Path $venv 'Scripts\python.exe'
& $py -I -m pip install --disable-pip-version-check --index-url https://download.pytorch.org/whl/cu128 @Torch
if ($LASTEXITCODE) { throw 'Could not install PyTorch.' }
& $py -I -m pip install --disable-pip-version-check @Packages
if ($LASTEXITCODE) { throw 'Could not install the builder.' }

if (-not $Checkpoint) {
    $Checkpoint = Join-Path $Work 'checkpoint'
    & $py -I -m huggingface_hub.commands.huggingface_cli download $Model --revision $Revision --exclude 'assets/*' --local-dir $Checkpoint
    if ($LASTEXITCODE) { throw "Could not download $Model at $Revision." }
}

$build = Join-Path $Work 'build'
& $py -I -X utf8 (Join-Path $Here 'build_z_image.py') $Checkpoint $build
if ($LASTEXITCODE) { throw 'The build failed.' }
& $py -I -X utf8 (Join-Path $Here 'check_z_image.py') $Checkpoint $build (Join-Path $Work 'check')
if ($LASTEXITCODE) { throw 'The check failed.' }

foreach ($part in 'encoder', 'denoiser', 'decoder') {
    $target = Join-Path $Out $part
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $build "$part\$part.onnx"), (Join-Path $build "$part\$part.onnx.data") $target -Force
}
Copy-Item (Join-Path $Checkpoint 'tokenizer\tokenizer.json') (Join-Path $Out 'encoder') -Force
Copy-Item (Join-Path $build 'denoiser\branch-scales.json') (Join-Path $Out 'denoiser') -Force
foreach ($file in 'README.md', 'build_z_image.py', 'check_z_image.py', 'Build-ZImageTurbo.ps1') { Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force }

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
