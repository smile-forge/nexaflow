using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// PNGs of 16 bits a channel, read and written exactly through Windows Imaging Component: Skia writes a 16-bit colour PNG but
/// reads one back only as half floats, which hold fewer values, and writes no 16-bit grey at all.
/// </summary>
/// <remarks>WIC objects belong to the thread that made them; everything here is made and finished on the caller's.</remarks>
public static class DeepPng
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>What a PNG's header says of its pixels — bits a channel, and whether they are grey (with or without alpha) — or
    /// null where it is no PNG.</summary>
    public static (int Bits, bool Grey)? HeaderOf(ReadOnlySpan<byte> png)
    {
        if (png.Length < 26 || !png[..8].SequenceEqual(Signature)) return null;
        return (png[24], png[25] is 0 or 4);
    }

    /// <summary>Whether <paramref name="png"/> holds more than 8 bits a channel.</summary>
    public static bool IsDeep(ReadOnlySpan<byte> png) => HeaderOf(png) is { Bits: 16 };

    /// <summary>A PNG's pixels as 16-bit red, green, blue and alpha, not premultiplied — grey spread to all three, an 8-bit
    /// PNG's values widened exactly — and its size.</summary>
    /// <exception cref="InvalidDataException">It is not a PNG Windows reads.</exception>
    public static (ushort[] Rgba, int Width, int Height) Read(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        BitmapFrame frame;
        try
        {
            frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException)
        {
            throw new InvalidDataException($"A layer's pixels are not a picture: {ex.Message}");
        }
        int width = frame.PixelWidth, height = frame.PixelHeight;
        var rgba = new ushort[width * height * 4];
        new FormatConvertedBitmap(frame, PixelFormats.Rgba64, null, 0).CopyPixels(rgba, width * 8, 0);
        return (rgba, width, height);
    }

    /// <summary>
    /// 16-bit pixels as a 16-bit PNG: <paramref name="rgba"/> — red, green, blue and alpha, not premultiplied — as colour; or,
    /// <paramref name="grey"/> and wholly opaque, as 16-bit grey, which holds them in a quarter of the bytes.
    /// </summary>
    public static byte[] Write(ReadOnlySpan<ushort> rgba, int width, int height, bool grey)
    {
        BitmapSource source;
        if (grey && Opaque(rgba))
        {
            var greys = new ushort[width * height];
            for (int i = 0; i < greys.Length; i++) greys[i] = rgba[i * 4];
            source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray16, null, greys, width * 2);
        }
        else
        {
            source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgba64, null, rgba.ToArray(), width * 8);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static bool Opaque(ReadOnlySpan<ushort> rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
            if (rgba[i] != ushort.MaxValue) return false;
        return true;
    }

    /// <summary>16-bit pixels laid out as their bytes, as a tile of 16-bit colour holds them.</summary>
    public static ReadOnlySpan<byte> AsBytes(ushort[] samples) => MemoryMarshal.AsBytes(samples.AsSpan());
}
