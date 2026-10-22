namespace Nexaflow.Features.Pdf.Editing;

/// <summary>Which way a page is cut into parts.</summary>
public enum PdfSplitDirection
{
    /// <summary>Across its longer side: a page wider than it is tall side by side, a taller one one above another.</summary>
    Auto = 0,

    /// <summary>Into parts side by side, cut from top to bottom.</summary>
    Across = 1,

    /// <summary>Into parts one above another, cut from side to side.</summary>
    Down = 2,
}
