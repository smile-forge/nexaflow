using System.Collections.Generic;
using XamlMath.Rendering;

namespace XamlMath.Boxes;

// Box that draws the rules of an array - the vertical ones asked for by | in the preamble, the
// horizontal ones by \hline - and nothing else. Layered over the grid it belongs to, so the rules
// span the whole of it rather than being cut into the rows.
internal sealed class GridRulesBox : Box
{
    private readonly IReadOnlyList<double> _verticalAt;
    private readonly IReadOnlyList<double> _horizontalAt;
    private readonly double _thickness;

    /// <param name="verticalAt">X offsets, measured from the left edge of the grid.</param>
    /// <param name="horizontalAt">Y offsets, measured down from the top edge of the grid.</param>
    public GridRulesBox(
        TexEnvironment environment,
        IReadOnlyList<double> verticalAt,
        IReadOnlyList<double> horizontalAt,
        double thickness)
    {
        _verticalAt = verticalAt;
        _horizontalAt = horizontalAt;
        _thickness = thickness;
        this.Foreground = environment.Foreground;
        this.Background = environment.Background;
    }

    public override void RenderTo(IElementRenderer renderer, double x, double y)
    {
        var top = y - this.Height;
        var total = this.Height + this.Depth;

        foreach (var offset in _verticalAt)
            renderer.RenderRectangle(new Rectangle(x + offset, top, _thickness, total), this.Foreground);

        foreach (var offset in _horizontalAt)
            renderer.RenderRectangle(new Rectangle(x, top + offset, this.Width, _thickness), this.Foreground);
    }

    public override int GetLastFontId()
    {
        return TexFontUtilities.NoFontId;
    }
}
