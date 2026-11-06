<#
.SYNOPSIS
    Writes a published graph's CPU build beside it, for a machine whose GPU's budget does not hold the graph.

.DESCRIPTION
    A prompt encoder quantised for the GPU (8-bit MatMulNBits weights, float inputs) runs on the CPU as built, but slowly: each
    product unpacks its weights every time. retag_for_cpu.py writes <stem>.cpu.onnx beside the graph, the same graph reading its
    weights with 8-bit inputs — fifty times faster on the CPU — sharing the graph's .onnx.data, so publishing it uploads one
    small file:

        hf upload smile-forge/<repo> <folder> <folder in the repository> --include "*.cpu.onnx"

    then add a CPU part beside the CUDA one in src/Nexaflow.Providers/Nexaflow.Providers.AiHost/owned-models.json, pinned at the
    commit it makes. Not every graph keeps its reading with 8-bit inputs: the AI host's OwnedModel tests hold each CPU build
    to its graph's own output before it is offered.

.PARAMETER Graph
    The published graph, its .onnx.data beside it.

.PARAMETER Work
    Scratch space: the virtual environment.

.PARAMETER Python
    A Python 3.12 to create the virtual environment from.
#>
param(
    [Parameter(Mandatory)] [string] $Graph,
    [Parameter(Mandatory)] [string] $Work,
    [string] $Python = 'python'
)

$ErrorActionPreference = 'Stop'

$Packages = @('onnx==1.23.2')

New-Item -ItemType Directory -Force $Work | Out-Null
$venv = Join-Path $Work 'venv'
if (-not (Test-Path (Join-Path $venv 'Scripts\python.exe'))) {
    & $Python -I -m venv $venv
    if ($LASTEXITCODE) { throw 'Could not create the virtual environment.' }
}
$py = Join-Path $venv 'Scripts\python.exe'
& $py -I -m pip install --disable-pip-version-check @Packages
if ($LASTEXITCODE) { throw 'Could not install onnx.' }

$Graph = (Resolve-Path $Graph).Path
& $py -I -X utf8 (Join-Path $PSScriptRoot 'retag_for_cpu.py') $Graph
if ($LASTEXITCODE) { throw 'The CPU build failed.' }

$built = Join-Path (Split-Path $Graph) ([IO.Path]::GetFileNameWithoutExtension($Graph) + '.cpu.onnx')
'{0}  {1,12}  {2}' -f (Get-FileHash $built -Algorithm SHA256).Hash.ToLowerInvariant(), (Get-Item $built).Length, (Split-Path $built -Leaf)
