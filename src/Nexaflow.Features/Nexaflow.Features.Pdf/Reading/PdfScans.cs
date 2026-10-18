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
/// A document's scanned pages — pages with no text on them and one picture over all of each, but a margin — and the
/// resolution each was scanned at.
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

    /// <summary>
    /// How many of its own pixels a scanned page's picture puts in each point of the page — the resolution it was scanned
    /// at, over 72 — or null where the page has no picture, or its placement cannot be read. Its longer sides are measured
    /// against each other, so a picture placed turned is measured as it was scanned.
    /// </summary>
    public static double? ScanResolution(PdfDocument document, int number)
    {
        if (document.GetPage(number).GetImages().FirstOrDefault() is not { } image) return null;
        var placed = Math.Max(image.Bounds.Width, image.Bounds.Height);
        return placed > 0 ? Math.Max(image.WidthInSamples, image.HeightInSamples) / placed : null;
    }
}
