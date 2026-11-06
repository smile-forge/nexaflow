using System.Runtime.InteropServices;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The most a GPU held while something ran — the card's own used memory, every process's together, as the driver's management
/// library (NVML) reads it every few milliseconds: the host is a process of its own, and Windows says nothing of one process's
/// share. Two runs watched in turn, with the same else loaded in both, differ by what they themselves held.
/// </summary>
internal sealed class GpuMemoryWatch : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Memory
    {
        public ulong Total, Free, Used;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] private static extern int Init();
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static extern int DeviceByIndex(uint index, out IntPtr device);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")] private static extern int MemoryOf(IntPtr device, out Memory memory);

    private static readonly Lazy<bool> Ready = new(() => Init() == 0);
    private readonly Thread _thread;
    private volatile bool _stopped;
    private long _peak;

    private GpuMemoryWatch(IntPtr device)
    {
        _thread = new Thread(() =>
        {
            while (!_stopped)
            {
                if (MemoryOf(device, out var memory) == 0) Interlocked.Exchange(ref _peak, Math.Max(Interlocked.Read(ref _peak), (long)memory.Used));
                Thread.Sleep(5);
            }
        }) { IsBackground = true, Name = "GPU memory watch" };
        _thread.Start();
    }

    /// <summary>Starts watching GPU <paramref name="index"/>, as NVML numbers it.</summary>
    public static GpuMemoryWatch Start(uint index = 0)
    {
        if (!Ready.Value || DeviceByIndex(index, out var device) != 0) throw new AssertInconclusiveException("NVML cannot read this machine's GPU.");
        return new GpuMemoryWatch(device);
    }

    /// <summary>The most the GPU held while watched.</summary>
    public long PeakBytes => Interlocked.Read(ref _peak);

    public void Dispose()
    {
        _stopped = true;
        _thread.Join();
    }
}
