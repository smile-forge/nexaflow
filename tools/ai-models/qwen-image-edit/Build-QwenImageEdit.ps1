<#
.SYNOPSIS
    Builds the ONNX graphs of Qwen-Image-Edit-2511 that Nexaflow edits pictures with, published at
    huggingface.co/smile-forge/qwen-image-edit-2511-onnx.

.DESCRIPTION
    No ONNX build of Qwen-Image-Edit is published upstream for ONNX Runtime on CUDA, so Nexaflow makes its own — once,
    offline, on a machine with an NVIDIA GPU of at least 96 GB — and publishes the result; the app never converts
    anything on a user's machine. build_qwen_image_edit.py traces the PyTorch model into five graphs, holding each to the
    original as it goes, and check_qwen_image_edit.py makes four edits with them and with diffusers' own pipeline, so a
    build is never published unchecked:

      <Out>\picture-reader    Qwen2.5-VL's vision tower, fp32
      <Out>\prompt-encoder    Qwen2.5-VL-7B's language model, 8-bit weights, fp32 arithmetic, + tokenizer.json
      <Out>\picture-encoder   Qwen-Image's VAE encoder, fp16
      <Out>\denoiser          the 60-layer MMDiT, 8-bit weights, fp16 on fp32 residual streams
      <Out>\denoiser-fp16     the same with fp16 weights, for a GPU with the room
      <Out>\decoder           Qwen-Image's VAE decoder, fp16

    and beside them the model card (README.md) and the recipe, so the published repository says how to rebuild itself.
    Upload with
      hf upload smile-forge/qwen-image-edit-2511-onnx <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the published files are written.

.PARAMETER Work
    Scratch space: the virtual environment, the checkpoint, the full-precision graphs, the check's pictures.

.PARAMETER Pictures
    The pictures calibration and the check edit, PNGs named 00 to 09: HiDream-O1's calibration drawings, which
    Build-HiDreamO1.ps1 leaves in its <Work>\build\calibration.

.PARAMETER Python
    A Python 3.12 to create the virtual environment from.

.PARAMETER Checkpoint
    A folder already holding the checkpoint at the pinned revision, instead of downloading it.
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [Parameter(Mandatory)] [string] $Pictures,
    [string] $Python = 'python',
    [string] $Checkpoint
)

$ErrorActionPreference = 'Stop'

# What the published graphs were made from. Change these only together with a new upload.
$Model    = 'Qwen/Qwen-Image-Edit-2511'
$Revision = '6f3ccc0b56e431dc6a0c2b2039706d7d26f22cb9'
$Torch    = @('torch==2.11.0', 'torchvision==0.26.0')
# transformers 4.57, as the pipeline was written against: from 5.0 Qwen2.5-VL numbers a prompt's pictures in 3D only when
# given mm_token_type_ids, which diffusers' pipeline does not pass, so it would read every token on the diagonal. 4.57
# keeps huggingface_hub below 1.0, and diffusers with it.
$Packages = @('diffusers==0.39.0', 'transformers==4.57.1', 'accelerate==1.15.0', 'safetensors==0.8.0', 'onnx==1.23.2',
              'onnx-ir==1.0.0', 'onnxscript==0.7.2', 'onnxruntime-gpu==1.31.0', 'numpy==2.5.2', 'pillow', 'einops', 'huggingface_hub')
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
    & $py -I -m huggingface_hub.commands.huggingface_cli download $Model --revision $Revision --local-dir $Checkpoint
    if ($LASTEXITCODE) { throw "Could not download $Model at $Revision." }
}

$build = Join-Path $Work 'build'
& $py -I -X utf8 (Join-Path $Here 'build_qwen_image_edit.py') $Checkpoint $build --pictures $Pictures
if ($LASTEXITCODE) { throw 'The build failed.' }
& $py -I -X utf8 (Join-Path $Here 'check_qwen_image_edit.py') $Checkpoint $build $Pictures (Join-Path $Work 'check')
if ($LASTEXITCODE) { throw 'The check failed.' }

foreach ($part in 'picture-reader', 'prompt-encoder', 'picture-encoder', 'decoder') {
    $target = Join-Path $Out $part
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $build "$part\$part.onnx"), (Join-Path $build "$part\$part.onnx.data") $target -Force
}
Copy-Item (Join-Path $build 'prompt-encoder\tokenizer.json') (Join-Path $Out 'prompt-encoder') -Force
foreach ($folder in 'denoiser', 'denoiser-fp16') {
    $target = Join-Path $Out $folder
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($file in 'denoiser.onnx', 'denoiser.onnx.data', 'branch-scales.json') { Copy-Item (Join-Path $build "$folder\$file") $target -Force }
}
foreach ($file in 'README.md', 'build_qwen_image_edit.py', 'check_qwen_image_edit.py', 'edit_reference.py', 'Build-QwenImageEdit.ps1') {
    Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force
}

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
