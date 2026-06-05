using System.Runtime.InteropServices;
using LLama.Native;

namespace Nexaflow.Providers.Local;

/// <summary>How local inference will run on this host.</summary>
internal enum AccelStatus
{
    /// <summary>An NVIDIA GPU and the CUDA-12 runtime are present — models offload to the GPU.</summary>
    Gpu,
    /// <summary>An NVIDIA GPU is present but the CUDA-12 runtime (cudart64_12.dll) is missing — CPU only.</summary>
    NoCudaRuntime,
    /// <summary>No usable NVIDIA GPU/driver detected — CPU only.</summary>
    NoGpu,
}

/// <summary>
/// Configures the LlamaSharp native backend exactly once per process (<see cref="NativeLibraryConfig"/> is
/// process-global and set-once).
/// <para>
/// CUDA-version pinning gotcha: LlamaSharp 0.27 maps the host's detected CUDA major to a
/// <c>runtimes/win-x64/native/cuda{N}/</c> folder and only probes THAT folder. On a CUDA-13 host it
/// probes <c>cuda13/</c>, but there is no <c>LLamaSharp.Backend.Cuda13</c> package — only the
/// <c>Backend.Cuda12</c> binaries (ABI-matched to 0.27) ship. CUDA drivers are backward-compatible, so the
/// build mirrors the cuda12 binaries into <c>cuda13/</c> (see the <c>MirrorCuda12AsCuda13</c> target in
/// Nexaflow.Core.csproj); the matching cuda12 build auto-registers its backends on load, so no extra
/// registration code is needed. (Using a NEWER hand-built llama.cpp instead fails: its backends need
/// ggml_backend_load_all() AND its ABI no longer matches managed 0.27 — to go smaller, build llama.cpp at
/// commit 3f7c29d for cuda13.)
/// </para>
/// </summary>
internal static class LocalNativeRuntime
{
    private static int _configured;

    public static void EnsureConfigured()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1) return;
        try
        {
            NativeLibraryConfig.All
                .WithCuda(true)              // prefer GPU; auto-fallback covers CPU-only hosts
                .WithAutoFallback(true);
#if DEBUG
            // Debug-only: surface llama.cpp's backend-selection / load messages (NOT registered in Release).
            NativeLibraryConfig.All.WithLogCallback((LLamaLogLevel level, string message) =>
            {
                if (level == LLamaLogLevel.Debug || string.IsNullOrWhiteSpace(message)) return;
                System.Diagnostics.Debug.WriteLine($"[LLama:{level}] {message.TrimEnd()}");
            });
#endif
        }
        catch
        {
            // A native call elsewhere already locked the config — nothing we can do; carry on.
        }
    }

    /// <summary>Predicts whether local inference will use the GPU, for display in the config UI.</summary>
    internal static AccelStatus DetectAcceleration()
    {
        if (!CanLoad("nvcuda.dll")) return AccelStatus.NoGpu;                  // no NVIDIA driver/GPU
        return CanLoad("cudart64_12.dll") ? AccelStatus.Gpu : AccelStatus.NoCudaRuntime;
    }

    private static bool CanLoad(string library)
    {
        try
        {
            if (NativeLibrary.TryLoad(library, out var handle))
            {
                NativeLibrary.Free(handle);
                return true;
            }
        }
        catch { /* not resolvable */ }
        return false;
    }
}
