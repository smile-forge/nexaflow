namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Which side of the block a piece stands against — the block being the width its content is laid into.
///
/// <para>
/// Most things are placed where they are put. This is for the few whose place is an edge of the page rather
/// than a point on it: an equation's number, flush with the right however wide the column is. The layout
/// resolves it against the width the block turns out to be, so nothing that puts a piece there has to work
/// out where that is. See <see cref="LayoutBuilder.Against"/>.
/// </para>
/// </summary>
public enum Side
{
    Left,
    Centre,
    Right,
}
