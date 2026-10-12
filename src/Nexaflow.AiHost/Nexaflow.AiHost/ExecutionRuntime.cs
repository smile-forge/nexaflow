using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Nexaflow.AiHost;

/// <summary>
/// The host's one ONNX Runtime and onnxruntime-genai, held for the life of the process: onnxruntime-genai must keep
/// its handle until exit, or the process can hang at shutdown.
/// </summary>
internal sealed class ExecutionRuntime : IDisposable
{
    private readonly OgaHandle _genai = new();

    public ExecutionRuntime(HostLog log)
    {
        var env = OrtEnv.Instance();
        Version            = env.GetVersionString();
        ExecutionProviders = env.GetAvailableProviders();
        log.Write($"ONNX Runtime {Version} from {NativeRuntime.Pack ?? "the install"}; providers {string.Join(", ", ExecutionProviders)}");
    }

    public string Version { get; }

    /// <summary>The execution providers this runtime was built with, in ONNX Runtime's order of preference.</summary>
    public IReadOnlyList<string> ExecutionProviders { get; }

    public void Dispose() => _genai.Dispose();
}
