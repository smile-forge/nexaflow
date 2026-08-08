using Nexaflow.Features.Common;

namespace Nexaflow.Features.Pdf;

/// <summary>
/// Settings for PDF text extraction. Global rather than <c>[WorkspaceScopedConfig]</c> — a PDF parses the
/// same way whichever workspace happens to be asking.
/// </summary>
public sealed class PdfConfig : IFeatureConfig
{
    public string ConfigName   => "pdf";
    public string FriendlyName => "PDF";

    /// <summary>
    /// Above this size a search sweep skips the file rather than parsing it. The parse itself can't be
    /// interrupted once it starts — the whole cross-reference table has to be read before any page is
    /// available — so the only cheap defence against one enormous PDF stalling a sequential sweep is to
    /// decline before opening it. A skipped file is reported as unreadable, never as "no match".
    /// </summary>
    public int SearchMaxFileSizeMb { get; set; } = 128;

    /// <summary>
    /// Whether the extracted text is prefixed with the document's title/author/subject/keywords, its bookmark
    /// titles and its filled-in form field values. On by default: it is what makes a document findable by its
    /// title, and a completed form findable by what was typed into it, when the page bodies say neither.
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;
}
