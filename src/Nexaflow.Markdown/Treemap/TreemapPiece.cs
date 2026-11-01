namespace Nexaflow.Markdown.Treemap;

/// <summary>
/// The pieces a treemap's layout is made of: the map, each item a tile in it — a section holding the tiles of the items in it —
/// and, under the map, the items worth nothing yet, which have no room in it.
/// </summary>
public static class TreemapPiece
{
    /// <summary>The map: every item worth something, each a tile as big as its share. It stands for nothing written.</summary>
    public const string Map = "Map";

    /// <summary>
    /// A section: its tile, the band across the top of it with its label and total, and the tiles of the items in it. It stands for
    /// the section with everything under it, so choosing it washes the whole tile, and a press on a tile inside it means that tile.
    /// </summary>
    public const string Section = "Section";

    /// <summary>A leaf: its tile, its label and its value in it. It stands for the leaf as it was written.</summary>
    public const string Leaf = "Leaf";

    /// <summary>
    /// A leaf's words — its label's lines and its value — stood together apart from its tile, so the caret steps up and down among
    /// them rather than past the tile they are drawn on. It stands for nothing written.
    /// </summary>
    public const string Words = "Words";

    /// <summary>An item's label — typed into where it is drawn.</summary>
    public const string Label = "Label";

    /// <summary>A leaf's value, set as the front matter formats it — pressed, shown as written and typed into.</summary>
    public const string Value = "Value";

    /// <summary>What a section and everything in it come to, in its band — worked out, never a place for the caret.</summary>
    public const string Total = "Total";

    /// <summary>
    /// Under the map, the items worth nothing yet — a section with nothing in it, a leaf whose value is nought or still to write —
    /// each a chip of its own, so it can still be picked out, written in and given something. It stands for nothing written.
    /// </summary>
    public const string Empty = "Empty";
}
