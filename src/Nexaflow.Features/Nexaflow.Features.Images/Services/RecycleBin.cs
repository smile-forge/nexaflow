using System;
using System.Runtime.InteropServices;

namespace Nexaflow.Features.Images.Services;

/// <summary>
/// Sends a file to the Windows Recycle Bin via <c>SHFileOperation</c> (so a delete is undoable).
/// Mirrors the file browser's delete; the Images feature can't reference that assembly, so the
/// minimal interop lives here.
/// </summary>
internal static class RecycleBin
{
    public static bool TryRecycle(string path)
    {
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc  = FO_DELETE,
                pFrom  = path + "\0\0",   // pFrom must be double-null-terminated
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint   wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string  pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private const uint   FO_DELETE          = 0x0003;
    private const ushort FOF_ALLOWUNDO      = 0x0040;   // send to recycle bin
    private const ushort FOF_NOCONFIRMATION = 0x0010;   // we show our own confirmation
    private const ushort FOF_NOERRORUI      = 0x0400;
    private const ushort FOF_SILENT         = 0x0004;
}
