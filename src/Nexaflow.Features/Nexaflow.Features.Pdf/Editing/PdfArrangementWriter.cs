using System.IO;
using System.Windows.Media;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Reading;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// A source as the writer reads it: its file, and how its content is tagged when the document written is a tagged PDF
/// and the source is not — a picture inserted becomes a Figure.
/// </summary>
internal sealed record PdfSourceFile(string Path, AssembledContentTag? Tag);

/// <summary>
/// Writes an arrangement as one PDF, through the PdfPig fork's assembler: the pages in order from each source, and
/// everything the sources hold beyond their pages carried to where each page now is. Each source is opened afresh for
/// the write — never the reader's own instance, which is not thread-safe — so the reader goes on while a document is
/// written.
/// </summary>
internal static class PdfArrangementWriter
{
    /// <param name="arrangement">The pages, in order.</param>
    /// <param name="sources">Every source a page is from, and the primary.</param>
    /// <param name="primary">The source whose outline, form, names and settings come first: the file the tab shows.</param>
    /// <param name="output">Where the document is written; left open.</param>
    /// <exception cref="IOException">A source cannot be read.</exception>
    /// <exception cref="AssemblyRefusedException">A source cannot be assembled from.</exception>
    public static void Write(
        PdfPageArrangement arrangement,
        IReadOnlyDictionary<PdfSourceId, PdfSourceFile> sources,
        PdfSourceId primary,
        Stream output,
        CancellationToken ct)
    {
        var opened = new Dictionary<PdfSourceId, PdfDocumentScope>();
        try
        {
            foreach (var id in arrangement.Pages.SelectMany(page => page.Sheet.Pieces).Select(piece => piece.Source).Prepend(primary).Distinct())
            {
                opened[id] = PdfDocumentScope.TryOpen(sources[id].Path, ct)
                             ?? throw new IOException($"'{sources[id].Path}' could not be read.");
            }

            var pages = arrangement.Pages.Select(page => Assembled(page.Sheet, opened, sources)).ToList();

            PdfDocumentAssembler.Assemble(pages, output, new AssemblyOptions { Primary = opened[primary].Document }, ct);
        }
        finally
        {
            foreach (var scope in opened.Values)
                scope.Dispose();
        }
    }

    /// <summary>
    /// A sheet as it is written: a page as it is, or a part of one, as itself; any other sheet as a page of its size with
    /// each of its pieces placed on it — its place turned from the sheet's top-left, y-down space into PDF's, which runs up
    /// from the bottom left of the sheet and of the piece alike.
    /// </summary>
    private static AssembledSheet Assembled(
        PdfSheet sheet, Dictionary<PdfSourceId, PdfDocumentScope> opened, IReadOnlyDictionary<PdfSourceId, PdfSourceFile> sources)
    {
        if (sheet.IsPlain) return AssembledSheet.Of(Assembled(sheet.Pieces[0], opened, sources));

        PdfShownSize shown = (source, number) => opened[source].Pages.Slot(number).Size;
        var size = sheet.ShownSize(shown);
        var upSheet = new Matrix(1, 0, 0, -1, 0, size.Height);
        var placements = sheet.Pieces
            .Select(piece =>
            {
                var place = new Matrix(1, 0, 0, -1, 0, piece.Size(shown).Height) * piece.Place.Matrix * upSheet;
                return new AssembledPlacement(Assembled(piece, opened, sources),
                    TransformationMatrix.FromValues(place.M11, place.M12, place.M21, place.M22, place.OffsetX, place.OffsetY));
            })
            .ToList();
        return new AssembledSheet(size.Width, size.Height, placements);
    }

    /// <summary>A piece's page, showing only its part.</summary>
    private static AssembledPage Assembled(
        PdfSheetPiece piece, Dictionary<PdfSourceId, PdfDocumentScope> opened, IReadOnlyDictionary<PdfSourceId, PdfSourceFile> sources)
    {
        var scope = opened[piece.Source];
        var slot = scope.Pages.Slot(piece.Number);
        return new AssembledPage(scope.Document, piece.Number, sources[piece.Source].Tag,
            piece.Part == PdfPagePart.Whole ? null : piece.Part.InUserSpace(slot.Crop, slot.Rotation));
    }
}
