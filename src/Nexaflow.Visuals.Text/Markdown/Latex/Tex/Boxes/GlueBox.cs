using XamlMath.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace XamlMath.Boxes;

// Box representing glue.
internal sealed class GlueBox : Box
{
    public GlueBox(double space, double stretch, double shrink)
    {
        this.Width = space;
        this.Stretch = stretch;
        this.Shrink = shrink;
    }

    public double Stretch { get; }

    public double Shrink { get; }

    internal override void Lay(LatexCapture layer, double x, double y)
    {
    }

    public override int GetLastFontId()
    {
        return TexFontUtilities.NoFontId;
    }
}
