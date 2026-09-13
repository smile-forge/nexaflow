using XamlMath.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace XamlMath.Boxes;

// Box representing whitespace.
internal sealed class StrutBox : Box
{
    public static StrutBox Empty { get; } = new StrutBox(0, 0, 0, 0);

    public StrutBox(double width, double height, double depth, double shift)
    {
        this.Width = width;
        this.Height = height;
        this.Depth = depth;
        this.Shift = shift;
    }

    internal override void Lay(LatexCapture layer, double x, double y)
    {
    }

    public override int GetLastFontId()
    {
        return TexFontUtilities.NoFontId;
    }
}
