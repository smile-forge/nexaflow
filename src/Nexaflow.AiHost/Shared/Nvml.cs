using System.Runtime.InteropServices;

namespace Nexaflow.AiHost;

/// <summary>An NVIDIA card as its driver describes it: its PCI bus id, its name, all its memory and what is free of it now,
/// across every program using it.</summary>
internal sealed record NvmlCard(string BusId, string Name, long TotalBytes, long FreeBytes);

/// <summary>
/// NVIDIA's management library (NVML), which comes with the driver on Windows and Linux: the cards, in PCI bus order — the
/// order the host numbers them in — what is free on each now across every program using it, and the CUDA version the driver
/// runs. llama.cpp's and CUDA's own readings of free memory are what this process sees, and on Windows that leaves out what
/// another process holds: two programs going by them would each take the whole card as theirs. One source, compiled into
/// the host, which budgets by it, and the SDK, which reads the machine by it before any host runs. A card NVML does not
/// manage reads as null.
/// </summary>
internal sealed unsafe class Nvml : IDisposable
{
    private const int Success = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total, Free, Used;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlPciInfo
    {
        public fixed byte BusIdLegacy[16];
        public uint Domain, Bus, Device, PciDeviceId, PciSubSystemId;
        public fixed byte BusId[32];
    }

    private readonly IntPtr _library;
    private readonly delegate* unmanaged<int> _shutdown;
    private readonly delegate* unmanaged<uint*, int> _count;
    private readonly delegate* unmanaged<uint, IntPtr*, int> _byIndex;
    private readonly delegate* unmanaged<byte*, IntPtr*, int> _byBusId;
    private readonly delegate* unmanaged<IntPtr, NvmlPciInfo*, int> _pciInfo;
    private readonly delegate* unmanaged<IntPtr, byte*, uint, int> _name;
    private readonly delegate* unmanaged<IntPtr, NvmlMemory*, int> _memory;
    private readonly delegate* unmanaged<int*, int> _cudaDriver;
    private readonly Dictionary<string, IntPtr> _cards = new(StringComparer.OrdinalIgnoreCase);
    private int _closed;

    private Nvml(IntPtr library)
    {
        _library  = library;
        _shutdown = (delegate* unmanaged<int>)Export("nvmlShutdown");
        _count    = (delegate* unmanaged<uint*, int>)Export("nvmlDeviceGetCount_v2");
        _byIndex  = (delegate* unmanaged<uint, IntPtr*, int>)Export("nvmlDeviceGetHandleByIndex_v2");
        _byBusId  = (delegate* unmanaged<byte*, IntPtr*, int>)Export("nvmlDeviceGetHandleByPciBusId_v2");
        _pciInfo  = (delegate* unmanaged<IntPtr, NvmlPciInfo*, int>)Export("nvmlDeviceGetPciInfo_v3");
        _name     = (delegate* unmanaged<IntPtr, byte*, uint, int>)Export("nvmlDeviceGetName");
        _memory   = (delegate* unmanaged<IntPtr, NvmlMemory*, int>)Export("nvmlDeviceGetMemoryInfo");
        _cudaDriver = (delegate* unmanaged<int*, int>)Export("nvmlSystemGetCudaDriverVersion_v2");

        IntPtr Export(string name) => NativeLibrary.GetExport(library, name);
    }

    /// <summary>The driver's management library, started; null on a machine without it — no NVIDIA driver — or where it does
    /// not start.</summary>
    public static Nvml? Open()
    {
        foreach (var name in LibraryNames())
        {
            if (!NativeLibrary.TryLoad(name, out var library)) continue;
            try
            {
                if (!NativeLibrary.TryGetExport(library, "nvmlInit_v2", out var init) || ((delegate* unmanaged<int>)init)() != Success)
                {
                    NativeLibrary.Free(library);
                    return null;
                }
                return new Nvml(library);
            }
            catch (EntryPointNotFoundException)
            {
                NativeLibrary.Free(library);   // a driver too old for what is read here
                return null;
            }
        }
        return null;
    }

    /// <summary>Where the driver puts the library: beside the system's own on Windows — older drivers in NVSMI — and as the
    /// driver's shared object on Linux.</summary>
    private static IEnumerable<string> LibraryNames()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return "nvml.dll";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll");
        }
        else
        {
            yield return "libnvidia-ml.so.1";
            yield return "libnvidia-ml.so";
        }
    }

    /// <summary>What is free now on the card at <paramref name="busId"/> — a PCI bus id as <c>domain:bus:device.function</c>;
    /// null for none, or a card NVML does not manage.</summary>
    public long? FreeBytes(string? busId)
    {
        if (busId is null) return null;
        IntPtr card;
        lock (_cards)
        {
            if (!_cards.TryGetValue(busId, out card))
            {
                card = Card(busId);
                _cards[busId] = card;
            }
        }
        NvmlMemory memory;
        return card != IntPtr.Zero && _memory(card, &memory) == Success ? (long)memory.Free : null;
    }

    /// <summary>Every card NVML manages, as it is now.</summary>
    public IReadOnlyList<NvmlCard> Read()
    {
        uint count;
        if (_count(&count) != Success) return [];
        var cards = new List<NvmlCard>((int)count);
        byte* name = stackalloc byte[96];
        for (uint i = 0; i < count; i++)
        {
            IntPtr card;
            NvmlPciInfo pci;
            NvmlMemory memory;
            if (_byIndex(i, &card) != Success || _pciInfo(card, &pci) != Success || _memory(card, &memory) != Success) continue;
            string named = _name(card, name, 96) == Success ? Marshal.PtrToStringUTF8((IntPtr)name) ?? "" : "";
            cards.Add(new NvmlCard(Marshal.PtrToStringUTF8((IntPtr)pci.BusId) ?? "", named, (long)memory.Total, (long)memory.Free));
        }
        return cards;
    }

    /// <summary>The newest CUDA version the driver runs — 13.0 for one that reports 13000; null where it does not say.</summary>
    public Version? CudaDriver()
    {
        int version;
        return _cudaDriver(&version) == Success && version > 0 ? new Version(version / 1000, version % 1000 / 10) : null;
    }

    private IntPtr Card(string busId)
    {
        int length = System.Text.Encoding.ASCII.GetByteCount(busId);
        byte* id = stackalloc byte[length + 1];
        System.Text.Encoding.ASCII.GetBytes(busId, new Span<byte>(id, length));
        id[length] = 0;
        IntPtr card;
        return _byBusId(id, &card) == Success ? card : IntPtr.Zero;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _shutdown();
        NativeLibrary.Free(_library);
    }
}
