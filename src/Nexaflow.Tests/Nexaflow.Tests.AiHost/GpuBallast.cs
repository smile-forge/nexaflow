using System.Runtime.InteropServices;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Device memory a test holds on the first GPU, so the AI host finds the card as tight as the test needs — real memory, allocated
/// through the CUDA runtime of the test store's CUDA pack, as <c>tools/bench/Hold-GpuMemory.ps1</c> holds it by hand. Held before a
/// host starts, it is what the host finds the card leaves it, and budgets from. Let go on dispose.
/// </summary>
internal sealed class GpuBallast : IDisposable
{
    private const long Chunk = 256L << 20;

    private delegate int SetDevice(int device);
    private delegate int Malloc(out IntPtr pointer, nuint bytes);
    private delegate int Free(IntPtr pointer);
    private delegate int MemGetInfo(out nuint free, out nuint total);

    private readonly Free _free;
    private readonly List<IntPtr> _held = [];

    private GpuBallast(IntPtr cudart)
    {
        _free = Marshal.GetDelegateForFunctionPointer<Free>(NativeLibrary.GetExport(cudart, "cudaFree"));
    }

    /// <summary>What it holds.</summary>
    public long Bytes => _held.Count * Chunk;

    /// <summary>
    /// Holds device memory until what is free on the card — every process's use counted — is at most <paramref name="leaveFree"/>.
    /// Inconclusive where the test store holds no CUDA pack this machine runs.
    /// </summary>
    public static GpuBallast Leaving(long leaveFree)
    {
        string pack = TestStore.RuntimePackFolder ?? throw new AssertInconclusiveException(TestStore.Missing());
        var cudart = NativeLibrary.Load(Path.Combine(pack, "cudart64_13.dll"));
        var ballast = new GpuBallast(cudart);
        Marshal.GetDelegateForFunctionPointer<SetDevice>(NativeLibrary.GetExport(cudart, "cudaSetDevice"))(0);
        var malloc = Marshal.GetDelegateForFunctionPointer<Malloc>(NativeLibrary.GetExport(cudart, "cudaMalloc"));
        var memory = Marshal.GetDelegateForFunctionPointer<MemGetInfo>(NativeLibrary.GetExport(cudart, "cudaMemGetInfo"));
        // Counted as the CUDA runtime counts it, as each chunk is taken: NVML's reading lags what this process has just allocated.
        while (memory(out nuint free, out _) == 0 && (long)free > leaveFree)
        {
            if (malloc(out var pointer, (nuint)Chunk) != 0) break;   // the card has nothing more to give
            ballast._held.Add(pointer);
        }
        return ballast;
    }

    public void Dispose()
    {
        foreach (var pointer in _held) _free(pointer);
        _held.Clear();
    }
}
