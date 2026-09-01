using XamlMath.Rendering;

namespace XamlMath.Boxes;

/// <summary>
/// The hollow box standing where an argument has still to be written.
/// <para>
/// An empty argument sets as nothing at all, so <c>\frac{}{}</c> draws a bar with two invisible sides —
/// a formula a reader cannot see, cannot aim at and cannot tell from a broken one. A box gives the hole
/// a size and a place, which is all anything else needs to treat it as an ordinary symbol.
/// </para>
/// <para>
/// Drawn as four hairlines rather than a filled block or a glyph: filled would read as content, and a
/// glyph would be content — something a reader could leave in place and something the source would have
/// to carry. This is the shape every equation editor has used for the same purpose for thirty years, so
/// it needs no explaining.
/// </para>
/// </summary>
internal sealed class PlaceholderBox : Box
{
    /// <summary>How far across the em the box runs, and how far up. Squat enough to read as a slot.</summary>
    private const double WidthInEm = 0.55;
    private const double HeightInEm = 0.62;

    /// <summary>Line thickness, as a share of the box's height. Thin enough not to read as ink.</summary>
    private const double Hairline = 0.07;

    private readonly double _thickness;

    public PlaceholderBox(TexEnvironment environment)
    {
        var size = environment.MathFont.GetXHeight(environment.Style, environment.LastFontId);

        Width = size * (WidthInEm / HeightInEm);
        Height = size;
        Depth = 0;
        _thickness = System.Math.Max(size * Hairline, 0.4);

        Foreground = environment.Foreground;
        Background = environment.Background;
    }

    public override void RenderTo(IElementRenderer renderer, double x, double y)
    {
        var top = y - Height;

        // Four sides. The renderer draws filled rectangles, so an outline is four thin ones — which
        // also means every consumer that already understands a rule understands this.
        renderer.RenderRectangle(new Rectangle(x, top, Width, _thickness), Foreground);
        renderer.RenderRectangle(new Rectangle(x, y - _thickness, Width, _thickness), Foreground);
        renderer.RenderRectangle(new Rectangle(x, top, _thickness, Height), Foreground);
        renderer.RenderRectangle(new Rectangle(x + Width - _thickness, top, _thickness, Height), Foreground);
    }

    public override int GetLastFontId() => TexFontUtilities.NoFontId;
}
