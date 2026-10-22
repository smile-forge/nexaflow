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
/// A page of the edited document: which page of which source it is — or which part of it, for a page split from a sheet
/// printed several to a page.
/// </summary>
internal readonly record struct PdfArrangedPage(PdfPageId Id, PdfSourceId Source, int Number, PdfPagePart? Part = null);
