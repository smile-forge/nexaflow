namespace Nexaflow.Features.Pdf.Editing;

/// <summary>What order the pages split off sheets are put in.</summary>
public enum PdfSplitOrder
{
    /// <summary>As they lie on each sheet, left to right and top to bottom, sheet after sheet.</summary>
    AsTheyLie = 0,

    /// <summary>The sheets are a folded booklet's, printed for a stapler: their halves put into reading order.</summary>
    FoldedBooklet = 1,

    /// <summary>
    /// The sheets were printed on both sides and cut into leaves: each front followed by the back after it, each leaf's
    /// front then its back (<see cref="PdfCutLeaves"/>).
    /// </summary>
    DoubleSidedCut = 2,
}
