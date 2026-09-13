using XamlMath.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace XamlMath.Boxes;

// Box that draws a rectangular frame around its own extent and nothing else. Layered over the content it
// frames, it gives \boxed its border.
internal sealed class FrameBox : Box
{
    private readonly double _thickness;

    public FrameBox(TexEnvironment environment, double thickness)
    {
        _thickness = thickness;
        this.Foreground = environment.Foreground;
        this.Background = environment.Background;
    }

    internal override void Lay(LatexCapture layer, double x, double y)
    {
        var top = y - this.Height;
        var totalHeight = this.Height + this.Depth;

        layer.Rule(new Rectangle(x, top, this.Width, _thickness), this.Foreground);
        layer.Rule(
            new Rectangle(x, top + totalHeight - _thickness, this.Width, _thickness), this.Foreground);
        layer.Rule(new Rectangle(x, top, _thickness, totalHeight), this.Foreground);
        layer.Rule(
            new Rectangle(x + this.Width - _thickness, top, _thickness, totalHeight), this.Foreground);
    }

    public override int GetLastFontId()
    {
        return TexFontUtilities.NoFontId;
    }
}
