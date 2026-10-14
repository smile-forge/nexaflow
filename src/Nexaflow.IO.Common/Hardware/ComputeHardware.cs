using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Nexaflow.IO.Common.Hardware;

public enum ComputeVendor { Nvidia, Amd, Intel, Qualcomm, Microsoft, Other }

/// <summary>One adapter that can run machine-learning work: a GPU, or an NPU (one with no graphics).</summary>
public sealed record ComputeAdapter(
    string Name, ComputeVendor Vendor, long DedicatedMemoryBytes, bool IsIntegrated, bool IsNpu);

/// <summary>What a machine has for local AI: its ML-capable adapters and, on NVIDIA, the CUDA driver.</summary>
public sealed record ComputeHardwareReport(IReadOnlyList<ComputeAdapter> Adapters, Version? CudaDriver)
{
    public IEnumerable<ComputeAdapter> Npus => Adapters.Where(a => a.IsNpu);
}

/// <summary>
/// Finds the adapters a machine can run local AI on, through DXCore — one API for GPUs of every vendor and for NPUs,
/// with each adapter's memory as a 64-bit count (WMI's caps at 4 GB) — and the CUDA driver's version from
/// <c>nvcuda.dll</c>, without starting <c>nvidia-smi</c>. Never throws: what cannot be read is left out.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ComputeHardware
{
    public static ComputeHardwareReport Probe() => new(Adapters(), CudaDriver());

    private static IReadOnlyList<ComputeAdapter> Adapters()
    {
        var found = new List<ComputeAdapter>();
        try
        {
            if (DXCoreCreateAdapterFactory(in FactoryIid, out var factoryObject) < 0) return found;
            var factory = (IDXCoreAdapterFactory)factoryObject;

            if (factory.CreateAdapterList(1, [GenericMl], in ListIid, out var listObject) < 0) return found;
            var list = (IDXCoreAdapterList)listObject;

            for (uint i = 0, n = list.GetAdapterCount(); i < n; i++)
            {
                if (list.GetAdapter(i, in AdapterIid, out var adapterObject) < 0) continue;
                var adapter = (IDXCoreAdapter)adapterObject;
                if (!adapter.IsValid() || ReadBool(adapter, Property.IsHardware) != true) continue;   // the software renderer

                found.Add(new ComputeAdapter(
                    ReadString(adapter, Property.DriverDescription) ?? "Unknown adapter",
                    VendorOf(ReadHardwareVendor(adapter)),
                    (long)(ReadUInt64(adapter, Property.DedicatedAdapterMemory) ?? 0),
                    ReadBool(adapter, Property.IsIntegrated) == true,
                    IsNpu: !adapter.IsAttributeSupported(in D3D12Graphics)));
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            // DXCore is missing before Windows 10 2004; what was found stands.
        }
        return found;
    }

    private static Version? CudaDriver()
    {
        if (!NativeLibrary.TryLoad("nvcuda.dll", out var cuda)) return null;
        try
        {
            if (!NativeLibrary.TryGetExport(cuda, "cuDriverGetVersion", out var export)) return null;
            var getVersion = Marshal.GetDelegateForFunctionPointer<CuDriverGetVersion>(export);
            return getVersion(out var version) == 0 ? new Version(version / 1000, version % 1000 / 10) : null;
        }
        finally { NativeLibrary.Free(cuda); }
    }

    internal static ComputeVendor VendorOf(uint? pciVendor) => pciVendor switch
    {
        0x10DE => ComputeVendor.Nvidia,
        0x1002 or 0x1022 => ComputeVendor.Amd,
        0x8086 => ComputeVendor.Intel,
        0x5143 or 0x4D4F4351 => ComputeVendor.Qualcomm,
        0x1414 => ComputeVendor.Microsoft,
        _ => ComputeVendor.Other,
    };

    // ── DXCore (dxcore.h, dxcore_interface.h) ──────────────────────────────────

    private static readonly Guid FactoryIid    = new("78ee5945-c36e-4b13-a669-005dd11c0f06");
    private static readonly Guid ListIid       = new("526c7776-40e9-459b-b711-f32ad76dfc28");
    private static readonly Guid AdapterIid    = new("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e");
    private static readonly Guid GenericMl     = new("b71b0d41-1088-422f-a27c-0250b7d3a988");
    private static readonly Guid D3D12Graphics = new("0c9ece4d-2f6e-4f01-8c96-e89e331b47b1");

    /// <summary>DXCoreAdapterProperty values this probe reads.</summary>
    private static class Property
    {
        public const uint DriverDescription      = 2;
        public const uint HardwareId             = 3;
        public const uint DedicatedAdapterMemory = 7;
        public const uint IsHardware             = 11;
        public const uint IsIntegrated           = 12;
    }

    private static string? ReadString(IDXCoreAdapter adapter, uint property)
    {
        if (adapter.GetPropertySize(property, out var size) < 0 || size == 0) return null;
        var buffer = Marshal.AllocHGlobal((nint)size);
        try
        {
            if (adapter.GetProperty(property, size, buffer) < 0) return null;
            var bytes = new byte[(int)size];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static ulong? ReadUInt64(IDXCoreAdapter adapter, uint property)
        => Read(adapter, property, 8, b => (ulong)Marshal.ReadInt64(b));

    private static bool? ReadBool(IDXCoreAdapter adapter, uint property)
        => Read(adapter, property, 1, b => Marshal.ReadByte(b) != 0);

    /// <summary>DXCoreHardwareID: vendor, device, subsystem, revision — the vendor is the first.</summary>
    private static uint? ReadHardwareVendor(IDXCoreAdapter adapter)
        => Read(adapter, Property.HardwareId, 16, b => (uint)Marshal.ReadInt32(b));

    private static T? Read<T>(IDXCoreAdapter adapter, uint property, int size, Func<nint, T> read) where T : struct
    {
        if (!adapter.IsPropertySupported(property)) return null;
        var buffer = Marshal.AllocHGlobal(size);
        try { return adapter.GetProperty(property, (nuint)size, buffer) < 0 ? null : read(buffer); }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("dxcore.dll", ExactSpelling = true)]
    private static extern int DXCoreCreateAdapterFactory(in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    private delegate int CuDriverGetVersion(out int version);

    [ComImport, Guid("78ee5945-c36e-4b13-a669-005dd11c0f06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXCoreAdapterFactory
    {
        [PreserveSig] int CreateAdapterList(uint numAttributes, [MarshalAs(UnmanagedType.LPArray)] Guid[] filterAttributes,
                                            in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object list);
    }

    [ComImport, Guid("526c7776-40e9-459b-b711-f32ad76dfc28"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXCoreAdapterList
    {
        [PreserveSig] int GetAdapter(uint index, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object adapter);
        [PreserveSig] uint GetAdapterCount();
    }

    [ComImport, Guid("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXCoreAdapter
    {
        [PreserveSig] [return: MarshalAs(UnmanagedType.U1)] bool IsValid();
        [PreserveSig] [return: MarshalAs(UnmanagedType.U1)] bool IsAttributeSupported(in Guid attribute);
        [PreserveSig] [return: MarshalAs(UnmanagedType.U1)] bool IsPropertySupported(uint property);
        [PreserveSig] int GetProperty(uint property, nuint bufferSize, nint data);
        [PreserveSig] int GetPropertySize(uint property, out nuint size);
    }
}
