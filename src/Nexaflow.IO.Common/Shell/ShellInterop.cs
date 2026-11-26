using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Nexaflow.Icons;

namespace Nexaflow.IO.Common.Shell;

/// <summary>
/// What the shell says about a file, or about a kind of file: its icon and its type name, through <c>SHGetFileInfo</c>,
/// the call Explorer's own lists are drawn from. The only native surface of the shell layer. An icon is copied out of its
/// handle into <see cref="ShellIconPixels"/> through GDI, so nothing points at the handle once it is destroyed and the
/// picture can cross from the thread that drew it to the one that shows it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShellInterop
{
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const int SmallIconWidthMetric = 49;

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
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, InfoFlags uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, IntPtr phiconSmall, uint nIconSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

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
    public static ShellIconPixels? TypeIcon(string extension, bool isFolder, ShellIconSize size) =>
        Icon(StandIn(extension, isFolder), Attributes(isFolder), InfoFlags.UseFileAttributes | SizeFlag(size));

    /// <summary>The icon of the file or folder at <paramref name="path"/> itself — a program's own, a shortcut's target's
    /// with the shortcut arrow, a customised folder's. Reads the disk, so only ever called off the UI thread.</summary>
    public static ShellIconPixels? PathIcon(string path, ShellIconSize size) =>
        Icon(path, 0, InfoFlags.LinkOverlay | SizeFlag(size));

    /// <summary>
    /// The icon <paramref name="icon"/> names, drawn <paramref name="pixels"/> wide — the file's icon at that index or, for a
    /// negative index, the one with that resource id, as the shell reads a <c>DefaultIcon</c>. Reads the disk, so only ever
    /// called off the UI thread.
    /// </summary>
    public static ShellIconPixels? ResourceIcon(ShellIconRef icon, int pixels)
    {
        var file = Environment.ExpandEnvironmentVariables(icon.File);
        // The size's low word is the large icon's; with no small icon asked for, the high word is unread.
        if (SHDefExtractIcon(file, icon.Index, 0, out var hIcon, IntPtr.Zero, (uint)pixels) != 0 || hIcon == IntPtr.Zero)
            return null;
        try { return Detached(hIcon); }
        finally { DestroyIcon(hIcon); }
    }

    private static ShellIconPixels? Icon(string path, uint attributes, InfoFlags flags)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, attributes, ref info, InfoSize, flags | InfoFlags.Icon) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;
        try { return Detached(info.hIcon); }
        finally { DestroyIcon(info.hIcon); }
    }

    /// <summary>
    /// The icon's pixels, premultiplied. Its colour bitmap carries alpha when the icon has any; one that has none is opaque
    /// where its mask is black. A monochrome icon has no colour bitmap: its mask is twice the height, the mask above its
    /// picture.
    /// </summary>
    private static ShellIconPixels? Detached(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var info)) return null;
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (info.hbmColor == IntPtr.Zero)
            {
                if (Pixels(dc, info.hbmMask) is not { } masked) return null;
                var (maskWidth, maskHeight, both) = masked;
                int height = maskHeight / 2;
                var mono = new byte[maskWidth * height * 4];
                int half = maskWidth * height * 4;
                for (int i = 0; i < mono.Length; i += 4)
                {
                    bool shown = both[i] == 0;   // the mask's black is where the picture shows
                    byte value = both[half + i];
                    mono[i] = mono[i + 1] = mono[i + 2] = shown ? value : (byte)0;
                    mono[i + 3] = shown ? (byte)255 : (byte)0;
                }
                return new ShellIconPixels(maskWidth, height, mono);
            }

            if (Pixels(dc, info.hbmColor) is not { } drawn) return null;
            var (width, height2, colour) = drawn;
            bool hasAlpha = false;
            for (int i = 3; i < colour.Length && !hasAlpha; i += 4) hasAlpha = colour[i] != 0;

            if (!hasAlpha)
            {
                var mask = info.hbmMask != IntPtr.Zero ? Pixels(dc, info.hbmMask) : null;
                for (int i = 0; i < colour.Length; i += 4)
                    colour[i + 3] = mask is { } m && m.Bits[i] != 0 ? (byte)0 : (byte)255;
            }

            for (int i = 0; i < colour.Length; i += 4)
            {
                int alpha = colour[i + 3];
                if (alpha == 255) continue;
                colour[i]     = (byte)(colour[i]     * alpha / 255);
                colour[i + 1] = (byte)(colour[i + 1] * alpha / 255);
                colour[i + 2] = (byte)(colour[i + 2] * alpha / 255);
            }
            return new ShellIconPixels(width, height2, colour);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
        }
    }

    /// <summary>A bitmap's pixels as 32-bit BGRA, top row first.</summary>
    private static (int Width, int Height, byte[] Bits)? Pixels(IntPtr dc, IntPtr bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), out var described) == 0) return null;
        int width = described.bmWidth, height = Math.Abs(described.bmHeight);
        var header = new BITMAPINFOHEADER
        {
            biSize     = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth    = width,
            biHeight   = -height,   // negative: top row first
            biPlanes   = 1,
            biBitCount = 32,
        };
        var bits = new byte[width * height * 4];
        return GetDIBits(dc, bitmap, 0, (uint)height, bits, ref header, 0) == height ? (width, height, bits) : null;
    }

    /// <summary>A name the shell reads only for its kind — with <c>UseFileAttributes</c> nothing by that name has to exist.</summary>
    private static string StandIn(string extension, bool isFolder) => isFolder ? "folder" : "file" + extension;

    private static uint Attributes(bool isFolder) => isFolder ? FileAttributeDirectory : FileAttributeNormal;

    private static InfoFlags SizeFlag(ShellIconSize size) => size == ShellIconSize.Small ? InfoFlags.SmallIcon : InfoFlags.LargeIcon;
}
