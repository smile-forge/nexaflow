namespace Nexaflow.Features.Pdf.Editing;

/// <summary>What is done to pages as sheets: the pages printed on one cut apart, several put on one, or one set on paper of another size.</summary>
public enum PdfSheetEdit
{
    /// <summary>Each page cut into the pages printed on it.</summary>
    Split = 0,

    /// <summary>Pages put several to a sheet.</summary>
    Merge = 1,

    /// <summary>Each page set on paper of another size.</summary>
    Size = 2,
}
