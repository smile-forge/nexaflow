using System.Windows.Media;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// How a page is read when it is read onto a sheet rather than as itself: the part of it read, as its crop box; where that
/// part lies on the sheet, so everything read of it — what it draws, its annotations, its glyphs' boxes — comes out in the
/// sheet's space; and where its text starts in the sheet's, so a glyph names its stretch of the sheet's text.
/// </summary>
/// <param name="Part">The part of the page read.</param>
/// <param name="ToSheet">From the part as shown, in points from its top left, to the sheet.</param>
/// <param name="TextStart">How much of the sheet's text comes before this page's.</param>
internal sealed record PdfReadingPlacement(PdfPagePart Part, Matrix ToSheet, int TextStart)
{
    /// <summary>The <paramref name="part"/> of a page read as itself.</summary>
    public static PdfReadingPlacement Alone(PdfPagePart? part) => new(part ?? PdfPagePart.Whole, Matrix.Identity, 0);
}
