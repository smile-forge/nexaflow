using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Wraps the shell's <c>SHAssocEnumHandlers</c> API to list the executable apps Windows has
/// registered as handlers for a file extension. Used to seed the "Define New" wizard's
/// existing-app picker with system-registered apps (when registry mapping is on).
/// COM calls must run on the STA UI thread.
/// </summary>
internal static class ShellAssocHandlers
{
    /// <summary>
    /// Returns (friendly name, exe path) for each recommended .exe handler of
    /// <paramref name="extension"/> (e.g. ".slnx"). Non-exe handlers (UWP/protocol) are skipped
    /// because the launcher starts a process by path. Returns empty on any failure.
    /// </summary>
    public static IReadOnlyList<(string DisplayName, string Path)> ForExtension(string extension)
    {
        var results = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(extension)) return results;
        if (!extension.StartsWith('.')) extension = "." + extension;

        try
        {
            if (SHAssocEnumHandlers(extension, ASSOC_FILTER.RECOMMENDED, out var en) != 0 || en is null)
                return results;

            try
            {
                var batch = new IAssocHandler[1];
                while (en.Next(1, batch, out uint fetched) == 0 && fetched == 1)
                {
                    var h = batch[0];
                    try
                    {
                        h.GetName(out var path);
                        if (string.IsNullOrEmpty(path) ||
                            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            continue;

                        h.GetUIName(out var ui);
                        var name = string.IsNullOrWhiteSpace(ui) ? Path.GetFileNameWithoutExtension(path) : ui;
                        results.Add((name, path));
                    }
                    finally { Marshal.ReleaseComObject(h); }
                }
            }
            finally { Marshal.ReleaseComObject(en); }
        }
        catch { /* shell unavailable / unexpected handler — return what we have */ }

        return results;
    }

    // ── Interop ───────────────────────────────────────────────────────────────

    private enum ASSOC_FILTER { NONE = 0, RECOMMENDED = 1 }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHAssocEnumHandlers(
        [MarshalAs(UnmanagedType.LPWStr)] string pszExtra,
        ASSOC_FILTER afFilter,
        out IEnumAssocHandlers ppEnumHandler);

    [ComImport, Guid("973810ae-9599-4b88-9e4d-6ee98c9552da"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumAssocHandlers
    {
        [PreserveSig] int Next(uint celt,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IAssocHandler[] rgelt,
            out uint pceltFetched);
    }

    // Only the leading vtable methods we call are declared (GetName, GetUIName); the remaining
    // IAssocHandler members are intentionally omitted.
    [ComImport, Guid("F04061AC-1659-4a3f-A954-775AA57FC083"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAssocHandler
    {
        [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string ppsz);
        [PreserveSig] int GetUIName([MarshalAs(UnmanagedType.LPWStr)] out string ppsz);
    }
}
