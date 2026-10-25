<#
.SYNOPSIS
    Builds the ONNX graphs of MOSS-Image that Nexaflow draws with, published at
    huggingface.co/smile-forge/moss-image-onnx.

.DESCRIPTION
    No ONNX build of MOSS-Image is published upstream for ONNX Runtime on CUDA, so Nexaflow makes its own — once,
    offline, on a machine with an NVIDIA GPU — and publishes the result; the app never converts anything on a user's
    machine. build_moss_image.py traces the PyTorch model into two graphs, convert_moss_loras.py turns each LoRA
    loras.json lists into an adapter for them, and check_moss_image.py and check_moss_loras.py draw with them and with
    PyTorch and measure each part's GPU memory, so a build is never published unchecked:

      <Out>\denoiser        the diffusion transformer, one pass, 8-bit weights, fp16 branches on an fp32 stream
      <Out>\decoder         the FLUX.2 small VAE decoder, fp16
      <Out>\loras           an ONNX Runtime adapter for each LoRA loras.json lists, and the distillation's sampler

    and beside them the model card (README.md) and the recipe, so the published repository says how to rebuild itself.
    The prompt encoder is Qwen3.5-9B on llama.cpp, a GGUF already published — Compare-MossEncoder.ps1 chose which.
    Upload with
      hf upload smile-forge/moss-image-onnx <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the published files are written.

.PARAMETER Work
    Scratch space shared with the comparison: the virtual environment, the checkpoint, the full-precision graphs, the
    check's pictures.

.PARAMETER Python
    A Python 3.12 or 3.13 to create the virtual environment from.

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
. (Join-Path $PSScriptRoot 'MossImage.Pins.ps1')

$setup = Initialize-MossWork $Work $Python $Checkpoint
New-Item -ItemType Directory -Force $Out | Out-Null

$build = Join-Path $Work 'build'
& $setup.Python -I -X utf8 (Join-Path $PSScriptRoot 'build_moss_image.py') $setup.Checkpoint $build
if ($LASTEXITCODE) { throw 'The build failed.' }

$listing   = Join-Path $PSScriptRoot 'loras.json'
$downloads = Join-Path $Work 'loras'
foreach ($lora in (Get-Content $listing -Raw | ConvertFrom-Json).loras) {
    Get-PinnedFile "https://huggingface.co/$($lora.repo)/resolve/$($lora.revision)/$($lora.file)" (Join-Path $downloads $lora.id) $lora.file $lora.sha256 | Out-Null
}
& $setup.Python -I -X utf8 (Join-Path $PSScriptRoot 'convert_moss_loras.py') $listing $downloads $build
if ($LASTEXITCODE) { throw 'Converting the LoRAs failed.' }

& $setup.Python -I -X utf8 (Join-Path $PSScriptRoot 'check_moss_image.py') $setup.Checkpoint $build (Join-Path $Work 'check')
if ($LASTEXITCODE) { throw 'The check failed.' }
& $setup.Python -I -X utf8 (Join-Path $PSScriptRoot 'check_moss_loras.py') $setup.Checkpoint $build $listing $downloads (Join-Path $Work 'check-loras')
if ($LASTEXITCODE) { throw 'The LoRAs'' check failed.' }

foreach ($part in 'denoiser', 'decoder') {
    $target = Join-Path $Out $part
    New-Item -ItemType Directory -Force $target | Out-Null
    $name = if ($part -eq 'decoder') { 'decoder' } else { 'denoiser' }
    Copy-Item (Join-Path $build "$part\$name.onnx"), (Join-Path $build "$part\$name.onnx.data") $target -Force
    if ($part -ne 'decoder') { Copy-Item (Join-Path $build "$part\branch-scales.json") $target -Force }
}
New-Item -ItemType Directory -Force (Join-Path $Out 'loras') | Out-Null
Copy-Item (Join-Path $build 'loras\*') (Join-Path $Out 'loras') -Force
Copy-Item (Join-Path $Work 'check\check.json') $Out -Force
Copy-Item (Join-Path $Work 'check-loras\loras.json') (Join-Path $Out 'check-loras.json') -Force
foreach ($file in 'README.md', 'MossImage.Pins.ps1', 'build_moss_image.py', 'check_moss_image.py', 'Build-MossImage.ps1',
                  'compare_moss_encoder.py', 'Compare-MossEncoder.ps1', 'loras.json', 'convert_moss_loras.py', 'check_moss_loras.py') {
    Copy-Item (Join-Path $PSScriptRoot $file) (Join-Path $Out $file) -Force
}

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
