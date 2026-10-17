<#
.SYNOPSIS
    Builds what Nexaflow's page reading runs — Baidu's Unlimited-OCR as GGUF for llama.cpp, and PaddleOCR's PP-OCRv5
    mobile text detector as ONNX — published at huggingface.co/smile-forge/unlimited-ocr.

.DESCRIPTION
    Nexaflow converts the makers' weights once, offline, and publishes the result; the app never converts anything on a
    user's machine. Every input is pinned: Unlimited-OCR at a Hugging Face revision, checked by hash; llama.cpp's
    converter at the commit of the release the AI host runs (b11606), and that release's quantizer, checked by the hash
    the host's project pins; PP-OCRv5's detector at a Hugging Face revision, checked by hash.

      <Out>\reader\unlimited-ocr-q8_0.gguf          the language model, 8-bit
      <Out>\reader\unlimited-ocr-mmproj-f16.gguf    the vision encoder and projector, fp16
      <Out>\text-finder\pp-ocrv5-mobile-det.onnx    the text detector (fp32, opset 17)

    with the model card (README.md), both licences and this recipe beside them. build_text_finder.py holds the detector
    to Paddle's own predictor on the sample pages. page_reference.py then runs the makers' own transformers code on those
    pages, and text_finder_reference.py Paddle's detector, into <Work>\reference — what the AI host is held to. Upload with
      hf upload smile-forge/unlimited-ocr <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the published files are written.

.PARAMETER Work
    Scratch space: the virtual environments, the upstream code and weights, the sample pages, the reference.

.PARAMETER Python
    A Python 3.12 or later, with an NVIDIA GPU for the makers' code, for the converter and the reference.

.PARAMETER DetectorPython
    A Python 3.11, which Paddle 3.1.1 and paddle2onnx 2.1.0 are published for.
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [string] $Python = 'python',
    [Parameter(Mandatory)] [string] $DetectorPython
)

$ErrorActionPreference = 'Stop'

# What the published files were made from. Change these only together with a new upload.
$Reader = 'https://huggingface.co/baidu/Unlimited-OCR/resolve/8249d20b2822a7d8a4c22c8d8c9069b5ac36bda3'
$ReaderFiles = [ordered]@{
    'model.safetensors'            = '2bc48a7a110061ea58fff65d3169367eebe3aee371ca6968dc2219c1b2855fc6'
    'config.json'                  = '55e6c5179eea054a3fca99e3e41424649d22f574fba4c5f0737ef9c9095526e4'
    'generation_config.json'       = 'dc97b67afaad12b72320c4230f292be8cb5a73fcb4caa9072074fe5a341fb003'
    'tokenizer.json'               = 'a02f8fd5228c90256bb4f6554c34a579d48f909e5beb232dc4afad870b55a8b4'
    'tokenizer_config.json'        = 'a0cbe8464049da1f891b7a12676de06af4cb54c130995d42f71adc1c30c6e9f3'
    'special_tokens_map.json'      = 'ab4bd57ce17d62e39e0a39e739de1e407484f090f0b2c7e391312bca7a5b061a'
    'chat_template.jinja'          = '690dcec206a691b4876d70db7092b6b0ae14cb8b51396e4c89a25b777f5a6830'
    'preprocessor_config.json'     = '62cef9e2db4ae494ff2ecccaedfeef884d8ba880c25d17f14e81f3d1c3c899cb'
    'processor_config.json'        = '2c82d81788aac5146db01d842138e8f5921daa907d2fd61c3c6c757d41dfb1d7'
    'configuration_deepseek_v2.py' = 'b8470dd616ba8745fce6e27b093aef73a098863cc891b2477dcf9326a36000f7'
    'conversation.py'              = 'ec7b6ce89bcda643de1f43269ffa66a7b2e65dc3ed30e427958f776546b4ba03'
    'deepencoder.py'               = '0ae2fb6d1e5ae8cf100fc32f854830acd08c821a0a1f23a94a76588c222ddcf2'
    'modeling_deepseekv2.py'       = '74e36e6bd0ba7bc565ef76464a99baa8e6bccb710ae9c1007b54ac30b855fa4c'
    'modeling_unlimitedocr.py'     = '3894696d68197ad00e89f0963880645dfce49b1933a9f27e78fbc05983547a78'
    'LICENSE'                      = 'd985048c6d69429d685fdbe7557340aa0897c0fd8dc038299b148b9c75dc3383'
    'Unlimited-OCR.pdf'            = 'd4cc0b2e98f53d9165e63af925519cccbf80ecc6f047973e3f9b2bdd84474a8b'
}
$Detector = 'https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det/resolve/0d63e78e2b680928f6b1747d76a08db6e645efb7'
$DetectorFiles = [ordered]@{
    'inference.json'      = '05feef1acb00aa4cd7362b15f7f501fc4f99d7b1fa73c1c871e0c7b1504b0f5c'
    'inference.pdiparams' = 'afa1820cb16c1fd0dad589d0f8b389139061c1ef6d68019685fd07be997dda5b'
    'inference.yml'       = '98069072e1b6b37d727fd9d9f11725faa46d6ea0de012f2ed26caea011c37699'
}
$DetectorLicence = @('https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/dab3fe35379033fdcb2d0e9572fac0b36c9a9ebf/LICENSE',
                     '3840c5c0c61c294264d2dd77b8777be6ddd90121ef4e0e64abcd22edea581d6e')
# llama.cpp as the AI host runs it: the converter at release b11606's commit, the quantizer from that release's CPU
# build, checked by the hash Nexaflow.AiHost.csproj pins.
$LlamaCpp = @('https://github.com/ggml-org/llama.cpp', '1b70350c92563449576bcbbf552994b1d1ed847d')
$LlamaBuild = @('https://github.com/ggml-org/llama.cpp/releases/download/b11606/llama-b11606-bin-win-cpu-x64.zip',
                '8c0f4f2a1b808eebe00e738edf027cceea1aa9e6c883618bd37151666093f1b4')
$Torch    = @('torch==2.11.0', 'torchvision==0.26.0')
$Packages = @('transformers==4.57.1', 'tokenizers==0.22.2', 'safetensors==0.8.0', 'sentencepiece==0.2.2', 'numpy==2.5.2',
              'pillow==12.3.0', 'einops==0.8.2', 'addict==2.4.0', 'easydict==1.13', 'matplotlib==3.11.2', 'pymupdf==1.28.2',
              'protobuf==7.36.2', 'jinja2==3.1.6')
$DetectorPackages = @('paddlepaddle==3.1.1', 'paddle2onnx==2.1.0', 'onnxruntime==1.31.0', 'numpy==2.4.6',
                      'opencv-python-headless==5.0.0.93', 'pillow==12.3.0')
$Here = $PSScriptRoot

function Get-Pinned([string] $Url, [string] $File, [string] $Sha256) {
    if (-not (Test-Path $File)) { Invoke-WebRequest -Uri $Url -OutFile $File }
    if ($Sha256) {
        $hash = (Get-FileHash $File -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $Sha256) { throw "$File is not the pinned file: its hash is $hash." }
    }
}

function New-Venv([string] $From, [string] $Folder) {
    if (-not (Test-Path (Join-Path $Folder 'Scripts\python.exe'))) {
        & $From -I -m venv $Folder
        if ($LASTEXITCODE) { throw "Could not create $Folder." }
    }
    Join-Path $Folder 'Scripts\python.exe'
}

New-Item -ItemType Directory -Force $Out, $Work, (Join-Path $Out 'reader'), (Join-Path $Out 'text-finder') | Out-Null

$py = New-Venv $Python (Join-Path $Work 'venv')
& $py -I -m pip install --disable-pip-version-check --index-url https://download.pytorch.org/whl/cu128 @Torch
if ($LASTEXITCODE) { throw 'Could not install PyTorch.' }
& $py -I -m pip install --disable-pip-version-check @Packages
if ($LASTEXITCODE) { throw 'Could not install the converter''s packages.' }
$detectorPy = New-Venv $DetectorPython (Join-Path $Work 'detector-venv')
& $detectorPy -I -m pip install --disable-pip-version-check @DetectorPackages
if ($LASTEXITCODE) { throw 'Could not install Paddle.' }

# The makers' weights, and Paddle's detector, checked against their hashes before anything reads them.
$readerDir = Join-Path $Work 'unlimited-ocr'
$detectorDir = Join-Path $Work 'pp-ocrv5-mobile-det'
New-Item -ItemType Directory -Force $readerDir, $detectorDir | Out-Null
foreach ($name in $ReaderFiles.Keys) { Get-Pinned "$Reader/$name" (Join-Path $readerDir $name) $ReaderFiles[$name] }
foreach ($name in $DetectorFiles.Keys) { Get-Pinned "$Detector/$name" (Join-Path $detectorDir $name) $DetectorFiles[$name] }
Get-Pinned $DetectorLicence[0] (Join-Path $Work 'PaddleOCR-LICENSE') $DetectorLicence[1]

$llama = Join-Path $Work 'llama.cpp'
if (-not (Test-Path (Join-Path $llama '.git'))) {
    git init -q $llama
    git -C $llama remote add origin $LlamaCpp[0]
}
git -C $llama fetch -q --depth 1 origin $LlamaCpp[1]
git -C $llama checkout -q --force $LlamaCpp[1]
if ($LASTEXITCODE) { throw 'Could not fetch llama.cpp.' }
$llamaBin = Join-Path $Work 'llama-bin'
$llamaZip = Join-Path $Work 'llama-bin.zip'
Get-Pinned $LlamaBuild[0] $llamaZip $LlamaBuild[1]
Expand-Archive -LiteralPath $llamaZip -DestinationPath $llamaBin -Force

# The language model to bf16 and then 8-bit — on the sample pages 4-bit read the same, but in llama.cpp's own tests
# 4- and 5-bit builds gave nothing back for a page of three lines; the vision tower and projector to fp16.
$bf16 = Join-Path $Work 'unlimited-ocr-bf16.gguf'
$reader = Join-Path $Out 'reader\unlimited-ocr-q8_0.gguf'
$projector = Join-Path $Out 'reader\unlimited-ocr-mmproj-f16.gguf'
$env:PYTHONPATH = Join-Path $llama 'gguf-py'
& $py -X utf8 (Join-Path $llama 'convert_hf_to_gguf.py') $readerDir --outtype bf16 --outfile $bf16
if ($LASTEXITCODE) { throw 'The language model could not be converted.' }
& $py -X utf8 (Join-Path $llama 'convert_hf_to_gguf.py') $readerDir --mmproj --outtype f16 --outfile $projector
if ($LASTEXITCODE) { throw 'The vision encoder could not be converted.' }
$q8 = Join-Path $Work 'unlimited-ocr-q8_0.gguf'
& (Join-Path $llamaBin 'llama-quantize.exe') $bf16 $q8 Q8_0
if ($LASTEXITCODE) { throw 'The language model could not be quantized.' }
# The converter writes a template of its own that prints a turn's content as it is — a picture's turn, given as parts,
# comes out as a list. chat_template.jinja is the makers' own, after the start token their code puts first.
& $py -X utf8 (Join-Path $llama 'gguf-py\gguf\scripts\gguf_new_metadata.py') $q8 $reader --chat-template-file (Join-Path $Here 'chat_template.jinja') --force
if ($LASTEXITCODE) { throw 'The chat template could not be set.' }
Remove-Item Env:\PYTHONPATH

$reference = Join-Path $Work 'reference'
$samples = Join-Path $reference 'pages'
Push-Location $Here
try {
    & $py -I -X utf8 (Join-Path $Here 'make_samples.py') (Join-Path $readerDir 'Unlimited-OCR.pdf') $samples "$env:WINDIR\Fonts\arial.ttf"
    if ($LASTEXITCODE) { throw 'The sample pages could not be made.' }
    & $detectorPy -X utf8 (Join-Path $Here 'build_text_finder.py') $detectorDir (Join-Path $Out 'text-finder\pp-ocrv5-mobile-det.onnx') $samples
    if ($LASTEXITCODE) { throw 'The text finder could not be built.' }
    & $detectorPy -X utf8 (Join-Path $Here 'text_finder_reference.py') $detectorDir $samples (Join-Path $reference 'text-finder')
    if ($LASTEXITCODE) { throw 'The text finder''s reference could not be made.' }
    & $py -X utf8 (Join-Path $Here 'page_reference.py') $readerDir $samples (Join-Path $reference 'reader')
    if ($LASTEXITCODE) { throw 'The reader''s reference could not be made.' }
}
finally { Pop-Location }

Copy-Item (Join-Path $readerDir 'LICENSE') (Join-Path $Out 'LICENSE') -Force
Copy-Item (Join-Path $Work 'PaddleOCR-LICENSE') (Join-Path $Out 'LICENSE-PaddleOCR') -Force
foreach ($file in 'README.md', 'Build-PageReading.ps1', 'chat_template.jinja', 'make_samples.py', 'text_finding.py', 'build_text_finder.py',
                  'text_finder_reference.py', 'page_reference.py') {
    Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force
}

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
