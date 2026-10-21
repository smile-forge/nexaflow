using System.IO;
using Nexaflow.Features.Pdf.Reading;
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
            foreach (var id in arrangement.Pages.Select(page => page.Source).Prepend(primary).Distinct())
            {
                opened[id] = PdfDocumentScope.TryOpen(sources[id].Path, ct)
                             ?? throw new IOException($"'{sources[id].Path}' could not be read.");
            }

            var pages = arrangement.Pages
                .Select(page => new AssembledPage(
                    opened[page.Source].Document,
                    page.Number,
                    sources[page.Source].Tag,
                    page.Part is { } part && opened[page.Source].Pages.Slot(page.Number) is var slot
                        ? part.InUserSpace(slot.Crop, slot.Rotation)
                        : null))
                .ToList();

            PdfDocumentAssembler.Assemble(pages, output, new AssemblyOptions { Primary = opened[primary].Document }, ct);
        }
        finally
        {
            foreach (var scope in opened.Values)
                scope.Dispose();
        }
    }
}
