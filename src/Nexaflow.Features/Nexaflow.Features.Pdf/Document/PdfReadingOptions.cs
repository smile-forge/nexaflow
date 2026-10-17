using UglyToad.PdfPig;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// The options a document is opened with, carrying what its pages share when they are read as
/// <see cref="PdfPageNode"/>s. PdfPig hands its options to the page factories it makes, which is the one way into a
/// factory it constructs itself.
/// </summary>
internal sealed class PdfReadingOptions : ParsingOptions
{
    /// <summary>The caches this document's pages share.</summary>
    public PdfReadingCaches Caches { get; } = new();

    /// <summary>Options under which colours an ICC profile describes are converted by Windows' colour engine.</summary>
    public PdfReadingOptions() => IccProfileService = PdfColourEngine.Shared;
}
