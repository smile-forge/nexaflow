<#
.SYNOPSIS
    Builds the French and German lexicons Nexaflow's Kokoro voices read, published at
    huggingface.co/smile-forge/kokoro-lexicons-fr-de.

.DESCRIPTION
    Kokoro reads phonemes, not text. For French and German the phonemes its voices were trained on are espeak-ng's,
    post-processed as misaki does — and espeak-ng is GPL, slow as a process per phrase, and not something Nexaflow ships.
    So it runs here, once, over the words of Mozilla Common Voice's sentences (CC0), and only its output is published:

      <Out>\fr.tsv.gz   French
      <Out>\de.tsv.gz   German

    each a gzip of "word<TAB>phonemes" lines for every word seen more than once, most used first; beside them the model
    card (README.md), build_lexicon.py and this script, so the published repository says how to rebuild itself.
    Rerunning it with the same espeak-ng reproduces the files byte for byte. Upload with
      hf upload smile-forge/kokoro-lexicons-fr-de <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the lexicons are written.

.PARAMETER Work
    Scratch space: Common Voice's French and German sentences, fetched once at the pinned commit.

.PARAMETER Espeak
    A folder holding espeak-ng 1.52.0: espeak-ng.exe and espeak-ng-data.

.PARAMETER Python
    A Python 3.10+; only its standard library is used. Git fetches the sentences.
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Work,
    [Parameter(Mandatory)] [string] $Espeak,
    [string] $Python = 'python'
)

$ErrorActionPreference = 'Stop'

# What the published lexicons were made from. Change these only together with a new upload.
$EspeakVersion = '1.52.0'
$CommonVoice   = 'https://github.com/common-voice/common-voice.git'
$Commit        = '1017ce69b1db56cf91c5b25496409fcf7ae763df'
$Keys          = 400000
$Here          = $PSScriptRoot

$exe = Join-Path $Espeak 'espeak-ng.exe'
$env:ESPEAK_DATA_PATH = Join-Path $Espeak 'espeak-ng-data'
$version = (& $exe --version) -join ' '
if ($version -notmatch [regex]::Escape($EspeakVersion)) { throw "espeak-ng $EspeakVersion is needed; $exe is: $version" }

# Only the two languages' sentences, at the commit the lexicons were built from.
$sentences = Join-Path $Work 'common-voice'
if (-not (Test-Path (Join-Path $sentences '.git'))) {
    git clone --filter=blob:none --no-checkout $CommonVoice $sentences
    if ($LASTEXITCODE) { throw "Could not fetch $CommonVoice." }
}
git -C $sentences sparse-checkout set server/data/fr server/data/de
git -C $sentences checkout --quiet $Commit
if ($LASTEXITCODE) { throw "Could not check out Common Voice at $Commit." }

New-Item -ItemType Directory -Force $Out | Out-Null
foreach ($lang in 'fr', 'de') {
    & $Python -I (Join-Path $Here 'build_lexicon.py') $lang (Join-Path $sentences 'server\data') $Espeak $Keys (Join-Path $Out "$lang.tsv.gz")
    if ($LASTEXITCODE) { throw "The $lang lexicon failed." }
}

foreach ($file in 'README.md', 'build_lexicon.py', 'Build-KokoroLexicons.ps1') { Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force }

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
