using Nexaflow.AiHost;
using Nexaflow.AiHost.Llama;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>This test process bound to the native runtimes the host runs on, as the host binds itself: once, before anything touches
/// ONNX Runtime or llama.cpp — ONNX Runtime from the CUDA pack in the test store where it holds one this machine runs, and llama.cpp's
/// backends, the CPU one beside the host and the pack's CUDA one with it — for the tests that run the host's models in this process
/// rather than in a host of their own. llama.cpp's warnings and errors go to a log of this process's own.</summary>
internal static class PackRuntime
{
    private static readonly object Gate = new();
    private static bool _bound;
    private static HostLog? _log;

    /// <summary>Binds this process to the native runtimes, once; the pack in the test store
    /// (<see cref="TestStore.RuntimePackFolder"/>) they came from, or null where the store holds none this machine runs, and they
    /// run on the CPU.</summary>
    public static string? Bind()
    {
        var pack = TestStore.RuntimePackFolder;
        lock (Gate)
            if (!_bound)
            {
                NativeRuntime.Load(pack);
                _log = new HostLog(TestTemp.CreateSubdirectory("nexaflow-in-process-host-").FullName, modelCalls: true);
                LlamaBackends.Load(pack, _log);
                _bound = true;
            }
        return pack;
    }
}
