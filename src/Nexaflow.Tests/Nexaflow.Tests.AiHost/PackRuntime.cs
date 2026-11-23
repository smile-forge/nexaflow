using Nexaflow.AiHost;

namespace Nexaflow.Tests.AiHost;

/// <summary>This test process bound to the CUDA pack the host runs from, as the host binds itself: once, before anything touches
/// ONNX Runtime — for the tests that run the host's models in this process rather than in a host of their own.</summary>
internal static class PackRuntime
{
    private static readonly object Gate = new();
    private static bool _bound;

    /// <summary>The pack in the test store (<see cref="TestStore.RuntimePackFolder"/>), bound; null where the store holds none this
    /// machine runs.</summary>
    public static string? Bind()
    {
        if (TestStore.RuntimePackFolder is not { } pack) return null;
        lock (Gate)
            if (!_bound)
            {
                NativeRuntime.Load(pack);
                _bound = true;
            }
        return pack;
    }
}
