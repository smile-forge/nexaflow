namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// A mark drawn at a point: what a scatter plot tells its groups apart by.
///
/// <para>
/// Deliberately not a <see cref="DiagramShape"/>, though three of them draw as one. A node shape is
/// <em>sized around words</em> — <see cref="DiagramShapes.Around"/> and <see cref="DiagramShapes.Inside"/>
/// exist to make one big enough to hold a label, and every shape is held to it. A glyph is the opposite
/// thing: a mark a few pixels across that holds nothing at all, and whose whole job is to stay legible
/// at that size. Putting the two in one enum makes every glyph owe an answer to a question it has no
/// business being asked.
/// </para>
/// <para>
/// Where a glyph and a node shape are the same outline, the outline is the node shape's — there is no
/// second circle here.
/// </para>
/// </summary>
internal enum DiagramGlyph
{
    Circle,
    Square,
    Diamond,
    Triangle,
    TriangleDown,
    Plus,
    Cross,

    /// <summary>A regular hexagon standing on a point, which is what a hexagonal bin is drawn as.</summary>
    Hexagon,
}
