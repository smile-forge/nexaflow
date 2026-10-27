using Nexaflow.Markdown.Typesetting.Tex;
using Nexaflow.Markdown.Typesetting.Tex.Rendering;
using Nexaflow.Markdown.Typesetting.Tex.Rendering.Transformations;

namespace Nexaflow.Markdown.Typesetting;

/// <summary>
/// What a formula already set is laid into: a piece placed where its parent chose, and the three things a piece draws — a glyph,
/// a stroke, a filled rule. The typesetter measures and decides; whatever implements this makes the layout, for whichever
/// renderer it is laid for, so the typesetting is the same whoever draws it.
/// </summary>
/// <remarks>Coordinates are the setting's own, with <c>y</c> on the piece's baseline.</remarks>
internal interface ITexLayer
{
    /// <summary>Lays <paramref name="piece"/> with the left end of its baseline at the point given; its children place themselves.</summary>
    void Place(Set piece, double x, double y);

    /// <summary>The same, moved and turned by <paramref name="transforms"/> first.</summary>
    void PlaceTransformed(Set piece, IEnumerable<Transformation> transforms, double x, double y);

    /// <summary>One glyph, into the piece being placed.</summary>
    void DrawGlyph(CharInfo info, double x, double y, IBrush? foreground);

    /// <summary>A hairline, into the piece being placed.</summary>
    void DrawLine(Point from, Point to, IBrush? foreground);

    /// <summary>A filled rectangle, into the piece being placed.</summary>
    void DrawRule(Rectangle rectangle, IBrush? foreground);
}
