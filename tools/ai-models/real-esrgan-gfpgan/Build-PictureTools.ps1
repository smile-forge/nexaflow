<#
.SYNOPSIS
    Builds the ONNX graphs of Real-ESRGAN, GFPGAN, RetinaFace and ParseNet that Nexaflow's picture tools upscale, clean
    and repair faces with, published at huggingface.co/smile-forge/real-esrgan-gfpgan-onnx.

.DESCRIPTION
    No ONNX build of these is published by their makers, so Nexaflow makes its own — once, offline, on a machine with an
    NVIDIA GPU — and publishes the result; the app never converts anything on a user's machine. upstream.py fetches the
    makers' code at pinned commits, the weights are downloaded from their releases and checked by hash, and
    build_picture_tools.py traces each model into a graph, holding it to the original as it goes:

      <Out>\upscaler-x4     Real-ESRGAN x4plus
      <Out>\upscaler-x2     Real-ESRGAN x2plus
      <Out>\face-detector   RetinaFace (ResNet-50), facexlib's
      <Out>\face-restorer   GFPGAN v1.4
      <Out>\face-parser     ParseNet, facexlib's

    each fp32, for CPU and CUDA alike, with the model card (README.md) and this recipe beside them. picture_reference.py
    then runs the makers' own pipelines on their own sample pictures into <Work>\reference — what the AI host's port is
    held to (Nexaflow.Tests.AiHost's PictureRestorationTests, with NEXAFLOW_TEST_PICTURE_TOOLS=<Out> and
    NEXAFLOW_TEST_PICTURE_REFERENCE=<Work>\reference). Upload with
      hf upload smile-forge/real-esrgan-gfpgan-onnx <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the published files are written.

.PARAMETER Work
    Scratch space: the virtual environment, the upstream code, the weights, the reference pictures.

.PARAMETER Python
    A Python 3.12 or later to create the virtual environment from.
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [string] $Python = 'python'
)

$ErrorActionPreference = 'Stop'

# What the published graphs were made from. Change these only together with a new upload.
$Weights = [ordered]@{
    'RealESRGAN_x4plus.pth'        = @('https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth', '4fa0d38905f75ac06eb49a7951b426670021be3018265fd191d2125df9d682f1')
    'RealESRGAN_x2plus.pth'        = @('https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.1/RealESRGAN_x2plus.pth', '49fafd45f8fd7aa8d31ab2a22d14d91b536c34494a5cfe31eb5d89c2fa266abb')
    'GFPGANv1.4.pth'               = @('https://github.com/TencentARC/GFPGAN/releases/download/v1.3.0/GFPGANv1.4.pth', 'e2cd4703ab14f4d01fd1383a8a8b266f9a5833dacee8e6a79d3bf21a1b6be5ad')
    'detection_Resnet50_Final.pth' = @('https://github.com/xinntao/facexlib/releases/download/v0.1.0/detection_Resnet50_Final.pth', '6d1de9c2944f2ccddca5f5e010ea5ae64a39845a86311af6fdf30841b0a5a16d')
    'parsing_parsenet.pth'         = @('https://github.com/xinntao/facexlib/releases/download/v0.2.2/parsing_parsenet.pth', '3d558d8d0e42c20224f13cf5a29c79eba2d59913419f945545d8cf7b72920de2')
}
$Torch    = @('torch==2.11.0', 'torchvision==0.26.0')
$Packages = @('onnx==1.23.2', 'onnx-ir==1.0.0', 'onnxscript==0.7.2', 'onnxruntime-gpu==1.31.0', 'numpy==2.5.2',
              'opencv-python==5.0.0.93', 'scipy', 'pillow', 'packaging')
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

# The weights are pickles: each is checked against its published hash before anything loads it, and loaded weights-only.
$weightsDir = Join-Path $Work 'weights'
New-Item -ItemType Directory -Force $weightsDir | Out-Null
foreach ($name in $Weights.Keys) {
    $file = Join-Path $weightsDir $name
    if (-not (Test-Path $file)) { Invoke-WebRequest -Uri $Weights[$name][0] -OutFile $file }
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $Weights[$name][1]) { throw "$name is not the published weights: its hash is $hash." }
}

$shim = & $py -I -X utf8 (Join-Path $Here 'upstream.py') $Work
if ($LASTEXITCODE) { throw 'Could not fetch the upstream code.' }
$src = Join-Path $Work 'src'
$pictures = @(
    (Join-Path $src 'GFPGAN\inputs\whole_imgs\Blake_Lively.jpg'),
    (Join-Path $src 'GFPGAN\inputs\whole_imgs\00.jpg'),
    (Join-Path $src 'GFPGAN\inputs\whole_imgs\10045.png'),
    (Join-Path $src 'Real-ESRGAN\inputs\children-alpha.png'),
    (Join-Path $src 'Real-ESRGAN\inputs\0014.jpg')
)

$build = Join-Path $Work 'build'
& $py -I -X utf8 (Join-Path $Here 'build_picture_tools.py') $weightsDir $shim $build @pictures
if ($LASTEXITCODE) { throw 'The build failed.' }

# The reference runs GFPGAN's helper, which looks for its face models under gfpgan/weights in the folder it runs in.
$run = Join-Path $Work 'run'
New-Item -ItemType Directory -Force (Join-Path $run 'gfpgan\weights') | Out-Null
foreach ($name in 'detection_Resnet50_Final.pth', 'parsing_parsenet.pth') { Copy-Item (Join-Path $weightsDir $name) (Join-Path $run "gfpgan\weights\$name") -Force }
Push-Location $run
try {
    & $py -I -X utf8 (Join-Path $Here 'picture_reference.py') $weightsDir $shim (Join-Path $Work 'reference') @pictures
    if ($LASTEXITCODE) { throw 'The reference pictures could not be made.' }
}
finally { Pop-Location }

foreach ($folder in 'upscaler-x4', 'upscaler-x2', 'face-detector', 'face-restorer', 'face-parser') {
    $target = Join-Path $Out $folder
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $build "$folder\$folder.onnx") $target -Force
}
foreach ($file in 'README.md', 'upstream.py', 'build_picture_tools.py', 'picture_reference.py', 'Build-PictureTools.ps1') {
    Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force
}

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
