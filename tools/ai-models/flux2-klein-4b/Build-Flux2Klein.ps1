<#
.SYNOPSIS
    Builds the ONNX graphs of FLUX.2 [klein] 4B that Nexaflow draws and edits pictures with, published at
    huggingface.co/smile-forge/flux2-klein-4b-onnx.

.DESCRIPTION
    No ONNX build of FLUX.2 [klein] is published upstream for ONNX Runtime on CUDA, so Nexaflow makes its own — once,
    offline, on a machine with an NVIDIA GPU — and publishes the result; the app never converts anything on a user's
    machine. build_flux2_klein.py traces the PyTorch model into four graphs and check_flux2_klein.py makes edits with them
    and with Black Forest Labs' pipelines in diffusers, so a build is never published unchecked:

      <Out>\encoder           the prompt encoder (Qwen3-4B to its 27th layer), 8-bit weights, fp32 arithmetic, + tokenizer.json
      <Out>\denoiser          the rectified-flow transformer, 8-bit weights, fp16 on fp32 residual streams
      <Out>\picture-encoder   the VAE encoder, fp16
      <Out>\decoder           the VAE decoder, fp16

    and beside them the model card (README.md) and the recipe, so the published repository says how to rebuild itself.
    Upload with
      hf upload smile-forge/flux2-klein-4b-onnx <Out> .
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
$Model    = 'black-forest-labs/FLUX.2-klein-4B'
$Revision = 'e7b7dc27f91deacad38e78976d1f2b499d76a294'
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
    & $py -I -m huggingface_hub.commands.huggingface_cli download $Model --revision $Revision --exclude '*.jpg' 'flux-2-klein-4b.safetensors' --local-dir $Checkpoint
    if ($LASTEXITCODE) { throw "Could not download $Model at $Revision." }
}

$build = Join-Path $Work 'build'
& $py -I -X utf8 (Join-Path $Here 'build_flux2_klein.py') $Checkpoint $build
if ($LASTEXITCODE) { throw 'The build failed.' }
& $py -I -X utf8 (Join-Path $Here 'check_flux2_klein.py') $Checkpoint $build $Work
if ($LASTEXITCODE) { throw 'The check failed.' }

foreach ($part in 'encoder', 'denoiser', 'picture-encoder', 'decoder') {
    $target = Join-Path $Out $part
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $build "$part\$part.onnx"), (Join-Path $build "$part\$part.onnx.data") $target -Force
}
Copy-Item (Join-Path $Checkpoint 'tokenizer\tokenizer.json') (Join-Path $Out 'encoder') -Force
foreach ($file in 'README.md', 'build_flux2_klein.py', 'check_flux2_klein.py', 'Build-Flux2Klein.ps1') { Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force }

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
