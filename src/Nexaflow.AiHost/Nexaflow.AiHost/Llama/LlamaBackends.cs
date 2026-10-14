using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// llama.cpp in this process, set up once: the CPU backend best for this processor, from the libraries beside the
/// host, and with a runtime pack, the CUDA backend from the pack — loaded by path, its NVIDIA libraries found on the
/// PATH <see cref="NativeRuntime"/> gave the pack. llama.cpp's warnings and errors go to the host's log.
/// </summary>
internal static unsafe class LlamaBackends
{
    private const string CudaBackend = "ggml-cuda.dll";

    private static HostLog? _log;

    /// <summary>The llama.cpp build the host carries, from <c>llama-cpp.build</c> beside it.</summary>
    public static string Build { get; private set; } = "";

    /// <summary>Whether models can be offloaded to a GPU: the CUDA backend loaded.</summary>
    public static bool OnGpu { get; private set; }

    /// <exception cref="DllNotFoundException">The pack has no CUDA backend, or it would not load.</exception>
    public static void Load(string? pack, HostLog log)
    {
        _log = log;
        string here = AppContext.BaseDirectory;
        LlamaNative.LogSet(&Log, null);
        MtmdNative.LogSet(&Log, null);
        LlamaNative.LoadBackendsFrom(here);
        if (pack is not null)
        {
            string cuda = Path.Combine(pack, CudaBackend);
            if (LlamaNative.LoadBackend(cuda) == IntPtr.Zero)
                throw new DllNotFoundException($"llama.cpp's CUDA backend would not load from {cuda}.");
            OnGpu = true;
        }
        LlamaNative.BackendInit();

        Build = File.ReadAllText(Path.Combine(here, "llama-cpp.build")).Trim();
        var devices = Enumerable.Range(0, (int)LlamaNative.DeviceCount())
            .Select(i => Marshal.PtrToStringUTF8(LlamaNative.DeviceDescription(LlamaNative.Device((nuint)i))) ?? "?");
        log.Write($"llama.cpp {Build}; devices: {string.Join(", ", devices)}");
    }

    public static void Free() => LlamaNative.BackendFree();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Log(LlamaNative.LogLevel level, byte* text, void* userData)
    {
        if (level is not (LlamaNative.LogLevel.Warn or LlamaNative.LogLevel.Error) || text is null) return;
        _log?.Write("llama.cpp: " + Marshal.PtrToStringUTF8((IntPtr)text)?.TrimEnd());
    }
}
