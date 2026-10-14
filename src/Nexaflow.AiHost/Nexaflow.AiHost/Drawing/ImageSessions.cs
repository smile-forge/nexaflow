using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>How the image model's graphs are opened: on the GPU their spec places them on, else the CPU.</summary>
internal static class ImageSessions
{
    /// <exception cref="HostRequestException">The part names no file, or it is not there.</exception>
    public static InferenceSession Open(ModelSpec spec)
    {
        string path = Path.Combine(spec.Folder, spec.File ?? throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} does not name its file."));
        if (!File.Exists(path)) throw new HostRequestException(HostErrorKind.NotFound, $"{spec.Id}'s graph is not in {spec.Folder}.");

        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel       = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        if (spec.Device is { } device) CudaSessions.Append(options, device);
        else options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
        return new InferenceSession(path, options);
    }

    /// <summary>
    /// Options for a run after which the session gives its scratch memory back: a part between pictures then holds its
    /// weights alone, not the 2–5 GB its largest run needed.
    /// </summary>
    public static RunOptions Releasing(ModelSpec spec)
    {
        var run = new RunOptions();
        run.AddRunConfigEntry("memory.enable_memory_arena_shrinkage", CudaSessions.Arena(spec.Device));
        return run;
    }
}
