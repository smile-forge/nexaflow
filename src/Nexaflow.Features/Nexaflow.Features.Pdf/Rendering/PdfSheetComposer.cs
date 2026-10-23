using System.Text;
using System.Windows;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// A sheet of pieces of pages made into the one page it shows, from each piece read onto the sheet
/// (<see cref="PdfReadingPlacement"/>), in the sheet's order.
/// </summary>
internal static class PdfSheetComposer
{
    /// <summary>
    /// The sheet, <paramref name="size"/> in points: every piece's content inside its own bounds — a page read in part
    /// still draws what lies outside the part, which the page's own edge hides when it is shown alone; their annotations,
    /// each link saying which piece it is on, since that is what says which document its target is a page of; and their
    /// text, each piece's on a line after the one before's.
    /// </summary>
    public static PdfPageNode Compose(Size size, IReadOnlyList<(PdfPageNode Read, Rect Bounds)> pieces)
    {
        var content = new List<PdfContentNode>(pieces.Count);
        var annotations = new List<PdfAnnotationNode>();
        var unsupported = new List<PdfUnsupported>();
        var text = new StringBuilder();
        for (var i = 0; i < pieces.Count; i++)
        {
            var (read, bounds) = pieces[i];
            if (i > 0) text.Append('\n');
            text.Append(read.Text);

            content.Add(new PdfGroupNode(PdfGroupRole.Clip, [read.Content], clip: Rectangle(bounds)));
            var piece = i;
            annotations.AddRange(read.Annotations.Select(annotation => annotation.Link is PdfPageTarget target
                ? annotation with { Link = target with { Piece = piece } }
                : annotation));
            unsupported.AddRange(read.Unsupported);
        }

        var first = pieces.Count > 0 ? pieces[0].Read : null;
        return new PdfPageNode(first?.Number ?? 0, size, first?.FromUser ?? System.Windows.Media.Matrix.Identity,
            new PdfGroupNode(PdfGroupRole.Root, content), annotations, text.ToString(), unsupported);
    }

    private static PdfShape Rectangle(Rect bounds)
    {
        var writer = new PdfShape.Writer();
        writer.MoveTo(bounds.Left, bounds.Top);
        writer.LineTo(bounds.Right, bounds.Top);
        writer.LineTo(bounds.Right, bounds.Bottom);
        writer.LineTo(bounds.Left, bounds.Bottom);
        writer.Close();
        return writer.Take();
    }
}
