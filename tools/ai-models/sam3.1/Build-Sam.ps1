<#
.SYNOPSIS
    Builds the ONNX graphs of SAM 3.1 that Nexaflow's selection tool picks things out of pictures with — by clicks, a
    box or a phrase — published at huggingface.co/smile-forge/sam3.1-onnx.

.DESCRIPTION
    No ONNX build of SAM 3.1 is published by its makers, so Nexaflow makes its own — once, offline, on a machine with an
    NVIDIA GPU — and publishes the result; the app never converts anything on a user's machine. The makers' code is
    fetched at a pinned commit; the checkpoint is the makers' own, from a public copy at a pinned revision (Meta's
    repository asks for a form first), checked by hash; and build_sam.py traces each path into graphs, holding them to
    the makers' own paths on their own sample pictures, on CUDA and on the CPU:

      <Out>\image-encoder      the picture, once (fp16)
      <Out>\click-decoder      clicks and boxes to a mask (fp32)
      <Out>\text-encoder       a phrase (fp16)
      <Out>\exemplar-encoder   example boxes, "like this" and "not like this" (fp32)
      <Out>\finder             everything a phrase and its examples name (fp16)
      <Out>\tokenizer          the makers' byte-pair vocabulary

    with the model card (README.md), the SAM License and this recipe beside them. sam_reference.py then runs the makers'
    own paths into <Work>\reference — what the AI host's port is held to. Upload with
      hf upload smile-forge/sam3.1-onnx <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the published files are written.

.PARAMETER Work
    Scratch space: the virtual environment, the upstream code, the checkpoint, the reference.

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
$Upstream = @('https://github.com/facebookresearch/sam3', '7f473cfd983d5eb43040ff608f66bff0d7bdcb53')
$Mirror   = 'https://huggingface.co/research21/sam3.1/resolve/3bd4a45137d9db4cb21c0119e7eb67deb69e339f'
$Weights  = [ordered]@{
    # the same file, byte for byte, as facebook/sam3.1's
    'sam3.1_multiplex.pt' = '0567debeec80ba4ac6369540c6c248025283cb3ff2b92827509e57e2b3541cb6'
    'LICENSE'             = 'bec48f70bd37bf8280a9d1ebf01642d26086f6122ba735baa08fe03c5a6e7448'
}
$Torch    = @('torch==2.11.0', 'torchvision==0.26.0')
$Packages = @('onnx==1.23.2', 'onnx-ir==1.0.0', 'onnxscript==0.7.2', 'onnxruntime-gpu==1.31.0', 'numpy==2.5.2', 'pillow==12.3.0',
              'timm==1.0.30', 'einops==0.8.2', 'ftfy==6.1.1', 'regex==2026.9.29', 'iopath==0.1.10', 'huggingface_hub==2.2.0',
              'triton-windows==3.8.0.post29', 'setuptools==78.1.0', 'psutil==7.2.2', 'scipy==1.18.1', 'tqdm==4.70.1', 'pycocotools==2.0.11')
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

# The makers' code, at its pinned commit, installed without its dependency pins (numpy < 2 among them), which the
# packages above stand in for.
$src = Join-Path $Work 'sam3'
if (-not (Test-Path (Join-Path $src '.git'))) {
    git init -q $src
    git -C $src remote add origin $Upstream[0]
}
git -C $src fetch -q --depth 1 origin $Upstream[1]
git -C $src checkout -q --force $Upstream[1]
if ($LASTEXITCODE) { throw 'Could not fetch the makers'' code.' }
& $py -I -m pip install --disable-pip-version-check --no-deps $src
if ($LASTEXITCODE) { throw 'Could not install the makers'' code.' }

# The checkpoint is a pickle: it is checked against its hash before anything loads it, and loaded weights-only.
$weightsDir = Join-Path $Work 'weights'
New-Item -ItemType Directory -Force $weightsDir | Out-Null
foreach ($name in $Weights.Keys) {
    $file = Join-Path $weightsDir $name
    if (-not (Test-Path $file)) { Invoke-WebRequest -Uri "$Mirror/$name" -OutFile $file }
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $Weights[$name]) { throw "$name is not the makers' file: its hash is $hash." }
}
$checkpoint = Join-Path $weightsDir 'sam3.1_multiplex.pt'
$pictures = Join-Path $src 'assets\images'

Push-Location $Here
try {
    & $py -X utf8 (Join-Path $Here 'build_sam.py') $checkpoint $pictures $Out
    if ($LASTEXITCODE) { throw 'The build failed.' }
    & $py -X utf8 (Join-Path $Here 'sam_reference.py') $checkpoint $pictures (Join-Path $Work 'reference')
    if ($LASTEXITCODE) { throw 'The reference could not be made.' }
}
finally { Pop-Location }

$tokenizer = Join-Path $Out 'tokenizer'
New-Item -ItemType Directory -Force $tokenizer | Out-Null
Copy-Item (Join-Path $src 'sam3\assets\bpe_simple_vocab_16e6.txt.gz') $tokenizer -Force
Copy-Item (Join-Path $weightsDir 'LICENSE') (Join-Path $Out 'LICENSE') -Force
foreach ($file in 'README.md', 'sam_graphs.py', 'build_sam.py', 'sam_reference.py', 'Build-Sam.ps1') {
    Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force
}

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
