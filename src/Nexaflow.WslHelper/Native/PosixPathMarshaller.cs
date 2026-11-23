using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.WslHelper.Native;

/// <summary>
/// Hands a path to the C library as the exact bytes it names, NUL-terminated: a name that is no UTF-8 — held in .NET as
/// <see cref="PosixNames"/> holds it — reaches the kernel as the bytes the distro holds, where the runtime's UTF-8
/// marshalling would write U+FFFD in their place and name some other file, or none.
/// </summary>
[CustomMarshaller(typeof(string), MarshalMode.ManagedToUnmanagedIn, typeof(PosixPathMarshaller))]
internal static unsafe class PosixPathMarshaller
{
    public static byte* ConvertToUnmanaged(string? managed)
    {
        if (managed is null) return null;
        int count = PosixNames.GetByteCount(managed);
        byte* native = (byte*)NativeMemory.Alloc((nuint)count + 1);
        PosixNames.GetBytes(managed, new Span<byte>(native, count));
        native[count] = 0;
        return native;
    }

    public static void Free(byte* unmanaged) => NativeMemory.Free(unmanaged);
}
