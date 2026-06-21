using Nexaflow.Providers.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Intrinsics = System.Runtime.Intrinsics.X86;

namespace Nexaflow.Core.Services;

/// <summary>Native backend Whisper should run on, given the detected hardware.</summary>
public enum WhisperBackend { Cpu, Cuda }

/// <summary>
/// Result of a one-shot host probe. CPU flags come from System.Runtime.Intrinsics
/// (+ a CPUID stub for F16C); GPU data is parsed from <c>nvidia-smi</c>.
/// </summary>
public sealed record CapabilityReport(
    bool   Avx,
    bool   Avx2,
    bool   Fma,
    bool   F16C,
    bool   CudaAvailable,
    int    CudaMajorVersion,
    string? GpuName,
    double GpuComputeCapability,
    int    GpuVramMb,
    int    TotalRamMb,
    bool   CanRunWhisper,
    WhisperBackend RecommendedBackend);

/// <summary>
/// Probes CPU/GPU capabilities once in the background and caches the result.
/// Used to decide whether Whisper voice input can be enabled and on which backend.
/// Thread-safe and non-blocking; <see cref="CapabilitiesReady"/> fires on the UI thread.
/// </summary>
public sealed class HostCapabilityService : IHostCapabilityService
{
    public static HostCapabilityService Instance { get; } = new();
    private HostCapabilityService() { }

    /// <summary>RTX 2070 (Turing) compute capability — the "2070 or later" floor.</summary>
    private const double MinComputeCapability = 7.5;
    /// <summary>Minimum VRAM to even consider the CUDA backend (smallest model).</summary>
    private const int    MinCudaVramMb = 2048;

    /// <summary>The app UI dispatcher, captured when the singleton is built on the UI thread (App.InitializeApp).</summary>
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    private int _started;   // 0 = not started, 1 = started (interlocked guard)

    public CapabilityReport? Report { get; private set; }
    public bool IsReady => Report is not null;

    /// <summary>The Providers.Common view of the probe — the LLM-relevant subset, no Whisper fields.</summary>
    HostCapabilities? IHostCapabilityService.Report => Report is { } r
        ? new HostCapabilities(r.Avx, r.Avx2, r.Fma, r.F16C, r.CudaAvailable, r.CudaMajorVersion,
                               r.GpuName, r.GpuComputeCapability, r.GpuVramMb, r.TotalRamMb)
        : null;

    /// <summary>Fired on the UI thread once the probe completes.</summary>
    public event EventHandler<CapabilityReport>? CapabilitiesReady;

    /// <summary>Kicks off the probe once; subsequent calls are no-ops.</summary>
    public void StartProbe()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        Task.Run(() =>
        {
            var report = Probe();
            Report = report;
            Dispatch(() => CapabilitiesReady?.Invoke(this, report));
        });
    }

    private static CapabilityReport Probe()
    {
        bool avx  = Intrinsics.Avx.IsSupported;
        bool avx2 = Intrinsics.Avx2.IsSupported;
        bool fma  = Intrinsics.Fma.IsSupported;
        bool f16c = NativeMethods.HasF16C();

        // Total memory available to this process — equals physical RAM on a normal desktop, or the
        // container/job limit when constrained. Good enough to gate which local models can load on CPU.
        int ramMb = (int)Math.Min(int.MaxValue, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024L));

        var (cudaMajor, gpuName, cc, vram) = ProbeGpu();
        // CUDA drivers are backward-compatible with the CUDA-12 runtime, so accept 12 and any newer major.
        bool cudaAvailable = gpuName is not null && cudaMajor >= 12;

        bool cudaUsable = cudaAvailable && cc >= MinComputeCapability && vram >= MinCudaVramMb;
        var  backend    = cudaUsable ? WhisperBackend.Cuda : WhisperBackend.Cpu;
        bool canRun     = cudaUsable || avx;   // CPU runtime needs at least AVX to be useful

        return new CapabilityReport(
            avx, avx2, fma, f16c,
            cudaAvailable, cudaMajor,
            gpuName, cc, vram, ramMb,
            canRun, backend);
    }

    // ── GPU via nvidia-smi ─────────────────────────────────────────────────

    private static (int cudaMajor, string? name, double computeCap, int vramMb) ProbeGpu()
    {
        // name, VRAM (MiB), compute capability, driver — one row per GPU.
        var csv = RunNvidiaSmi(
            "--query-gpu=name,memory.total,compute_cap,driver_version --format=csv,noheader,nounits");
        if (string.IsNullOrWhiteSpace(csv)) return (0, null, 0, 0);

        var first = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .FirstOrDefault();
        if (first is null) return (0, null, 0, 0);

        var cols = first.Split(',', StringSplitOptions.TrimEntries);
        string? name = cols.Length > 0 ? cols[0] : null;
        int     vram = cols.Length > 1 && int.TryParse(cols[1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var v) ? v : 0;
        double  cc   = cols.Length > 2 && double.TryParse(cols[2], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var c) ? c : 0;

        int cudaMajor = ProbeCudaMajor();
        return (cudaMajor, name, cc, vram);
    }

    /// <summary>
    /// The driver's max CUDA version is only in the default nvidia-smi table header
    /// ("CUDA Version: 12.x"), not a --query field — so parse it from a plain run.
    /// </summary>
    private static int ProbeCudaMajor()
    {
        var text = RunNvidiaSmi(string.Empty);
        if (string.IsNullOrEmpty(text)) return 0;
        var m = Regex.Match(text, @"CUDA Version:\s*(\d+)\.");
        return m.Success && int.TryParse(m.Groups[1].Value, out var major) ? major : 0;
    }

    private static string? RunNvidiaSmi(string arguments)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName               = "nvidia-smi",
                    Arguments              = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            if (!p.Start()) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch
        {
            // nvidia-smi not on PATH → no NVIDIA driver/GPU.
            return null;
        }
    }

    private void Dispatch(Action action)
    {
        if (_ui.CheckAccess()) action();
        else _ui.Invoke(action);
    }
}
