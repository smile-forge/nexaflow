using System.Runtime.InteropServices;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// The few calls into NVIDIA's CUDA runtime the host makes itself — pinned memory, device memory, and copies between them on a
/// stream of their own — from the runtime pack's <c>cudart</c>, the one ONNX Runtime's CUDA layer loads, bound by path as
/// <see cref="NativeRuntime"/> binds ONNX Runtime. Every call names its device first: the runtime's device is per thread.
/// </summary>
internal static unsafe class CudaRuntime
{
    public const string Library = "cudart64_13.dll";

    private static readonly Lazy<Exports> Bound = new(Bind);

    private sealed class Exports
    {
        public delegate* unmanaged<int, int> SetDevice;
        public delegate* unmanaged<IntPtr*, nuint, int> Malloc;
        public delegate* unmanaged<IntPtr, int> Free;
        public delegate* unmanaged<IntPtr*, nuint, uint, int> HostAlloc;
        public delegate* unmanaged<IntPtr, int> FreeHost;
        public delegate* unmanaged<IntPtr, IntPtr, nuint, int, IntPtr, int> MemcpyAsync;
        public delegate* unmanaged<IntPtr*, int> StreamCreate;
        public delegate* unmanaged<IntPtr, int> StreamDestroy;
        public delegate* unmanaged<IntPtr, int> StreamSynchronize;
    }

    private static Exports Bind()
    {
        string path = NativeRuntime.Pack is { } pack ? Path.Combine(pack, Library)
            : throw new HostRequestException(HostErrorKind.Runtime, "The host runs without the CUDA pack, so it has no CUDA runtime to stream a graph with.");
        IntPtr library = NativeLibrary.Load(path);
        IntPtr Export(string name) => NativeLibrary.GetExport(library, name);
        return new Exports
        {
            SetDevice         = (delegate* unmanaged<int, int>)Export("cudaSetDevice"),
            Malloc            = (delegate* unmanaged<IntPtr*, nuint, int>)Export("cudaMalloc"),
            Free              = (delegate* unmanaged<IntPtr, int>)Export("cudaFree"),
            HostAlloc         = (delegate* unmanaged<IntPtr*, nuint, uint, int>)Export("cudaHostAlloc"),
            FreeHost          = (delegate* unmanaged<IntPtr, int>)Export("cudaFreeHost"),
            MemcpyAsync       = (delegate* unmanaged<IntPtr, IntPtr, nuint, int, IntPtr, int>)Export("cudaMemcpyAsync"),
            StreamCreate      = (delegate* unmanaged<IntPtr*, int>)Export("cudaStreamCreate"),
            StreamDestroy     = (delegate* unmanaged<IntPtr, int>)Export("cudaStreamDestroy"),
            StreamSynchronize = (delegate* unmanaged<IntPtr, int>)Export("cudaStreamSynchronize"),
        };
    }

    private const int HostToDevice = 1, DeviceToHost = 2;

    private static void Check(int status, string what)
    {
        if (status != 0) throw new HostRequestException(HostErrorKind.Runtime, $"CUDA could not {what} (error {status}).");
    }

    /// <summary><paramref name="bytes"/> of device memory on <paramref name="device"/>.</summary>
    public static IntPtr DeviceAlloc(int device, long bytes)
    {
        var e = Bound.Value;
        Check(e.SetDevice(device), $"select GPU {device}");
        IntPtr pointer;
        Check(e.Malloc(&pointer, (nuint)bytes), $"set aside {bytes >> 20} MiB on GPU {device}");
        return pointer;
    }

    public static void DeviceFree(int device, IntPtr pointer)
    {
        var e = Bound.Value;
        e.SetDevice(device);
        e.Free(pointer);
    }

    /// <summary><paramref name="bytes"/> of this machine's memory, pinned, so a copy to a GPU runs at the link's speed and
    /// beside the GPU's work.</summary>
    public static IntPtr PinnedAlloc(int device, long bytes)
    {
        var e = Bound.Value;
        Check(e.SetDevice(device), $"select GPU {device}");
        IntPtr pointer;
        Check(e.HostAlloc(&pointer, (nuint)bytes, 0), $"pin {bytes >> 20} MiB of this machine's memory");
        return pointer;
    }

    public static void PinnedFree(IntPtr pointer) => Bound.Value.FreeHost(pointer);

    public static IntPtr StreamCreate(int device)
    {
        var e = Bound.Value;
        Check(e.SetDevice(device), $"select GPU {device}");
        IntPtr stream;
        Check(e.StreamCreate(&stream), $"make a stream on GPU {device}");
        return stream;
    }

    public static void StreamDestroy(int device, IntPtr stream)
    {
        var e = Bound.Value;
        e.SetDevice(device);
        e.StreamDestroy(stream);
    }

    /// <summary>Starts copying <paramref name="bytes"/> from pinned memory to device memory on <paramref name="stream"/>, and
    /// returns at once.</summary>
    public static void CopyToDevice(int device, IntPtr to, IntPtr from, long bytes, IntPtr stream)
    {
        var e = Bound.Value;
        Check(e.SetDevice(device), $"select GPU {device}");
        Check(e.MemcpyAsync(to, from, (nuint)bytes, HostToDevice, stream), $"copy {bytes >> 20} MiB to GPU {device}");
    }

    /// <summary>Starts copying <paramref name="bytes"/> from device memory to pinned memory on <paramref name="stream"/>, and
    /// returns at once.</summary>
    public static void CopyToHost(int device, IntPtr to, IntPtr from, long bytes, IntPtr stream)
    {
        var e = Bound.Value;
        Check(e.SetDevice(device), $"select GPU {device}");
        Check(e.MemcpyAsync(to, from, (nuint)bytes, DeviceToHost, stream), $"copy {bytes >> 20} MiB from GPU {device}");
    }

    /// <summary>Waits until every copy started on <paramref name="stream"/> is done.</summary>
    public static void Wait(int device, IntPtr stream)
    {
        var e = Bound.Value;
        Check(e.SetDevice(device), $"select GPU {device}");
        Check(e.StreamSynchronize(stream), $"finish copying to GPU {device}");
    }
}
