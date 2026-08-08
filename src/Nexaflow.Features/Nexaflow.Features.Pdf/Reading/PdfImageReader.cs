using System;
using System.Collections.Generic;
using System.Threading;
using Nexaflow.IO.Common;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>One extractable image from a PDF, ready to be written as-is.</summary>
/// <param name="PageNumber">1-based page it was first seen on.</param>
/// <param name="IndexOnPage">1-based position among that page's images.</param>
/// <param name="Bytes">The exact bytes to write.</param>
/// <param name="Extension">File extension including the dot, matching <paramref name="Bytes"/>.</param>
internal readonly record struct PdfImageData(
    int PageNumber, int IndexOnPage, ReadOnlyMemory<byte> Bytes, string Extension)
{
    /// <summary>Zero-padded so a directory listing sorts into reading order past page 9 and image 9.</summary>
    public string SuggestedFileName => $"p{PageNumber:000}-{IndexOnPage:00}{Extension}";
}

/// <summary>How a run of image extraction went.</summary>
/// <param name="Extracted">Images yielded.</param>
/// <param name="Duplicates">Images skipped because an identical one had already been yielded.</param>
/// <param name="Undecodable">Images skipped because no codec here could turn them into a file.</param>
internal readonly record struct PdfImageTally(int Extracted, int Duplicates, int Undecodable);

/// <summary>
/// Pulls the embedded images out of a PDF. Shell-free and synchronous, so it can be tested directly and the
/// caller decides where it runs.
/// </summary>
internal static class PdfImageReader
{
    /// <summary>
    /// Every distinct image in the document, in page order.
    /// <para>
    /// Deduplicated by content hash, which is not a nicety: a PDF references one logo XObject from every
    /// page, so a 40-page report would otherwise extract the same header image 40 times and bury the three
    /// pictures someone actually wanted.
    /// </para>
    /// </summary>
    public static IEnumerable<PdfImageData> Read(
        PdfDocument document, Action<PdfImageTally>? onFinished, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int extracted = 0, duplicates = 0, undecodable = 0;

        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<IPdfImage> images;
            try { images = ReadPageImages(document, pageNumber); }
            catch (OperationCanceledException) { throw; }
            catch { continue; }   // one bad page shouldn't cost the rest of the document

            var indexOnPage = 0;
            foreach (var image in images)
            {
                ct.ThrowIfCancellationRequested();
                indexOnPage++;

                ReadOnlyMemory<byte> bytes;
                string extension;
                try
                {
                    if (!TryGetWritableBytes(image, out bytes, out extension)) { undecodable++; continue; }
                }
                catch { undecodable++; continue; }

                if (!seen.Add(Hashing.Sha256(bytes.Span))) { duplicates++; continue; }

                extracted++;
                yield return new PdfImageData(pageNumber, indexOnPage, bytes, extension);
            }
        }

        onFinished?.Invoke(new PdfImageTally(extracted, duplicates, undecodable));
    }

    // Separated so the try/catch around it can live in a method that isn't an iterator.
    private static IReadOnlyList<IPdfImage> ReadPageImages(PdfDocument document, int pageNumber)
    {
        var images = new List<IPdfImage>();
        foreach (var image in document.GetPage(pageNumber).GetImages()) images.Add(image);
        return images;
    }

    /// <summary>
    /// Bytes we can write to a file, preferring the encoded form the PDF already holds.
    /// <para>
    /// When a PDF stores a photo it almost always stores a JPEG verbatim, so writing those bytes straight out
    /// is both faster and lossless — re-encoding to PNG would inflate the file and throw away nothing but
    /// fidelity. Detected by signature rather than by reading the <c>/Filter</c> array, because that handles
    /// the awkward cases for free: a JPEG with a second filter stacked on top won't look like a JPEG here and
    /// correctly falls through to full decoding.
    /// </para>
    /// </summary>
    private static bool TryGetWritableBytes(
        IPdfImage image, out ReadOnlyMemory<byte> bytes, out string extension)
    {
        var raw = image.RawMemory;
        if (IsJpeg(raw.Span))      { bytes = raw; extension = ".jpg"; return true; }
        if (IsJpeg2000(raw.Span))  { bytes = raw; extension = ".jp2"; return true; }

        // Everything else gets decoded and re-encoded as PNG. This is the branch that carries scanned
        // documents, and it only succeeds because the JBIG2/JPX/DCT filter packages are referenced — PdfPig's
        // built-in stubs for those codecs decline, and the image would be lost.
        if (image.TryGetPng(out var png) && png is { Length: > 0 })
        {
            bytes = png; extension = ".png"; return true;
        }

        bytes = default; extension = string.Empty; return false;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> b)
        => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    // Either the JP2 container's signature box or a bare JPEG 2000 codestream.
    private static bool IsJpeg2000(ReadOnlySpan<byte> b)
        => (b.Length >= 12 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0x00 && b[3] == 0x0C
            && b[4] == 0x6A && b[5] == 0x50 && b[6] == 0x20 && b[7] == 0x20)
           || (b.Length >= 4 && b[0] == 0xFF && b[1] == 0x4F && b[2] == 0xFF && b[3] == 0x51);
}
