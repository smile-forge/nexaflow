namespace Nexaflow.Markdown.Layout;

/// <summary>
/// A place a caret may rest: against one edge of a piece. A place is a piece rather than a bare offset because one character
/// boundary can be several places on the page — after a script raised half a line, and back on the line after the term it finishes.
/// </summary>
/// <param name="Against">What the caret stands against.</param>
/// <param name="Trailing">Whether it stands after the piece rather than before it.</param>
public readonly record struct CaretPlace(LayoutPiece Against, bool Trailing)
{
    public bool Exists => Against.Exists;

    /// <summary>The offset in the source a caret standing here is at.</summary>
    public int Offset
    {
        get
        {
            var at = Against.Sits();
            return Trailing ? at.Start + at.Length : at.Start;
        }
    }
}
