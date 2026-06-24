namespace Nexaflow.IO.Common;

/// <summary>
/// A document's line-terminator convention. <see cref="Preserve"/> means "leave whatever is already
/// there" — used as the editor's default so opening and saving a file doesn't silently rewrite its EOLs.
/// </summary>
public enum LineEnding
{
    Preserve,
    Lf,
    CrLf,
    Cr,
}

/// <summary>The line terminator(s) actually present in a document — for a read-only status indicator.
/// <see cref="None"/> means no terminators (a single line); <see cref="Mixed"/> means more than one kind.</summary>
public enum LineEndingKind
{
    None,
    Lf,
    CrLf,
    Cr,
    Mixed,
}

public static class LineEndingExtensions
{
    /// <summary>The terminator character sequence. Throws for <see cref="LineEnding.Preserve"/>, which has none.</summary>
    public static string ToSequence(this LineEnding eol) => eol switch
    {
        LineEnding.Lf   => "\n",
        LineEnding.CrLf => "\r\n",
        LineEnding.Cr   => "\r",
        _ => throw new System.ArgumentOutOfRangeException(nameof(eol), eol, "Preserve has no sequence."),
    };
}
