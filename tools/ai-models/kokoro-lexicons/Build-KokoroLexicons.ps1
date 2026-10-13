<#
.SYNOPSIS
    Builds the French and German lexicons Nexaflow's Kokoro voices read, published at
    huggingface.co/smile-forge/kokoro-lexicons-fr-de.

.DESCRIPTION
    Kokoro reads phonemes, not text. For French and German the phonemes its voices were trained on are espeak-ng's,
    post-processed as misaki does — and espeak-ng is GPL, slow as a process per phrase, and not something Nexaflow ships.
    So it runs here, once, over the words people actually use, and only its output is published:

      <Out>\fr.tsv.gz   the 200,000 most used French words
      <Out>\de.tsv.gz   the 400,000 most used German words

    each a gzip of "word<TAB>phonemes" lines, most used first; beside them the model card (README.md), build_lexicon.py
    and this script, so the published repository says how to rebuild itself. Rerunning it with the same espeak-ng
    reproduces the files byte for byte. Upload with
      hf upload smile-forge/kokoro-lexicons-fr-de <Out> .
    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json.

.PARAMETER Out
    Where the lexicons are written.

.PARAMETER Work
    Scratch space: the word lists, downloaded once and checked against their pinned hashes.

.PARAMETER Espeak
    A folder holding espeak-ng 1.52.0: espeak-ng.exe and espeak-ng-data.

.PARAMETER Python
    A Python 3.10+; only its standard library is used.
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
$Keys          = @{ fr = 200000; de = 400000 }
$Hermitdave    = @{
    fr = @{ Url = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/8863f1a993b7b3f1bb3beed50b95525820c3171a/content/2018/fr/fr_full.txt'; Sha256 = 'dda23c3025fc195b3a7996af26371dcf3abd1aa2cd7218dbbb7c92a49780ab0c' }
    de = @{ Url = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/072bbed282316a23651aa7068c7173aa7898cf80/content/2018/de/de_full.txt'; Sha256 = 'a5fc13f4efaaf70fd9226d11295fd3cd4c2264852ee1fad22c0abaf64acec8dc' }
}
$Leipzig       = @{
    fra_news_2023_1M      = '14b8812f80ebf91812bf9334feae2b53d101b9507d0e68bd11d30dd56861aa8f'
    fra_wikipedia_2021_1M = '73eadebaa2fe3600ebf4e41bac5241e821d7baa4c9906afc11969fb6c8aaec86'
    deu_news_2023_1M      = '392065f34be70e3612d6ef6b3edc6687b026524e57e00eef47f0528456deb1ba'
    deu_wikipedia_2021_1M = 'd5eb95fe71010a88d69ba408a4016f67a3a219e1a63cade652593abf33010aba'
}
$Here = $PSScriptRoot

$exe = Join-Path $Espeak 'espeak-ng.exe'
$env:ESPEAK_DATA_PATH = Join-Path $Espeak 'espeak-ng-data'
$version = (& $exe --version) -join ' '
if ($version -notmatch [regex]::Escape($EspeakVersion)) { throw "espeak-ng $EspeakVersion is needed; $exe is: $version" }

function Get-Pinned([string] $Url, [string] $Sha256, [string] $Path) {
    if ((Test-Path $Path) -and (Get-FileHash $Path -Algorithm SHA256).Hash -eq $Sha256) { return }
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    Invoke-WebRequest $Url -OutFile $Path
    $got = (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($got -ne $Sha256) { throw "$Url is not the pinned file: sha256 $got, expected $Sha256." }
}

$sources = Join-Path $Work 'sources'
foreach ($lang in $Hermitdave.Keys) {
    Get-Pinned $Hermitdave[$lang].Url $Hermitdave[$lang].Sha256 (Join-Path $sources "hermitdave\$($lang)_full.txt")
}
foreach ($corpus in $Leipzig.Keys) {
    $archive = Join-Path $sources "leipzig\$corpus.tar.gz"
    Get-Pinned "https://downloads.wortschatz-leipzig.de/corpora/$corpus.tar.gz" $Leipzig[$corpus] $archive
    if (-not (Test-Path (Join-Path $sources "leipzig\$corpus\$corpus-words.txt"))) {
        tar -xzf $archive -C (Join-Path $sources 'leipzig') "$corpus/$corpus-words.txt"
        if ($LASTEXITCODE) { throw "Could not extract the word list from $archive." }
    }
}

New-Item -ItemType Directory -Force $Out | Out-Null
foreach ($lang in 'fr', 'de') {
    & $Python -I (Join-Path $Here 'build_lexicon.py') $lang $sources $Espeak $Keys[$lang] (Join-Path $Out "$lang.tsv.gz")
    if ($LASTEXITCODE) { throw "The $lang lexicon failed." }
}

foreach ($file in 'README.md', 'build_lexicon.py', 'Build-KokoroLexicons.ps1') { Copy-Item (Join-Path $Here $file) (Join-Path $Out $file) -Force }

Get-ChildItem $Out -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.FullName.Substring($Out.Length + 1)
}
