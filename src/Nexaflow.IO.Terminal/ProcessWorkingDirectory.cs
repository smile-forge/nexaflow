using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexaflow.IO.Terminal;

/// <summary>
/// The current folder of another process, read from that process's own parameters — what <c>SetCurrentDirectory</c> writes
/// and cmd's <c>cd</c> calls. Windows has no API for it, so it is read through the process environment block.
/// </summary>
internal static class ProcessWorkingDirectory
{
    // x64: PEB.ProcessParameters, and RTL_USER_PROCESS_PARAMETERS.CurrentDirectory.DosPath (a UNICODE_STRING).
    private const int ParametersOffset = 0x20;
    private const int DirectoryOffset  = 0x38;
    private const int PointerOffset    = 8;
    private const int StringSize       = 16;

    // The process may change folder as it is read: an answer is two reads that agree.
    private const int Attempts = 3;

    /// <summary>The folder, without a trailing separator unless it is a root; null when the process cannot be read or is
    /// 32-bit, whose folder is kept elsewhere.</summary>
    public static string? Read(SafeProcessHandle process)
    {
        try
        {
            if (!NativeMethods.RunsUnderWow64(process, out bool wow64) || wow64) return null;
            if (NativeMethods.NtQueryInformationProcess(process, NativeMethods.ProcessBasicInformation, out var basic,
                                                        Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0)
                return null;

            Span<byte> pointer = stackalloc byte[IntPtr.Size];
            if (!TryRead(process, basic.PebBaseAddress + ParametersOffset, pointer)) return null;
            var directory = (IntPtr)BinaryPrimitives.ReadInt64LittleEndian(pointer) + DirectoryOffset;

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                if (ReadString(process, directory) is { Length: > 0 } folder && folder == ReadString(process, directory))
                    return Path.TrimEndingDirectorySeparator(folder);
            }
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;   // the terminal closed meanwhile
        }
    }

    private static string? ReadString(SafeProcessHandle process, IntPtr address)
    {
        Span<byte> header = stackalloc byte[StringSize];
        if (!TryRead(process, address, header)) return null;

        int length = BinaryPrimitives.ReadUInt16LittleEndian(header);
        var buffer = (IntPtr)BinaryPrimitives.ReadInt64LittleEndian(header[PointerOffset..]);
        if (length == 0 || length % 2 != 0 || buffer == IntPtr.Zero) return null;

        var bytes = new byte[length];
        return TryRead(process, buffer, bytes) ? Encoding.Unicode.GetString(bytes) : null;
    }

    private static bool TryRead(SafeProcessHandle process, IntPtr address, Span<byte> into)
        => NativeMethods.ReadProcessMemory(process, address, ref MemoryMarshal.GetReference(into), (nuint)into.Length, out var read)
           && read == (nuint)into.Length;
}
