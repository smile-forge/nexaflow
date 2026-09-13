using XamlMath.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace XamlMath.Boxes;

/// <summary>Box representing horizontal line.</summary>
internal sealed class HorizontalRule : Box
{
    public HorizontalRule(TexEnvironment environment, double thickness, double width, double shift)
    {
        this.Width = width;
        this.Height = thickness;
        this.Shift = shift;
        this.Foreground = environment.Foreground;
        this.Background = environment.Background;	//Not strictly necessary
    }

    internal override void Lay(LatexCapture layer, double x, double y)
    {
        var rectangle = new Rectangle(x, y - this.Height, this.Width, this.Height);
        layer.Rule(rectangle, Foreground);
    }

    public override int GetLastFontId()
    {
        return TexFontUtilities.NoFontId;
    }
}
