namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// What is done to pages as sheets: the pages printed on one cut apart, several put on one, one set on paper of another
/// size, or its content set within margins.
/// </summary>
public enum PdfSheetEdit
{
    /// <summary>Each page cut into the pages printed on it.</summary>
    Split = 0,

    /// <summary>Pages put several to a sheet.</summary>
    Merge = 1,

    /// <summary>Each page set on paper of another size.</summary>
    Size = 2,

    /// <summary>Each page's content moved, and made smaller where it must be, to sit within margins on its paper.</summary>
    Margins = 3,
}
