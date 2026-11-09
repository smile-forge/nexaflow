using System;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>How a picture put in place of one of a document's images is encoded.</summary>
internal enum PdfPictureFormat
{
    /// <summary>A JPEG file, written into the document as it is.</summary>
    Jpeg,

    /// <summary>A PNG file, written into the document as compressed pixels, its alpha a soft mask.</summary>
    Png,
}

/// <summary>
/// A picture put in place of one of a document's images: its file, as the document is written with it, and — decoded
/// once, when first drawn — the picture its pages draw in the image's place, filling the place the image had.
/// Immutable; an edit that changes the picture makes another.
/// </summary>
internal sealed class PdfImageReplacement
{
    private readonly Lazy<(PdfPicture? Picture, bool HasAlpha)> _decoded;

    private PdfImageReplacement(byte[] bytes, PdfPictureFormat format)
    {
        Bytes = bytes;
        Format = format;
        Made = DateTime.Now;
        _decoded = new Lazy<(PdfPicture?, bool)>(Decode, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The picture file, as it was given.</summary>
    public byte[] Bytes { get; }

    public PdfPictureFormat Format { get; }

    /// <summary>When the picture was put in place, local time: its file's modified time.</summary>
    public DateTime Made { get; }

    /// <summary>The extension a file of it is named with.</summary>
    public string Extension => Format == PdfPictureFormat.Jpeg ? ".jpg" : ".png";

    /// <summary>The picture the pages draw in the image's place; null when its file cannot be decoded here.</summary>
    public PdfPicture? Picture => _decoded.Value.Picture;

    /// <summary>Whether the picture has transparency of its own — a soft mask in the document — so no other masks it.</summary>
    public bool HasAlpha => _decoded.Value.HasAlpha;

    /// <summary><paramref name="bytes"/> as a picture to put in an image's place; null when they are no JPEG or PNG file.</summary>
    public static PdfImageReplacement? Of(byte[] bytes)
    {
        if (IsJpeg(bytes)) return new PdfImageReplacement(bytes, PdfPictureFormat.Jpeg);
        if (IsPng(bytes)) return new PdfImageReplacement(bytes, PdfPictureFormat.Png);
        return null;
    }

    private (PdfPicture?, bool) Decode()
    {
        try
        {
            using var stream = new MemoryStream(Bytes, writable: false);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var hasAlpha = Format == PdfPictureFormat.Png && CarriesAlpha(frame.Format);
            var pixels = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            pixels.Freeze();
            return (PdfPicture.Of(pixels), hasAlpha);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException)
        {
            return (null, false);
        }
    }

    private static bool CarriesAlpha(PixelFormat format) =>
        format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 || format == PixelFormats.Rgba64 || format == PixelFormats.Prgba64
        || format == PixelFormats.Rgba128Float || format == PixelFormats.Prgba128Float;

    private static bool IsJpeg(ReadOnlySpan<byte> b) => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    private static bool IsPng(ReadOnlySpan<byte> b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;
}
