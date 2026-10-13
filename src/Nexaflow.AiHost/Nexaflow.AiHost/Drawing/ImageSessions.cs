using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Drawing;

/// <summary>How the image model's graphs are opened: on the GPU where the host runs models there, else the CPU.</summary>
internal static class ImageSessions
{
    /// <exception cref="HostRequestException">The part names no file, or it is not there.</exception>
    public static InferenceSession Open(ModelSpec spec, string executionProvider)
    {
        string path = Path.Combine(spec.Folder, spec.File ?? throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} does not name its file."));
        if (!File.Exists(path)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id}'s graph is not in {spec.Folder}.");

        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel       = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        if (executionProvider == ExecutionRuntime.Cuda)
        {
            // Grown only by what is asked for, so a picture's peak does not double into a power of two.
            using var cuda = new OrtCUDAProviderOptions();
            cuda.UpdateOptions(new Dictionary<string, string> { ["device_id"] = "0", ["arena_extend_strategy"] = "kSameAsRequested" });
            options.AppendExecutionProvider_CUDA(cuda);
        }
        else
        {
            options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
        }
        return new InferenceSession(path, options);
    }

    /// <summary>
    /// Options for a run after which the session gives its scratch memory back: a part between pictures then holds its
    /// weights alone, not the 2–5 GB its largest run needed.
    /// </summary>
    public static RunOptions Releasing()
    {
        var run = new RunOptions();
        run.AddRunConfigEntry("memory.enable_memory_arena_shrinkage", "gpu:0");
        return run;
    }
}
