<#
.SYNOPSIS
    Builds the ONNX graphs of Wan2.2-TI2V-5B and FastWan2.2-TI2V-5B that Nexaflow makes its clips with, published at
    huggingface.co/smile-forge/wan2.2-ti2v-5b-onnx.

.DESCRIPTION
    No ONNX build of Wan2.2-TI2V-5B is published upstream, so Nexaflow makes its own — once, offline, on a machine with
    an NVIDIA GPU of at least 48 GB — and publishes the result; the app never converts anything on a user's machine.
    build_wan.py traces the PyTorch models into five graphs, holding each to diffusers' and transformers' own models as
    it goes, and check_wan.py makes clips with the graphs and with PyTorch from the same noise and compares them, so a
    build is never published unchecked:

      <Out>\prompt-encoder   UMT5-XXL's encoder, 8-bit weights, fp32 activations, + spiece.model
      <Out>\denoiser         Wan2.2-TI2V-5B's transformer, fp16
      <Out>\denoiser-fast    FastWan2.2-TI2V-5B's transformer, fp16
      <Out>\decoder          the VAE decoder, a latent frame at a time: decoder-first.onnx, decoder.onnx, one weights file,
                             and caches.json, the shape of the cache it carries from call to call
      <Out>\frame-encoder    the VAE encoder, for the picture a clip starts from

    and beside them the model card (README.md) and the recipe, so the published repository says how to rebuild itself.
    Upload with
      hf upload smile-forge/wan2.2-ti2v-5b-onnx <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the published files are written.

.PARAMETER Work
    Scratch space: the virtual environment, the checkpoints, the fp32 prompt encoder, the check's clips.

.PARAMETER Python
    A Python 3.12 to create the virtual environment from.

.PARAMETER Base
    A folder already holding Wan2.2-TI2V-5B-Diffusers at the pinned revision, instead of downloading it.

.PARAMETER Fast
    A folder already holding FastWan2.2-TI2V-5B-FullAttn-Diffusers' transformer at the pinned revision.
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [string] $Python = 'python',
    [string] $Base,
    [string] $Fast
)

$ErrorActionPreference = 'Stop'

# What the published graphs were made from. Change these only together with a new upload.
$BaseModel    = 'Wan-AI/Wan2.2-TI2V-5B-Diffusers'
$BaseRevision = 'b8fff7315c768468a5333511427288870b2e9635'
$FastModel    = 'FastVideo/FastWan2.2-TI2V-5B-FullAttn-Diffusers'
$FastRevision = '3e187042a324f6f5fb68fd22110a78725253de8f'
$Torch        = @('torch==2.11.0', 'torchvision==0.26.0')
$Packages     = @('transformers==4.57.1', 'diffusers==0.39.0', 'accelerate==1.15.0', 'safetensors==0.8.0', 'sentencepiece',
                  'ftfy==6.3.1', 'onnx==1.23.2', 'onnx-ir==1.0.0', 'onnxscript==0.7.2', 'onnxruntime-gpu==1.31.0',
                  'numpy==2.5.2', 'tqdm', 'huggingface_hub')
$Here         = $PSScriptRoot

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

if (-not $Base) {
    $Base = Join-Path $Work 'wan2.2-ti2v-5b'
    & $py -I -X utf8 -m huggingface_hub.commands.huggingface_cli download $BaseModel --revision $BaseRevision --local-dir $Base
    if ($LASTEXITCODE) { throw "Could not download $BaseModel at $BaseRevision." }
}
if (-not $Fast) {
    $Fast = Join-Path $Work 'fastwan2.2-ti2v-5b'
    & $py -I -X utf8 -m huggingface_hub.commands.huggingface_cli download $FastModel --revision $FastRevision --local-dir $Fast --include 'transformer/*'
    if ($LASTEXITCODE) { throw "Could not download $FastModel at $FastRevision." }
}

$build = Join-Path $Work 'build'
& $py -I -X utf8 (Join-Path $Here 'build_wan.py') $Base $Fast $build
if ($LASTEXITCODE) { throw 'The build failed.' }
& $py -I -X utf8 (Join-Path $Here 'check_wan.py') $Base $Fast $build (Join-Path $Work 'check')
if ($LASTEXITCODE) { throw 'The check failed.' }

$published = @{
    'prompt-encoder' = @('prompt-encoder.onnx', 'prompt-encoder.onnx.data', 'spiece.model')
    'denoiser'       = @('denoiser.onnx', 'denoiser.onnx.data')
    'denoiser-fast'  = @('denoiser.onnx', 'denoiser.onnx.data')
    'decoder'        = @('decoder-first.onnx', 'decoder-first.onnx.data', 'decoder.onnx', 'caches.json')
    'frame-encoder'  = @('frame-encoder.onnx', 'frame-encoder.onnx.data')
}
foreach ($folder in $published.Keys) {
    $target = Join-Path $Out $folder
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($file in $published[$folder]) { Copy-Item (Join-Path $build "$folder\$file") $target -Force }
}
foreach ($file in 'README.md', 'build_wan.py', 'check_wan.py', 'wan_reference.py', 'Build-Wan.ps1') { Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force }

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
