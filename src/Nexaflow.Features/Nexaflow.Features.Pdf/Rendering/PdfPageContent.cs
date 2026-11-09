using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// Where a page's content is: the box everything on it covers that is not the paper's own colour, found from a picture of
/// the page as the reader draws it — so a background filled with the paper's colour, or a path that draws nothing, is not
/// taken for content, as the page's objects' own bounds would take it.
/// </summary>
internal static class PdfPageContent
{
    /// <summary>The longer side of the picture a page's content is found in, in pixels: fine enough to place it to a point or so.</summary>
    private const int Longest = 1000;

    /// <summary>
    /// The box page <paramref name="index"/>'s content covers, in points on the page as shown; null for a page with nothing
    /// on it.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">The page's document can no longer be read.</exception>
    public static async Task<Rect?> FindAsync(PdfPageSource pages, int index, CancellationToken ct)
    {
        var dips = pages.PageSize(index);
        var size = new Size(dips.Width / PdfPageLayout.DipsPerPoint, dips.Height / PdfPageLayout.DipsPerPoint);
        var width = System.Math.Max(1, (int)System.Math.Round(Longest * size.Width / System.Math.Max(size.Width, size.Height)));
        var extent = await pages.PaintedAsync(index, width, ct).ConfigureAwait(false) is { } picture
            ? await Task.Run(() => PdfInk.Of(picture).Extent(), ct).ConfigureAwait(false)
            : null;
        if (extent is not { } part) return null;

        // A pixel more all round, held to the page: the faintest edge of a letter, lighter than the ink it is told by, is kept.
        var pixel = size.Width / width;
        var box = new Rect(part.X * size.Width - pixel, part.Y * size.Height - pixel, part.Width * size.Width + 2 * pixel, part.Height * size.Height + 2 * pixel);
        box.Intersect(new Rect(size));
        return box;
    }
}
