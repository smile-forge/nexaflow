using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.AiHost.Service;

/// <summary>
/// Hands a client taken on the service's pipe to another process: the handle is duplicated into it, and the duplicate's value
/// written on the line it reads (<see cref="HandedPipes"/>) with the client's account. The caller's own handle stays its own to
/// close; the connection lives on in the other process.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PipeHandover
{
    private const uint DuplicateSameAccess = 0x2;

    /// <exception cref="Win32Exception">The handle could not be duplicated into <paramref name="into"/> — it has exited.</exception>
    /// <exception cref="IOException">Its line could not be written.</exception>
    public static void Hand(TakenClient client, SafeProcessHandle into, TextWriter control)
    {
        if (!DuplicateHandle(GetCurrentProcess(), client.Pipe, into, out IntPtr copy, 0, inheritHandle: false, DuplicateSameAccess))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The client's pipe could not be handed over");
        control.WriteLine($"{copy.ToInt64().ToString(CultureInfo.InvariantCulture)} {client.Account.Value}");
        control.Flush();
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, SafePipeHandle source, SafeProcessHandle targetProcess, out IntPtr target,
                                               uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);
}
