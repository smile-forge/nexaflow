using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Services;

/// <summary>
/// Wraps the Windows shell's <c>IShellItemImageFactory</c> so album thumbnails come from the same
/// cache Explorer uses (real image previews, falling back to the file's icon). The returned bitmap
/// is frozen, so it can be handed to the UI thread from a background populate loop.
/// </summary>
internal static class ShellThumbnail
{
    /// <summary>Returns a frozen thumbnail (≈<paramref name="size"/> px square) for <paramref name="path"/>, or null.</summary>
    public static BitmapSource? TryGet(string path, int size)
    {
        IShellItemImageFactory? factory = null;
        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (factory is null) return null;

            int hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.ResizeToFit, out hbitmap);
            if (hr != 0 || hbitmap == IntPtr.Zero) return null;

            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit   = 0x00,
        BiggerSizeOk  = 0x01,
        MemoryOnly    = 0x02,
        IconOnly      = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly   = 0x10,
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
