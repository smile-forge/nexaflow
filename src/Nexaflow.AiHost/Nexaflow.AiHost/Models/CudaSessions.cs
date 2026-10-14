using System.Globalization;
using Microsoft.ML.OnnxRuntime;

namespace Nexaflow.AiHost.Models;

/// <summary>How an ONNX Runtime session is put on a GPU: the one its spec names, with an arena grown only by what is
/// asked for, so a peak does not double into a power of two.</summary>
internal static class CudaSessions
{
    public static void Append(SessionOptions options, int device)
    {
        using var cuda = new OrtCUDAProviderOptions();
        cuda.UpdateOptions(new Dictionary<string, string>
        {
            ["device_id"]             = device.ToString(CultureInfo.InvariantCulture),
            ["arena_extend_strategy"] = "kSameAsRequested",
        });
        options.AppendExecutionProvider_CUDA(cuda);
    }

    /// <summary>The arena a run gives its scratch memory back to: the GPU's, or the CPU's.</summary>
    public static string Arena(int? device)
        => device is { } number ? $"gpu:{number.ToString(CultureInfo.InvariantCulture)}" : "cpu:0";
}
