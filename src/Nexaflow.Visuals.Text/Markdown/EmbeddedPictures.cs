using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Nexaflow.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A picture written into a document's own source (<see cref="EmbeddedPicture"/>), decoded into something to draw: a PNG,
/// a JPEG or a bitmap by WPF's own decoders, and a Windows metafile — which WPF cannot read — played by GDI onto a bitmap.
///
/// <para>
/// <strong>Decoded once.</strong> A document is laid out again whenever its room changes, and the same reading of it holds
/// the same pictures, so what each decodes to is kept for as long as the picture is.
/// </para>
/// </summary>
public static class EmbeddedPictures
{
    /// <summary>The widest or tallest a metafile is played at, in pixels, whatever size it says it is.</summary>
    private const int Largest = 4096;

    private static readonly ConditionalWeakTable<EmbeddedPicture, Decoding> Decoded = new();

    /// <summary>What was made of one picture: the bitmap, or nothing where its bytes are not a picture this machine reads.</summary>
    private sealed record Decoding(BitmapSource? Bitmap);

    /// <summary>
    /// <paramref name="picture"/>, decoded and drawn at the size its writer asked for — measured in the size the source calls
    /// ordinary text, which is drawn at <paramref name="textSize"/> — or its own size where they did not say. Null where its
    /// bytes are not a picture this machine can read.
    /// </summary>
    public static ImageSource? Drawn(EmbeddedPicture picture, double textSize)
    {
        if (Decoded.GetValue(picture, static read => new Decoding(Decode(read))).Bitmap is not { } bitmap) return null;

        var (wide, tall) = Size(picture, bitmap, textSize);

        if (Math.Abs(wide - bitmap.Width) < 0.5 && Math.Abs(tall - bitmap.Height) < 0.5) return bitmap;

        // Drawn at the size asked for rather than resampled to it, so it stays as sharp as its pixels allow.
        var sized = new DrawingImage(new ImageDrawing(bitmap, new Rect(0, 0, wide, tall)));
        sized.Freeze();

        return sized;
    }

    /// <summary>How big it is drawn: as asked, keeping its shape where only one side was asked for.</summary>
    private static (double Wide, double Tall) Size(EmbeddedPicture picture, BitmapSource bitmap, double textSize)
    {
        var wide = picture.Width is > 0 ? picture.Width.Value * textSize : (double?)null;
        var tall = picture.Height is > 0 ? picture.Height.Value * textSize : (double?)null;

        if (wide is null && tall is null) return (bitmap.Width, bitmap.Height);

        var aspect = bitmap.Height > 0 ? bitmap.Width / bitmap.Height : 1;

        return (wide ?? tall!.Value * aspect, tall ?? wide!.Value / aspect);
    }

    private static BitmapSource? Decode(EmbeddedPicture picture)
    {
        try
        {
            return picture.MediaType switch
            {
                "image/emf" => Played(EnhancedFrom(picture.Bytes)),
                "image/wmf" => Played(EnhancedFrom(picture.Bytes, picture.Metafile)),
                _ => Bitmap(picture.Bytes),
            };
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException
                                       or InvalidOperationException or ExternalException or OverflowException)
        {
            // Not a picture, or not one this machine reads: nothing is drawn for it.
            return null;
        }
    }

    /// <summary>A PNG, a JPEG, a BMP, a GIF or a TIFF — whatever WPF's decoders read — frozen so any thread can draw it.</summary>
    private static BitmapSource Bitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);

        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                                         BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();

        return frame;
    }

    // ── Metafiles ───────────────────────────────────────────────────────────

    /// <summary>An enhanced metafile's bytes, as GDI holds one.</summary>
    private static IntPtr EnhancedFrom(byte[] bytes) => Native.SetEnhMetaFileBits((uint)bytes.Length, bytes);

    /// <summary>
    /// A Windows metafile's bytes, made into an enhanced one with what playing it needs said about it — its mapping mode and
    /// extent, which a metafile written into RTF carries beside it rather than in a header of its own.
    /// </summary>
    private static IntPtr EnhancedFrom(byte[] bytes, PictureMetafile? played)
    {
        var picture = new Native.MetafilePicture
        {
            Mapping = played?.Mapping is > 0 and var mapping ? mapping : Native.Anisotropic,
            Width = played?.Width ?? 0,
            Height = played?.Height ?? 0,
        };

        return Native.SetWinMetaFileBits((uint)bytes.Length, bytes, IntPtr.Zero, ref picture);
    }

    /// <summary>
    /// An enhanced metafile played onto a white bitmap at its own size, at the reader's pixel density — and let go of, whatever
    /// happens. Null where GDI could not read it.
    /// </summary>
    private static BitmapSource? Played(IntPtr metafile)
    {
        if (metafile == IntPtr.Zero) return null;

        try
        {
            var header = new byte[Native.HeaderSize];
            if (Native.GetEnhMetaFileHeader(metafile, (uint)header.Length, header) == 0) return null;

            // Its frame, in hundredths of a millimetre.
            var frame = MemoryMarshal.Cast<byte, int>(header.AsSpan(Native.FrameAt, 16));
            var wide = Math.Clamp((int)Math.Round((frame[2] - frame[0]) / 2540.0 * 96), 1, Largest);
            var tall = Math.Clamp((int)Math.Round((frame[3] - frame[1]) / 2540.0 * 96), 1, Largest);

            return Painted(metafile, wide, tall);
        }
        finally
        {
            Native.DeleteEnhMetaFile(metafile);
        }
    }

    /// <summary>The metafile played onto a <paramref name="wide"/> by <paramref name="tall"/> bitmap, white where it draws nothing.</summary>
    private static BitmapSource? Painted(IntPtr metafile, int wide, int tall)
    {
        var dc = Native.CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;

        var info = new Native.BitmapInfo { Size = 40, Width = wide, Height = -tall, Planes = 1, BitCount = 32 };
        var section = Native.CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);

        try
        {
            if (section == IntPtr.Zero) return null;

            var old = Native.SelectObject(dc, section);
            var stride = wide * 4;
            var pixels = new byte[stride * tall];

            // White underneath, as the page the writer drew it on was.
            Array.Fill(pixels, (byte)0xFF);
            Marshal.Copy(pixels, 0, bits, pixels.Length);

            var area = new Native.Rectangle { Right = wide, Bottom = tall };
            Native.PlayEnhMetaFile(dc, metafile, ref area);
            Native.GdiFlush();

            Marshal.Copy(bits, pixels, 0, pixels.Length);
            Native.SelectObject(dc, old);

            // GDI leaves the fourth byte of what it drew empty, so the pixels are read as having none.
            var bitmap = BitmapSource.Create(wide, tall, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            bitmap.Freeze();

            return bitmap;
        }
        finally
        {
            if (section != IntPtr.Zero) Native.DeleteObject(section);
            Native.DeleteDC(dc);
        }
    }

    private static class Native
    {
        /// <summary><c>MM_ANISOTROPIC</c>: what a metafile written into RTF is played in where it says nothing.</summary>
        public const int Anisotropic = 8;

        /// <summary>How much of an <c>ENHMETAHEADER</c> is read, and where in it the frame is.</summary>
        public const int HeaderSize = 108;

        public const int FrameAt = 24;

        [StructLayout(LayoutKind.Sequential)]
        public struct MetafilePicture
        {
            public int Mapping;
            public int Width;
            public int Height;
            public IntPtr Handle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfo
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("gdi32.dll")] public static extern IntPtr SetEnhMetaFileBits(uint size, byte[] bytes);
        [DllImport("gdi32.dll")] public static extern IntPtr SetWinMetaFileBits(uint size, byte[] bytes, IntPtr reference, ref MetafilePicture picture);
        [DllImport("gdi32.dll")] public static extern uint GetEnhMetaFileHeader(IntPtr metafile, uint size, byte[] header);
        [DllImport("gdi32.dll")] public static extern bool DeleteEnhMetaFile(IntPtr metafile);
        [DllImport("gdi32.dll")] public static extern bool PlayEnhMetaFile(IntPtr dc, IntPtr metafile, ref Rectangle area);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    }
}
