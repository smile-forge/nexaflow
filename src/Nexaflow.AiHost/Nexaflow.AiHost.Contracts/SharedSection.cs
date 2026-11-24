using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// A named shared memory section, mapped whole: the host makes one for each large block of bytes a message carries, readable
/// and writable only by the accounts its descriptor names, and the app opens it by name. Its handles are safe handles, so a
/// section dropped without being disposed is still closed.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class SharedSection : MemoryManager<byte>
{
    private const uint PageReadWrite = 0x04, FileMapWrite = 0x0002, FileMapRead = 0x0004;
    private const int AlreadyExists = 183;

    private readonly SafeSectionHandle _section;
    private readonly SafeViewHandle _view;
    private readonly int _length;

    private SharedSection(SafeSectionHandle section, int length)
    {
        _section = section;
        _length = length;
        _view = MapViewOfFile(section, FileMapRead | FileMapWrite, 0, 0, (nuint)Math.Max(length, 1));
        if (_view.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            section.Dispose();
            throw new Win32Exception(error, "A shared section could not be mapped.");
        }
    }

    /// <summary>A new section <paramref name="name"/> of <paramref name="length"/> bytes, open to the accounts
    /// <paramref name="descriptor"/> (SDDL) grants.</summary>
    /// <exception cref="Win32Exception">It could not be made — or a section of that name was already there.</exception>
    public static SharedSection Create(string name, int length, string descriptor)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(descriptor, 1, out IntPtr sd, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "A shared section's descriptor does not read.");
        try
        {
            var attributes = new SecurityAttributes { Length = sizeof(SecurityAttributes), Descriptor = sd, Inherit = 0 };
            ulong size = (ulong)Math.Max(length, 1);
            var section = CreateFileMappingW(new IntPtr(-1), ref attributes, PageReadWrite, (uint)(size >> 32), (uint)size, name);
            int error = Marshal.GetLastPInvokeError();
            if (section.IsInvalid) throw new Win32Exception(error, $"Shared section {name} could not be made.");
            if (error == AlreadyExists)
            {
                section.Dispose();
                throw new Win32Exception(error, $"Shared section {name} already exists.");
            }
            return new SharedSection(section, length);
        }
        finally { LocalFree(sd); }
    }

    /// <summary>Section <paramref name="name"/>, which another process made, mapped for its first <paramref name="length"/> bytes.</summary>
    /// <exception cref="Win32Exception">It is not there, or not open to this account.</exception>
    public static SharedSection Open(string name, int length)
    {
        var section = OpenFileMappingW(FileMapRead | FileMapWrite, false, name);
        if (section.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Shared section {name} could not be opened.");
        return new SharedSection(section, length);
    }

    public override Span<byte> GetSpan() => new((void*)_view.DangerousGetHandle(), _length);

    public override MemoryHandle Pin(int elementIndex = 0) => new((byte*)_view.DangerousGetHandle() + elementIndex);

    public override void Unpin() { }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        _view.Dispose();
        _section.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        public int Inherit;
    }

    private sealed class SafeSectionHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    private sealed class SafeViewHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => UnmapViewOfFile(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeSectionHandle CreateFileMappingW(IntPtr file, ref SecurityAttributes attributes, uint protect, uint sizeHigh, uint sizeLow, string name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeSectionHandle OpenFileMappingW(uint access, bool inherit, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeViewHandle MapViewOfFile(SafeSectionHandle section, uint access, uint offsetHigh, uint offsetLow, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr view);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string descriptor, uint revision, out IntPtr securityDescriptor, IntPtr size);
}
