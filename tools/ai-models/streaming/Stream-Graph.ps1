<#
.SYNOPSIS
    Cuts a published graph into the groups the AI host streams to a GPU a group at a time, beside the graph itself.

.DESCRIPTION
    A denoiser larger than a small card's budget runs streamed (StreamedGraph in the host): cut into groups of about
    -GroupMegabytes of 8-bit weights each, each group's weights read into pinned memory and copied to the GPU while the group
    before it runs. split_graph.py writes the groups and their manifest beside the graph; the weights stay in the graph's own
    .onnx.data, which the whole graph and its groups share, so publishing a streamed build uploads only the new small files:

        hf upload smile-forge/<repo> <folder> <folder in the repository> --include "*.group*.onnx" "*.stream.json"

    then pin the commit it makes in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json, with a part for the
    streamed build beside the whole one. The host's OwnedModel tests run the two side by side and hold the streamed build to
    the whole one's output.

.PARAMETER Graph
    The published graph to cut, its .onnx.data beside it.

.PARAMETER Work
    Scratch space: the virtual environment.

.PARAMETER GroupMegabytes
    About how many megabytes of weights each group holds: two groups' worth are on the GPU at once.

.PARAMETER Python
    A Python 3.12 to create the virtual environment from.
#>
param(
    [Parameter(Mandatory)] [string] $Graph,
    [Parameter(Mandatory)] [string] $Work,
    [double] $GroupMegabytes = 400,
    [string] $Python = 'python'
)

$ErrorActionPreference = 'Stop'

# What reads the graph. ONNX Runtime's symbolic inference types the tensors that cross between groups.
$Packages = @('onnx==1.23.2', 'onnxruntime==1.31.0', 'numpy==2.5.2', 'sympy')

New-Item -ItemType Directory -Force $Work | Out-Null
$venv = Join-Path $Work 'venv'
if (-not (Test-Path (Join-Path $venv 'Scripts\python.exe'))) {
    & $Python -I -m venv $venv
    if ($LASTEXITCODE) { throw 'Could not create the virtual environment.' }
}
$py = Join-Path $venv 'Scripts\python.exe'
& $py -I -m pip install --disable-pip-version-check @Packages
if ($LASTEXITCODE) { throw 'Could not install the splitter.' }

$Graph = (Resolve-Path $Graph).Path
& $py -I -X utf8 (Join-Path $PSScriptRoot 'split_graph.py') $Graph $GroupMegabytes
if ($LASTEXITCODE) { throw 'The split failed.' }

$folder = Split-Path $Graph
$stem = [IO.Path]::GetFileNameWithoutExtension($Graph)
Get-ChildItem $folder -File | Where-Object { $_.Name -like "$stem.group*.onnx" -or $_.Name -eq "$stem.stream.json" } | Sort-Object Name | ForEach-Object {
    '{0}  {1,12}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Length, $_.Name
}
