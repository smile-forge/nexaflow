using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntimeGenAI;
using Nexaflow.IO.Common.Hardware;

namespace Nexaflow.AiHost;

/// <summary>
/// The host's one ONNX Runtime and onnxruntime-genai, held for the life of the process: onnxruntime-genai must keep
/// its handle until exit, or the process can hang at shutdown. Models run on CUDA when the runtime came from the CUDA
/// pack, on the CPU otherwise.
/// </summary>
internal sealed class ExecutionRuntime : IDisposable
{
    public const string Cuda = "cuda";
    public const string Cpu  = "cpu";

    /// <summary>What a GPU keeps back from models: the desktop and other applications, the runtime's own allocations
    /// (about 1.8 GB stays with a process that has used CUDA), and transients beyond each model's estimate.</summary>
    internal const long ReservedBytes = 3L << 30;

    private readonly OgaHandle _genai = new();

    public ExecutionRuntime(HostLog log, ComputeHardwareReport hardware)
    {
        // The runtime's own warnings (node placement, shape merges) are expected on these exports; errors only.
        var options = new EnvironmentCreationOptions { logId = "aihost", logLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        var env = OrtEnv.CreateInstanceWithOptions(ref options);
        Version            = env.GetVersionString();
        ExecutionProviders = env.GetAvailableProviders();
        ExecutionProvider  = NativeRuntime.Pack is null ? Cpu : Cuda;
        BudgetBytes        = ExecutionProvider == Cuda ? BudgetOf(hardware) : null;
        log.Write($"ONNX Runtime {Version} from {NativeRuntime.Pack ?? "the install"}; models run on {ExecutionProvider}"
                  + (BudgetBytes is { } budget ? $", within {budget >> 20} MiB of VRAM" : ""));
    }

    public string Version { get; }

    /// <summary>The execution providers this runtime was built with, in ONNX Runtime's order of preference.</summary>
    public IReadOnlyList<string> ExecutionProviders { get; }

    /// <summary>What models load on: <see cref="Cuda"/> or <see cref="Cpu"/>.</summary>
    public string ExecutionProvider { get; }

    /// <summary>The GPU memory models may hold together, or null when they run on the CPU.</summary>
    public long? BudgetBytes { get; }

    /// <summary>The NVIDIA GPU with the most memory of its own, less what is kept back.</summary>
    internal static long? BudgetOf(ComputeHardwareReport hardware)
        => hardware.Adapters.Where(a => a.Vendor == ComputeVendor.Nvidia && !a.IsIntegrated && !a.IsNpu)
                            .Select(a => (long?)Math.Max(0, a.DedicatedMemoryBytes - ReservedBytes))
                            .Max();

    public void Dispose() => _genai.Dispose();
}
