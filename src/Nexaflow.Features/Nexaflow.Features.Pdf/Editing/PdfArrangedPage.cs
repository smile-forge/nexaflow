using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// One of the documents an edited document's pages come from: the file opened, or a PDF inserted into it.
/// </summary>
internal readonly record struct PdfSourceId(int Value);

/// <summary>
/// A page as it is known through every edit: it keeps its id however it moves, through undo and through a save, and a
/// page inserted is a new one.
/// </summary>
internal readonly record struct PdfPageId(int Value);

/// <summary>
/// A page of the edited document: the sheet it shows — a page of a source as it is, a part of one split from a sheet
/// printed several to a page, or pages merged onto one sheet.
/// </summary>
internal readonly record struct PdfArrangedPage(PdfPageId Id, PdfSheet Sheet)
{
    /// <summary>Page <paramref name="number"/> of <paramref name="source"/>, or the <paramref name="part"/> of it, as it is.</summary>
    public PdfArrangedPage(PdfPageId id, PdfSourceId source, int number, PdfPagePart? part = null)
        : this(id, PdfSheet.Page(source, number, part))
    {
    }
}
