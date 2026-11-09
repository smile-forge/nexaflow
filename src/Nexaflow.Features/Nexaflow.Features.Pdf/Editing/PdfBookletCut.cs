namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// How a booklet's sheets are printed: each on a paper of its own, or two to a paper, one above the other, for a booklet
/// half the paper's size — the pile printed then cut across.
/// </summary>
public enum PdfBookletCut
{
    /// <summary>Each booklet sheet printed on a paper of its own.</summary>
    Uncut = 0,

    /// <summary>
    /// Two booklet sheets to a paper, put so that once the pile is cut across and its top halves laid on its bottom halves,
    /// the sheets lie in the order an uncut booklet's print in: one booklet.
    /// </summary>
    Stacked = 1,

    /// <summary>Each booklet sheet printed twice to a paper: cut across, two booklets.</summary>
    Copies = 2,
}
