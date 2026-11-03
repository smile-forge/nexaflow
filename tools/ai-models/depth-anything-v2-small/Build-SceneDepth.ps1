<#
.SYNOPSIS
    Builds the ONNX graph of Depth Anything V2 Small, the scene depth Nexaflow's lens blur, haze and selection by distance are
    drawn from, published at huggingface.co/smile-forge/depth-anything-v2-small-onnx.

.DESCRIPTION
    Only the Small model is Apache-2.0 — Base, Large and Giant are CC-BY-NC-4.0 and are never built here. No ONNX build of it
    takes any picture size on ONNX Runtime's CUDA provider with the makers' own positional embedding, so Nexaflow makes its own
    — once, offline, on a machine with an NVIDIA GPU — and publishes the result; the app never converts anything on a user's
    machine. upstream.py fetches the makers' code at a pinned commit, their weights are downloaded from their Hugging Face
    repository at a pinned revision and checked by hash, and build_depth.py traces the model into a graph, holding it to the
    original as it goes:

      <Out>\estimator     Depth Anything V2 Small, fp32, for CPU and CUDA alike

    with the model card (README.md) and this recipe beside it. depth_reference.py then runs the makers' own infer_image on
    their sample pictures into <Work>\reference — what the AI host's port is held to (Nexaflow.Tests.AiHost's SceneDepthTests,
    with NEXAFLOW_TEST_SCENE_DEPTH=<Out> and NEXAFLOW_TEST_SCENE_DEPTH_REFERENCE=<Work>\reference) — and
    resampling_reference.py what OpenCV and PyTorch make of small pictures, for the resizes' ports (Nexaflow.Tests.IO's
    DepthResamplingTests). Upload with
      hf upload smile-forge/depth-anything-v2-small-onnx <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the published files are written.

.PARAMETER Work
    Scratch space: the virtual environment, the upstream code, the weights, the sample pictures, the reference.

.PARAMETER Python
    A Python 3.12 or later to create the virtual environment from.
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [string] $Python = 'python'
)

$ErrorActionPreference = 'Stop'

# What the published graph was made from. Change these only together with a new upload.
$Weights  = @('https://huggingface.co/depth-anything/Depth-Anything-V2-Small/resolve/03876f8651c73a60fe4c2c48294e09fcb6838fcf/depth_anything_v2_vits.pth',
              '715fade13be8f229f8a70cc02066f656f2423a59effd0579197bbf57860e1378')
$Samples  = 'https://raw.githubusercontent.com/DepthAnything/Depth-Anything-V2/a561b849ebae10a6f5ef49e26c83cbbcd36c71bf/assets/examples'
$Pictures = [ordered]@{
    'demo01.jpg' = '35ef1bbb63f6540e49aa9b6302b9b938be4fe8b9c08c07c3694b02396b0e87e0'
    'demo08.jpg' = 'd32b480349013be5f84521b0df1d6590139163aef8457f051076ed03c7371e6f'
    'demo12.jpg' = '264458adcf5af6e3733dfda7ef4628c4a1dc49ed249aa8896256d9534a8377c4'
}
$Torch    = @('torch==2.11.0', 'torchvision==0.26.0')
$Packages = @('onnx==1.23.2', 'onnx-ir==1.0.0', 'onnxscript==0.7.2', 'onnxruntime-gpu==1.31.0', 'numpy==2.5.2',
              'opencv-python-headless==4.14.0.94')
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

function Get-Checked([string] $Url, [string] $File, [string] $Sha256) {
    if (-not (Test-Path $File)) { Invoke-WebRequest -Uri $Url -OutFile $File }
    $hash = (Get-FileHash $File -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256) { throw "$File is not the published file: its hash is $hash." }
}

# The checkpoint is a plain state dict: it is checked against its hash, and read with weights_only, so nothing it names runs.
$weights = Join-Path $Work 'depth_anything_v2_vits.pth'
Get-Checked $Weights[0] $weights $Weights[1]

$pictureDir = Join-Path $Work 'pictures'
New-Item -ItemType Directory -Force $pictureDir | Out-Null
$pictures = foreach ($name in $Pictures.Keys) {
    $file = Join-Path $pictureDir $name
    Get-Checked "$Samples/$name" $file $Pictures[$name]
    $file
}

$upstream = & $py -I -X utf8 (Join-Path $Here 'upstream.py') $Work
if ($LASTEXITCODE) { throw 'Could not fetch the upstream code.' }

$build = Join-Path $Work 'build'
& $py -I -X utf8 (Join-Path $Here 'build_depth.py') $weights $upstream $build @pictures
if ($LASTEXITCODE) { throw 'The build failed.' }

& $py -I -X utf8 (Join-Path $Here 'depth_reference.py') $weights $upstream (Join-Path $Work 'reference') @pictures
if ($LASTEXITCODE) { throw 'The reference could not be made.' }

& $py -I -X utf8 (Join-Path $Here 'resampling_reference.py') (Join-Path $Work 'depth-resampling-reference.json')
if ($LASTEXITCODE) { throw 'The resampling reference could not be made.' }

$target = Join-Path $Out 'estimator'
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $build 'estimator\depth-anything-v2-small.onnx') $target -Force
# The makers' licence travels with the weights, from their repository at the commit the graph was built from.
Copy-Item (Join-Path $upstream 'LICENSE') (Join-Path $Out 'LICENSE') -Force
foreach ($file in 'README.md', 'upstream.py', 'depth_common.py', 'position_matmul.py', 'head_size.py', 'build_depth.py', 'depth_reference.py',
                  'resampling_reference.py', 'Build-SceneDepth.ps1') {
    Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force
}

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
