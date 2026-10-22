namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// Why a page edit could not be made.
/// </summary>
internal enum PdfEditRefusal
{
    /// <summary>The file is not a PDF that can be read — damaged, or locked with a password.</summary>
    Unreadable,

    /// <summary>The PDF has an XFA form, which lays out the whole document itself and cannot follow its pages.</summary>
    XfaForm,

    /// <summary>The file is not a picture this machine can decode.</summary>
    NotAPicture,

    /// <summary>Another program has the file open for writing, so edits that depend on it cannot be held to it.</summary>
    InUse,
}

/// <summary>
/// A page edit refused, and the file it was refused for.
/// </summary>
internal sealed class PdfEditRefusedException(string path, PdfEditRefusal reason, Exception? inner = null)
    : Exception($"'{path}' cannot be edited in: {reason}.", inner)
{
    public string Path { get; } = path;

    public PdfEditRefusal Reason { get; } = reason;
}

/// <summary>
/// What a save will change about the file beyond its pages, for the reader to agree to first.
/// </summary>
[Flags]
internal enum PdfSaveWarnings
{
    None = 0,

    /// <summary>A document the pages come from is encrypted; the file written is not.</summary>
    Encrypted = 1,

    /// <summary>The document is signed; a file written anew invalidates its signatures.</summary>
    Signed = 2,
}
