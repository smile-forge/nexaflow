using System.Globalization;
using Microsoft.ML.OnnxRuntime;

namespace Nexaflow.AiHost.Models;

/// <summary>How an ONNX Runtime session is put on a GPU: the one its spec names, with an arena grown only by what is
/// asked for, so a peak does not double into a power of two.</summary>
internal static class CudaSessions
{
    /// <summary>Puts a session on GPU <paramref name="device"/>, with any options its spec adds to the CUDA provider's.</summary>
    public static void Append(SessionOptions options, int device, IReadOnlyDictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string>
        {
            ["device_id"]             = device.ToString(CultureInfo.InvariantCulture),
            ["arena_extend_strategy"] = "kSameAsRequested",
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>()) settings[key] = value;
        using var cuda = new OrtCUDAProviderOptions();
        cuda.UpdateOptions(settings);
        options.AppendExecutionProvider_CUDA(cuda);
    }

    /// <summary>The arena a run gives its scratch memory back to: the GPU's, or the CPU's.</summary>
    public static string Arena(int? device)
        => device is { } number ? $"gpu:{number.ToString(CultureInfo.InvariantCulture)}" : "cpu:0";

    private static readonly HashSet<int> SharedArenas = [];

    /// <summary>
    /// Has a session take its GPU memory from the one arena every session sharing it on <paramref name="device"/> takes it from —
    /// registered with the runtime the first time it is asked for, grown only by what is asked for. A graph run as many sessions one
    /// after another (<see cref="StreamedGraph"/>) then holds one session's scratch at a time, not one for each.
    /// </summary>
    public static void ShareArena(SessionOptions options, int device)
    {
        lock (SharedArenas)
            if (SharedArenas.Add(device))
            {
                using var info = new OrtMemoryInfo(OrtMemoryInfo.allocatorCUDA, OrtAllocatorType.DeviceAllocator, device, OrtMemType.Default);
                using var arena = new OrtArenaCfg(0, 1, -1, -1);   // no cap; kSameAsRequested; ONNX Runtime's own chunk and dead-byte limits
                OrtEnv.Instance().CreateAndRegisterAllocator("CUDAExecutionProvider", info, arena,
                    new Dictionary<string, string> { ["device_id"] = device.ToString(CultureInfo.InvariantCulture) });
            }
        options.AddSessionConfigEntry("session.use_env_allocators", "1");
    }
}
