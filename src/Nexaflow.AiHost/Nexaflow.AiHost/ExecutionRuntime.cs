using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntimeGenAI;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Llama;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Hardware;

namespace Nexaflow.AiHost;

/// <summary>
/// The host's one ONNX Runtime and onnxruntime-genai, held for the life of the process: onnxruntime-genai must keep
/// its handle until exit, or the process can hang at shutdown. With the CUDA pack, models run on the GPUs it finds,
/// each within its own budget; without it, on the CPU.
/// </summary>
internal sealed class ExecutionRuntime : IHostGpus, IDisposable
{
    public const string Cpu  = "cpu";



    private readonly OgaHandle _genai = new();

    /// <param name="memoryShare">The share of this machine's memory, in percent, models on the CPU may hold.</param>
    public ExecutionRuntime(HostLog log, int memoryShare = AiHostProtocol.DefaultMemoryShare)
    {
        Memory = HostMemory.Of(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, memoryShare);
        // The runtime's own warnings (node placement, shape merges) are expected on these exports; errors only.
        var options = new EnvironmentCreationOptions { logId = "aihost", logLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        var env = OrtEnv.CreateInstanceWithOptions(ref options);
        Version            = env.GetVersionString();
        ExecutionProviders = env.GetAvailableProviders();
        LlamaBackends.Load(NativeRuntime.Pack, log);
        Gpus = [.. LlamaBackends.Gpus.Select(g => new HostGpu(g.Number, g.Name, g.BusId, g.TotalBytes, BudgetOf(g.FreeBytes), g.FreeBytes))];
        log.Write($"ONNX Runtime {Version} from {NativeRuntime.Pack ?? "the install"}; models run on "
                  + (Gpus.Count == 0 ? Cpu : string.Join(", ", Gpus.Select(g => $"GPU {g.Device} ({g.Name}, within {g.BudgetBytes >> 20} MiB)")))
                            + $"; models on the CPU within {Memory.BudgetBytes >> 20} of {Memory.TotalBytes >> 20} MiB");
    }

    public string Version { get; }

    /// <summary>The execution providers this runtime was built with, in ONNX Runtime's order of preference.</summary>
    public IReadOnlyList<string> ExecutionProviders { get; }

    /// <summary>The GPUs models may run on, each with its budget and what was free when the host started — none when
    /// they run on the CPU.</summary>
    public IReadOnlyList<HostGpu> Gpus { get; }

    /// <summary>This machine's memory, and the share of it models on the CPU may hold.</summary>
    public HostMemory Memory { get; }

    /// <summary>The GPUs as they are now: each with what is free on it.</summary>
    public IReadOnlyList<HostGpu> GpusNow() => [.. Gpus.Select(g => g with { FreeBytes = LlamaBackends.FreeBytes(g.Device) })];

    /// <exception cref="HostRequestException">This host has no GPU <paramref name="device"/>.</exception>
    public HostGpu Gpu(int device)
        => device >= 0 && device < Gpus.Count ? Gpus[device]
           : throw new HostRequestException(HostErrorKind.Invalid,
               Gpus.Count == 0 ? $"GPU {device} was asked for, and this host runs models on the CPU."
                               : $"GPU {device} was asked for, and this host has {Gpus.Count}.");

    /// <summary>What models may hold on a GPU that had <paramref name="freeBytes"/> free.</summary>
    internal static long BudgetOf(long freeBytes) => Math.Max(0, freeBytes - HostGpu.ReservedBytes);

    public void Dispose()
    {
        LlamaBackends.Free();
        _genai.Dispose();
    }
}
