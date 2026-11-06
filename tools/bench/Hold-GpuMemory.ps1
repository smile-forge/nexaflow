<#
.SYNOPSIS
  Holds a GPU's memory so the AI host finds it as a smaller card would leave it: run it, then start the app or a test,
  and the host's budget is what a card of -CardGb gives. Run it by hand; it holds until Enter is pressed, or for
  -Seconds.

.DESCRIPTION
  Allocates device memory through the CUDA runtime in the CUDA pack, 256 MiB at a time, until what is free on the GPU
  is what a card of -CardGb leaves once its desktop holds -DesktopGb — the free bytes the host reads at start and
  budgets from. What it holds is real device memory: anything the host loads past its budget fails as it would on that
  card.

  It stands in for the host's view only. The app's own guess before the host runs — which models a machine is offered —
  is read from the adapter's dedicated memory, which this cannot change.

.PARAMETER CardGb
  The card to stand in for, in GiB of dedicated memory.

.PARAMETER DesktopGb
  What the desktop and the programs around it hold on that card.

.PARAMETER Device
  The CUDA device to hold.

.PARAMETER Pack
  The CUDA pack folder holding cudart64_13.dll. Defaults to the newest under the app's AI folder.

.PARAMETER Seconds
  How long to hold before letting go; 0 holds until Enter is pressed.
#>
param(
    [Parameter(Mandatory)][double]$CardGb,
    [double]$DesktopGb = 1,
    [int]$Device = 0,
    [string]$Pack,
    [int]$Seconds = 0
)
$ErrorActionPreference = 'Stop'

if (-not $Pack) {
    $runtime = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Smile\nexaflow\ai\runtime'
    $Pack = Get-ChildItem $runtime -Directory -Filter 'cuda-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $Pack) { throw "No CUDA pack under $runtime; pass -Pack." }
}
$cudart = Join-Path $Pack 'cudart64_13.dll'
if (-not (Test-Path $cudart)) { throw "$cudart is not there." }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CudaHold
{
    [DllImport("cudart64_13.dll")] public static extern int cudaSetDevice(int device);
    [DllImport("cudart64_13.dll")] public static extern int cudaMemGetInfo(out UIntPtr free, out UIntPtr total);
    [DllImport("cudart64_13.dll")] public static extern int cudaMalloc(out IntPtr pointer, UIntPtr bytes);
    [DllImport("cudart64_13.dll")] public static extern int cudaFree(IntPtr pointer);
}
'@
[void][Runtime.InteropServices.NativeLibrary]::Load($cudart)

function Check([int]$status, [string]$what) { if ($status -ne 0) { throw "$what failed: CUDA error $status" } }
function Free { $f = [UIntPtr]::Zero; $t = [UIntPtr]::Zero; Check ([CudaHold]::cudaMemGetInfo([ref]$f, [ref]$t)) 'cudaMemGetInfo'; [pscustomobject]@{ Free = [long]$f.ToUInt64(); Total = [long]$t.ToUInt64() } }

Check ([CudaHold]::cudaSetDevice($Device)) 'cudaSetDevice'
$GiB = [long]1 -shl 30
$chunk = [long]256 -shl 20
$leave = [long](($CardGb - $DesktopGb) * $GiB)
$start = Free
if ($start.Free -le $leave) { throw ('GPU {0} has {1:N2} GiB free already, no more than a {2} GiB card would leave.' -f $Device, ($start.Free / $GiB), $CardGb) }

$held = [Collections.Generic.List[IntPtr]]::new()
while ((Free).Free - $chunk -ge $leave) {
    $p = [IntPtr]::Zero
    Check ([CudaHold]::cudaMalloc([ref]$p, [UIntPtr]::new([uint64]$chunk))) 'cudaMalloc'
    $held.Add($p)
}
$now = Free
'GPU {0}: {1:N2} GiB of {2:N2} free — as a {3} GiB card with {4} GiB on its desktop leaves it — holding {5:N2} GiB in {6} blocks.' -f `
    $Device, ($now.Free / $GiB), ($now.Total / $GiB), $CardGb, $DesktopGb, ($held.Count * $chunk / $GiB), $held.Count
try {
    if ($Seconds -gt 0) { Start-Sleep -Seconds $Seconds } else { [void](Read-Host 'Enter lets it go') }
}
finally {
    foreach ($p in $held) { [void][CudaHold]::cudaFree($p) }
    'Let go.'
}
