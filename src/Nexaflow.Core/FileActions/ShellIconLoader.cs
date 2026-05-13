using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Core.FileActions;

/// <summary>
/// Loads a 32×32 icon from a path string of the form used by DefaultIcon registry values
/// (e.g.  "C:\Windows\System32\shell32.dll,42"  or  "C:\path\to\app.exe,0").
/// Results are cached and the returned <see cref="ImageSource"/> is always frozen so it
/// can be accessed from any thread.
/// </summary>
internal static class ShellIconLoader
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int PrivateExtractIcons(
        string lpszFile, int nIconIndex, int cxIcon, int cyIcon,
        [Out] IntPtr[] phicon, [Out] int[] piconid, int nIcons, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Parses <paramref name="iconSpec"/> and returns a frozen WPF <see cref="ImageSource"/>,
    /// or <c>null</c> if the icon cannot be loaded.
    /// </summary>
    public static ImageSource? Load(string iconSpec)
    {
        if (string.IsNullOrWhiteSpace(iconSpec)) return null;
        return _cache.GetOrAdd(iconSpec, LoadCore);
    }

    private static ImageSource? LoadCore(string iconSpec)
    {
        try
        {
            // Expand environment variables that sometimes appear in registry values
            var spec = Environment.ExpandEnvironmentVariables(iconSpec.Trim('"'));

            // Strip surrounding quotes if present
            if (spec.StartsWith('"') && spec.EndsWith('"'))
                spec = spec[1..^1];

            string filePath;
            int    index = 0;

            // Format is  "path,index"  or just  "path"
            var lastComma = spec.LastIndexOf(',');
            if (lastComma >= 0 && int.TryParse(spec[(lastComma + 1)..].Trim(), out int parsed))
            {
                filePath = spec[..lastComma].Trim();
                index    = parsed;
            }
            else
            {
                filePath = spec;
            }

            if (string.IsNullOrEmpty(filePath)) return null;

            var hicons  = new IntPtr[1];
            var iconIds = new int[1];

            int count = PrivateExtractIcons(filePath, Math.Abs(index), 32, 32, hicons, iconIds, 1, 0);
            if (count <= 0 || hicons[0] == IntPtr.Zero) return null;

            try
            {
                using var icon = System.Drawing.Icon.FromHandle(hicons[0]);
                var bitmap = icon.ToBitmap();
                return BitmapFromBitmap(bitmap);
            }
            finally
            {
                DestroyIcon(hicons[0]);
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource BitmapFromBitmap(Bitmap bmp)
    {
        var hbmp = bmp.GetHbitmap(System.Drawing.Color.Transparent);
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            NativeDeleteObject(hbmp);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static void NativeDeleteObject(IntPtr h)
    {
        try { DeleteObject(h); } catch { }
    }
}
