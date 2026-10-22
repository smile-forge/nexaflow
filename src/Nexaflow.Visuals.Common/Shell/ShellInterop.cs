using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>
/// What the shell says about a file, or about a kind of file: its icon and its type name, through <c>SHGetFileInfo</c>,
/// the call Explorer's own lists are drawn from. The only native surface of the shell layer.
/// </summary>
internal static class ShellInterop
{
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal    = 0x80;
    private const int  SmallIconWidthMetric   = 49;   // SM_CXSMICON

    [Flags]
    private enum InfoFlags : uint
    {
        LargeIcon         = 0x0000,
        SmallIcon         = 0x0001,
        UseFileAttributes = 0x0010,
        Icon              = 0x0100,
        TypeName          = 0x0400,
        LinkOverlay       = 0x8000,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, InfoFlags uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private static readonly uint InfoSize = (uint)Marshal.SizeOf<SHFILEINFO>();

    /// <summary>The edge, in pixels, of the shell's small icon at the system DPI — the size its lists draw a row's icon at.</summary>
    public static int SmallIconPixels { get; } = Math.Max(16, GetSystemMetrics(SmallIconWidthMetric));

    /// <summary>
    /// The shell's name for files ending <paramref name="extension"/> ("Text Document"), or for a folder. Read from the
    /// registry alone, never the disk, so it is safe from any thread and for names that are not on disk. Empty when the
    /// shell gives none.
    /// </summary>
    public static string TypeName(string extension, bool isFolder)
    {
        var info = new SHFILEINFO();
        return SHGetFileInfo(StandIn(extension, isFolder), Attributes(isFolder), ref info, InfoSize,
                             InfoFlags.TypeName | InfoFlags.UseFileAttributes) == IntPtr.Zero
            ? string.Empty
            : info.szTypeName ?? string.Empty;
    }

    /// <summary>The icon the shell gives every file ending <paramref name="extension"/>, or every plain folder — from the
    /// registry, never the disk.</summary>
    public static BitmapSource? TypeIcon(string extension, bool isFolder, ShellIconSize size) =>
        Icon(StandIn(extension, isFolder), Attributes(isFolder), InfoFlags.UseFileAttributes | SizeFlag(size));

    /// <summary>The icon of the file or folder at <paramref name="path"/> itself — a program's own, a shortcut's target's
    /// with the shortcut arrow, a customised folder's. Reads the disk, so only ever called off the UI thread.</summary>
    public static BitmapSource? PathIcon(string path, ShellIconSize size) =>
        Icon(path, 0, InfoFlags.LinkOverlay | SizeFlag(size));

    private static BitmapSource? Icon(string path, uint attributes, InfoFlags flags)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, attributes, ref info, InfoSize, flags | InfoFlags.Icon) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;
        try { return Detached(info.hIcon); }
        finally { DestroyIcon(info.hIcon); }
    }

    /// <summary>
    /// The icon copied into a frozen bitmap of its own: nothing still points at the handle once it is destroyed, and the
    /// picture can cross from the thread that drew it to the one that shows it.
    /// </summary>
    private static BitmapSource Detached(IntPtr hIcon)
    {
        var drawn = new FormatConvertedBitmap(
            Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
            PixelFormats.Pbgra32, null, 0);
        int stride = drawn.PixelWidth * 4;
        var pixels = new byte[stride * drawn.PixelHeight];
        drawn.CopyPixels(pixels, stride, 0);

        var copy = BitmapSource.Create(drawn.PixelWidth, drawn.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        copy.Freeze();
        return copy;
    }

    /// <summary>A name the shell reads only for its kind — with <c>UseFileAttributes</c> nothing by that name has to exist.</summary>
    private static string StandIn(string extension, bool isFolder) => isFolder ? "folder" : "file" + extension;

    private static uint Attributes(bool isFolder) => isFolder ? FileAttributeDirectory : FileAttributeNormal;

    private static InfoFlags SizeFlag(ShellIconSize size) => size == ShellIconSize.Small ? InfoFlags.SmallIcon : InfoFlags.LargeIcon;
}
