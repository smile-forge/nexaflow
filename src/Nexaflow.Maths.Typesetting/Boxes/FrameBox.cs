using XamlMath.Rendering;

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

    public override void RenderTo(IElementRenderer renderer, double x, double y)
    {
        var top = y - this.Height;
        var totalHeight = this.Height + this.Depth;

        renderer.RenderRectangle(new Rectangle(x, top, this.Width, _thickness), this.Foreground);
        renderer.RenderRectangle(
            new Rectangle(x, top + totalHeight - _thickness, this.Width, _thickness), this.Foreground);
        renderer.RenderRectangle(new Rectangle(x, top, _thickness, totalHeight), this.Foreground);
        renderer.RenderRectangle(
            new Rectangle(x + this.Width - _thickness, top, _thickness, totalHeight), this.Foreground);
    }

    public override int GetLastFontId()
    {
        return TexFontUtilities.NoFontId;
    }
}
