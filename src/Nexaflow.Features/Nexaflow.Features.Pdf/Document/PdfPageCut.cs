namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// A line a page is cut along, measured on the page as shown: from top to bottom at <see cref="At"/> of its width when
/// <see cref="Vertical"/> — between parts side by side — otherwise from side to side at <see cref="At"/> of its height.
/// </summary>
internal readonly record struct PdfPageCut(double At, bool Vertical);
