# What every MOSS-Image recipe step is made from, dot-sourced by each, so a comparison and a build always describe the
# same model. Change these only together with a new comparison and a new upload.

$Model    = 'OpenMOSS-Team/MOSS-Image-SFT'
$Revision = 'f972bd4b4b9e3852133cc5da954d87bc32cd8fb9'
$Torch    = @('torch==2.11.0', 'torchvision==0.26.0')
$Packages = @('diffusers==0.41.0', 'transformers==5.19.0', 'accelerate==1.15.0', 'safetensors==0.8.0', 'onnx==1.23.2',
              'onnx-ir==1.0.0', 'onnxscript==0.7.2', 'onnxruntime-gpu==1.31.0', 'numpy==2.5.2', 'pillow==12.3.0', 'huggingface_hub==1.33.0',
                            'nvidia-ml-py==13.615.71')

# MOSS-Image's text encoder is Qwen/Qwen3.5-9B unchanged; these are its llama.cpp builds at the revision the catalog's
# Qwen3.5-9B chat model is pinned to, and the llama.cpp build the host runs them with.
$Gguf         = 'unsloth/Qwen3.5-9B-GGUF'
$GgufRevision = '3885219b6810b007914f3a7950a8d1b469d598a5'
$GgufFiles    = [ordered]@{
    'Qwen3.5-9B-BF16.gguf'   = 'daebe40eeea7057c1cdf35ac56d13f507d8bf12171bbb7a6b6b0d3f05439159a'
    'Qwen3.5-9B-Q8_0.gguf'   = '809626574d0cb43d4becfa56169980da2bb448f2299270f7be443cb89d0a6ae4'
    'Qwen3.5-9B-Q6_K.gguf'   = '91898433cf5ce0a8f45516a4cc3e9343b6e01d052d01f684309098c66a326c59'
    'Qwen3.5-9B-Q4_K_M.gguf' = '03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8'
}
$LlamaCpp        = 'b11606'
$LlamaCppArchives = [ordered]@{
    "llama-$LlamaCpp-bin-win-cuda-13.4-x64.zip" = '93ba7c16d1ad8343248d540bb4c06e7e18f7d0601e762d6952bad13628295be8'
    'cudart-llama-bin-win-cuda-13.4-x64.zip'    = '738f8c251ac22b70c3ae6f83a10cf222725df0395246a2cf58f32bdb85fbe668'
}

# The virtual environment and the checkpoint every step shares under its -Work folder.
function Initialize-MossWork([string] $Work, [string] $Python, [string] $Checkpoint) {
    New-Item -ItemType Directory -Force $Work | Out-Null
    $venv = Join-Path $Work 'venv'
    if (-not (Test-Path (Join-Path $venv 'Scripts\python.exe'))) {
        & $Python -I -m venv $venv | Out-Host
        if ($LASTEXITCODE) { throw 'Could not create the virtual environment.' }
    }
    $py = Join-Path $venv 'Scripts\python.exe'
    # Everything run here is sent to the host: the function's output is the setup it hands back, nothing else.
    & $py -I -m pip install --disable-pip-version-check --index-url https://download.pytorch.org/whl/cu128 @Torch | Out-Host
    if ($LASTEXITCODE) { throw 'Could not install PyTorch.' }
    & $py -I -m pip install --disable-pip-version-check @Packages | Out-Host
    if ($LASTEXITCODE) { throw 'Could not install the builder.' }

    if (-not $Checkpoint) {
        $Checkpoint = Join-Path $Work 'checkpoint'
        & $py -I -c "import sys; from huggingface_hub import snapshot_download; snapshot_download(sys.argv[1], revision=sys.argv[2], local_dir=sys.argv[3], ignore_patterns=['assets/*'])" $Model $Revision $Checkpoint | Out-Host
        if ($LASTEXITCODE) { throw "Could not download $Model at $Revision." }
    }
    return @{ Python = $py; Checkpoint = $Checkpoint }
}

# A file fetched once into $Folder and held to its SHA-256.
function Get-PinnedFile([string] $Url, [string] $Folder, [string] $Name, [string] $Sha256) {
    New-Item -ItemType Directory -Force $Folder | Out-Null
    $path = Join-Path $Folder $Name
    if (-not (Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash -ne $Sha256.ToUpperInvariant()) {
        & curl.exe -L --fail --retry 3 -o $path $Url | Out-Host
        if ($LASTEXITCODE) { throw "Could not download $Url." }
        if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $Sha256.ToUpperInvariant()) { throw "$Name is not the file pinned." }
    }
    return $path
}
