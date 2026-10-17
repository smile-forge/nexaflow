using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// A document's scanned pages — pages with no text on them and one picture over all of each, but a margin — and each one's
/// picture as a page reader takes it: as the JPEG it is stored as, or decoded to PNG, turned as the page is shown.
/// </summary>
internal static class PdfScans
{
    /// <summary>The scanned pages, by number. A picture whose placement cannot be read counts as covering its page:
    /// one picture and no text is a scan however it is drawn.</summary>
    public static IReadOnlyList<int> Find(PdfDocument document, double coverage, CancellationToken ct)
    {
        var scans = new List<int>();
        for (int number = 1; number <= document.NumberOfPages; number++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var page = document.GetPage(number);
                if (page.Letters.Count > 0) continue;
                var images = page.GetImages().Take(2).ToList();
                if (images.Count != 1) continue;
                double covered = PdfImageReader.PageCoverage(images[0], page.Width * page.Height);
                if (covered >= coverage || covered == 0) scans.Add(number);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* a page that will not parse is not offered for reading */ }
        }
        return scans;
    }

    /// <summary>The scanned page's picture, JPEG or PNG, upright as the page is shown; null when no codec here decodes it.</summary>
    public static byte[]? Picture(PdfDocument document, int number)
    {
        var page = document.GetPage(number);
        if (page.GetImages().FirstOrDefault() is not { } image) return null;
        var raw = image.RawMemory;
        byte[]? picture = IsJpeg(raw.Span) ? raw.ToArray()
                        : image.TryGetPng(out var png) && png is { Length: > 0 } ? png
                        : null;
        int turn = ((page.Rotation.Value % 360) + 360) % 360;
        return picture is null || turn == 0 ? picture : Turned(picture, turn);
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) => bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    /// <summary>The picture turned clockwise by <paramref name="degrees"/>, as PNG.</summary>
    private static byte[] Turned(byte[] picture, int degrees)
    {
        using var input = new MemoryStream(picture);
        var frame = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
        var turned = new TransformedBitmap(frame, new RotateTransform(degrees));
        turned.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(turned));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
