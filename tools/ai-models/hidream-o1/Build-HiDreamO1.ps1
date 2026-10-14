<#
.SYNOPSIS
    Builds the ONNX graph of HiDream-O1-Image that Nexaflow draws its high-quality pictures with, published at
    huggingface.co/smile-forge/hidream-o1-image-onnx.

.DESCRIPTION
    No ONNX build of HiDream-O1-Image is published upstream, so Nexaflow makes its own — once, offline, on a machine with
    an NVIDIA GPU of at least 48 GB — and publishes the result; the app never converts anything on a user's machine.
    build_hidream_o1.py traces HiDream's PyTorch model into one graph, its denoiser with the prompt cached, and
    check_hidream_o1.py draws twelve prompts with it and with PyTorch, so a build is never published unchecked:

      <Out>\denoiser   the denoiser, 8-bit weights, fp16 arithmetic on an fp32 residual stream, + tokenizer.json

    and beside it the model card (README.md) and the recipe, so the published repository says how to rebuild itself.
    Upload with
      hf upload smile-forge/hidream-o1-image-onnx <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the published files are written.

.PARAMETER Work
    Scratch space: the virtual environment, the checkpoint, HiDream's code, the fp16 graph, the check's pictures.

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

# What the published graph was made from. Change these only together with a new upload.
$Model      = 'HiDream-ai/HiDream-O1-Image'
$Revision   = '0b0901d99f200389e138c61946af1185f5f49a13'
$Code       = 'https://github.com/HiDream-ai/HiDream-O1-Image'
$CodeCommit = '2c2d29ff729e48f33e41f49edfdbd81d5ac103b4'
$Torch      = @('torch==2.11.0', 'torchvision==0.26.0')
# HiDream's model code is written against transformers 4.57, which keeps huggingface_hub below 1.0 — and diffusers with it.
$Packages   = @('transformers==4.57.1', 'diffusers==0.39.0', 'accelerate==1.15.0', 'safetensors==0.8.0', 'einops==0.8.2',
                'onnx==1.23.2', 'onnx-ir==1.0.0', 'onnxscript==0.7.2', 'onnxruntime-gpu==1.31.0', 'numpy==2.5.2', 'scipy',
                'pillow', 'tqdm', 'huggingface_hub')
$Here       = $PSScriptRoot

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

$code = Join-Path $Work 'code'
if (-not (Test-Path (Join-Path $code '.git'))) {
    git clone --quiet $Code $code
    if ($LASTEXITCODE) { throw "Could not clone $Code." }
}
git -C $code checkout --quiet $CodeCommit
if ($LASTEXITCODE) { throw "Could not check out $CodeCommit." }

if (-not $Checkpoint) {
    $Checkpoint = Join-Path $Work 'checkpoint'
    & $py -I -m huggingface_hub.commands.huggingface_cli download $Model --revision $Revision --local-dir $Checkpoint
    if ($LASTEXITCODE) { throw "Could not download $Model at $Revision." }
}

$build = Join-Path $Work 'build'
& $py -I -X utf8 (Join-Path $Here 'build_hidream_o1.py') $Checkpoint $code $build
if ($LASTEXITCODE) { throw 'The build failed.' }
& $py -I -X utf8 (Join-Path $Here 'check_hidream_o1.py') $Checkpoint $code $build (Join-Path $Work 'check')
if ($LASTEXITCODE) { throw 'The check failed.' }

$target = Join-Path $Out 'denoiser'
New-Item -ItemType Directory -Force $target | Out-Null
foreach ($file in 'denoiser.onnx', 'denoiser.onnx.data', 'tokenizer.json', 'branch-scales.json') {
    Copy-Item (Join-Path $build "denoiser\$file") $target -Force
}
foreach ($file in 'README.md', 'build_hidream_o1.py', 'check_hidream_o1.py', 'Build-HiDreamO1.ps1') { Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force }

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
