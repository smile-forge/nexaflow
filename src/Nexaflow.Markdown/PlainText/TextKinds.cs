namespace Nexaflow.Markdown.PlainText;

/// <summary>What a piece of plain text is — to the tree it is read into, and to the layout it is set in.</summary>
public static class TextKinds
{
    /// <summary>A whole stretch of text.</summary>
    public const string Text = "text";

    /// <summary>
    /// One line of a stretch of text, its line end with it where it has one: a block of its own, so a keystroke lays the line it was
    /// typed on and sets down every line that reads as it did as it was laid.
    /// </summary>
    public const string Line = "text-line";

    /// <summary>A word of a line: a run of its characters between spaces — what a spelling, or a name, is looked for in.</summary>
    public const string Word = "text-word";
}
