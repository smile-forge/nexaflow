namespace Nexaflow.Markdown.Code;

/// <summary>What a piece of code is — to the tree it is read into, and to the layout it is set in.</summary>
public static class CodeKinds
{
    /// <summary>A whole stretch of code.</summary>
    public const string Code = "code";

    /// <summary>Where one line of it ends and the next begins: a line feed, and the return before it where there is one.</summary>
    public const string LineEnd = "line-end";

    /// <summary>
    /// One line of a stretch of code, its line end with it where it has one: a block of its own, so a keystroke lays the line it was
    /// typed on and sets down every line that reads as it did as it was laid.
    /// </summary>
    public const string Line = "code-line";

    /// <summary>
    /// Hung on a stretch of code read a stretch at a time that the next stretch carries on from: the line after its last line end is
    /// the next stretch's first, so it is not laid as a line of this one.
    /// </summary>
    public const string CarriedOn = "carried-on";
}
