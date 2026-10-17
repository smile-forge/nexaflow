<#
.SYNOPSIS
    Builds the ONNX graph of big-lama, LaMa's inpainter, that Nexaflow's eraser fills what a person rubs out of a photo
    with, published at huggingface.co/smile-forge/big-lama-onnx.

.DESCRIPTION
    No ONNX build of big-lama that runs on ONNX Runtime's CUDA provider is published, so Nexaflow makes its own — once,
    offline, on a machine with an NVIDIA GPU — and publishes the result; the app never converts anything on a user's
    machine. upstream.py fetches LaMa's code at a pinned commit, the makers' weights are downloaded from the Hugging Face
    copy their README names and checked by hash, and build_lama.py traces the generator into a graph, holding it to the
    original as it goes:

      <Out>\inpainter     big-lama, fp32, for CPU and CUDA alike

    with the model card (README.md) and this recipe beside it. lama_reference.py then runs the makers' own predict on
    SAM 3's sample pictures with a drawn mask each into <Work>\reference — what the AI host's port is held to
    (Nexaflow.Tests.AiHost's PictureErasingTests, with NEXAFLOW_TEST_LAMA=<Out> and
    NEXAFLOW_TEST_LAMA_REFERENCE=<Work>\reference). Upload with
      hf upload smile-forge/big-lama-onnx <Out> .
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
$Weights  = @('https://huggingface.co/smartywu/big-lama/resolve/05cb2be7f8dbe6ca7c6e78f4fc827a4b2baaa4a9/big-lama.zip',
              'f1b358ca24093b93a106183b98a3dea6e8ed09f3b43ea7251eb2c81e7b4575f6')
$Samples  = 'https://raw.githubusercontent.com/facebookresearch/sam3/7f473cfd983d5eb43040ff608f66bff0d7bdcb53/assets/images'
$Pictures = [ordered]@{
    'groceries.jpg'  = '7073dfecb5a3ecafb6152124113163a0ea1c1c70f92999ec892b519eca63e3d3'
    'test_image.jpg' = '979f120edcb0050a12d5b4a1f1eaf6bc888b89f675524e7ffcf6ae5b77aa6bc4'
    'truck.jpg'      = '941715e721c8864324a1425b445ea4dde0498b995c45ddce0141a58971c6ff99'
}
$Torch    = @('torch==2.11.0', 'torchvision==0.26.0')
$Packages = @('onnx==1.23.2', 'onnx-ir==1.0.0', 'onnxscript==0.7.2', 'onnxruntime-gpu==1.31.0', 'numpy==2.5.2',
              'pillow==12.3.0', 'omegaconf==2.3.0', 'pyyaml')
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

# The checkpoint is a pickle: it is checked against its hash before anything reads it, and read with an unpickler that
# keeps tensors alone (lama_common.py).
$zip = Join-Path $Work 'big-lama.zip'
Get-Checked $Weights[0] $zip $Weights[1]
$model = Join-Path $Work 'big-lama'
if (-not (Test-Path (Join-Path $model 'models\best.ckpt'))) { Expand-Archive $zip $Work -Force }

$pictureDir = Join-Path $Work 'pictures'
New-Item -ItemType Directory -Force $pictureDir | Out-Null
$pictures = foreach ($name in $Pictures.Keys) {
    $file = Join-Path $pictureDir $name
    Get-Checked "$Samples/$name" $file $Pictures[$name]
    $file
}

$shim = & $py -I -X utf8 (Join-Path $Here 'upstream.py') $Work
if ($LASTEXITCODE) { throw 'Could not fetch the upstream code.' }

$build = Join-Path $Work 'build'
& $py -I -X utf8 (Join-Path $Here 'build_lama.py') $model $shim $build @pictures
if ($LASTEXITCODE) { throw 'The build failed.' }

& $py -I -X utf8 (Join-Path $Here 'lama_reference.py') $model $shim (Join-Path $Work 'reference') @pictures
if ($LASTEXITCODE) { throw 'The reference could not be made.' }

$target = Join-Path $Out 'inpainter'
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $build 'inpainter\inpainter.onnx') $target -Force
# LaMa's licence travels with the weights, from the makers' repository at the commit the graph was built from.
Copy-Item (Join-Path $Work 'src\lama\LICENSE') (Join-Path $Out 'LICENSE') -Force
foreach ($file in 'README.md', 'upstream.py', 'lama_common.py', 'fourier_matmul.py', 'build_lama.py',
                  'lama_reference.py', 'Build-Lama.ps1') {
    Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force
}

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
