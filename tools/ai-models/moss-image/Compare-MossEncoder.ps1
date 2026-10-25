<#
.SYNOPSIS
    Compares the ways Nexaflow could read a prompt for MOSS-Image — its text encoder, Qwen3.5-9B, in PyTorch, and the
    same weights in llama.cpp at bf16, 8, 6 and 4 bits — by their hidden states, the GPU memory each takes, and the
    pictures each draws.

.DESCRIPTION
    MOSS-Image's text encoder is Qwen/Qwen3.5-9B unchanged, a model the host already runs on llama.cpp. Before the host
    reads MOSS-Image's prompts that way, compare_moss_encoder.py holds every llama.cpp build against PyTorch: their
    readings of fourteen prompts against fp32, and the pictures MOSS-Image's own pipeline draws from each against those
    it draws from its own bf16 reading. Writes <Work>\encoder-comparison\encoder-comparison.json and every picture.

.PARAMETER Work
    Scratch space shared with the build: the virtual environment, the checkpoint, the GGUFs, llama.cpp, the results.

.PARAMETER Python
    A Python 3.12 or 3.13 to create the virtual environment from.

.PARAMETER Checkpoint
    A folder already holding the checkpoint at the pinned revision, instead of downloading it.
#>
param(
    [Parameter(Mandatory)] [string] $Work,
    [string] $Python = 'python',
    [string] $Checkpoint
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MossImage.Pins.ps1')

$setup = Initialize-MossWork $Work $Python $Checkpoint

$llama = Join-Path $Work "llama.cpp-$LlamaCpp"
foreach ($archive in $LlamaCppArchives.Keys) {
    $zip = Get-PinnedFile "https://github.com/ggml-org/llama.cpp/releases/download/$LlamaCpp/$archive" (Join-Path $Work 'downloads') $archive $LlamaCppArchives[$archive]
    Expand-Archive $zip $llama -Force
}
$server = Join-Path $llama 'llama-server.exe'
if (-not (Test-Path $server)) { throw "llama.cpp $LlamaCpp has no llama-server.exe." }

$ggufs = foreach ($file in $GgufFiles.Keys) {
    Get-PinnedFile "https://huggingface.co/$Gguf/resolve/$GgufRevision/$file" (Join-Path $Work 'gguf') $file $GgufFiles[$file]
}

& $setup.Python -I -X utf8 (Join-Path $PSScriptRoot 'compare_moss_encoder.py') $setup.Checkpoint $server (Join-Path $Work 'encoder-comparison') @ggufs
if ($LASTEXITCODE) { throw 'The comparison failed.' }
