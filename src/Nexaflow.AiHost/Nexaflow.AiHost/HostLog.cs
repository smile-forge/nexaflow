
namespace Nexaflow.AiHost;

/// <summary>
/// The host's own log. What the host does with itself — starting, stopping, a worker started or replaced, the runtime it loaded — and
/// every error is written wherever there is a log: a load failure or a crash's last words, ONNX Runtime's errors
/// (<see cref="ExecutionRuntime"/>), llama.cpp's errors and ggml's message when it aborts (<see cref="Llama.LlamaBackends"/>). What it
/// does for an app — a client connecting and going, a model loaded and let go, llama.cpp's warnings as it reads one — says what was
/// asked of it, so it is written only where the log was opened for model calls (<see cref="WriteActivity"/>): by the machine's
/// policy (<see cref="HostPolicy.LogsModelCalls"/>), or by an app whose safety logging is on. A host an app starts keeps a log only
/// while that is on — the app names a folder (<see cref="Nexaflow.AiHost.Contracts.AiHostProtocol.LogDirArgument"/>) beside the
/// shell's, under the same retention and cap; the service always keeps one. Each line is flushed as it is written, so a process that
/// dies leaves everything up to it. With no folder it keeps nothing.
/// </summary>
/// <param name="modelCalls">Whether what the host does for its apps is written too.</param>
internal sealed class HostLog(string? directory, bool modelCalls) : IDisposable
{
    /// <summary>Names the host's files in the shared logs folder.</summary>
    public const string FilePrefix = "aihost";

    private readonly AiHostLogFile? _file = directory is null ? null : new AiHostLogFile(directory, FilePrefix);

    /// <summary>Writes what the host does with itself, or an error.</summary>
    public void Write(string line) => _file?.Write(line);

    /// <summary>Writes what the host does for an app — who connected, which model loaded where — where the log keeps model calls.</summary>
    public void WriteActivity(string line)
    {
        if (modelCalls) _file?.Write(line);
    }

    public void Dispose() => _file?.Dispose();
}
