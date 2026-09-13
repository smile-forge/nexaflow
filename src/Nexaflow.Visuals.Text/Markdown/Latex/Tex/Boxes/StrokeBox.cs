using System;
using XamlMath.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace XamlMath.Boxes;

internal sealed class StrokeBox : Box
{
    private readonly StrokeBoxMode _mode;

    public StrokeBox(StrokeBoxMode mode)
    {
        _mode = mode;
    }

    internal override void Lay(LatexCapture layer, double x, double y)
    {
        if (_mode.HasFlag(StrokeBoxMode.Normal))
            layer.Line(new Point(x, y + Depth), new Point(x + Width, y - Height), Foreground);

        if (_mode.HasFlag(StrokeBoxMode.Back))
            layer.Line(new Point(x, y - Height), new Point(x + Width, y + Depth), Foreground);
    }

    public override int GetLastFontId()
    {
        return TexFontUtilities.NoFontId;
    }
}

[Flags]
internal enum StrokeBoxMode
{
    None = 0,
    Normal = 1,
    Back = 2,
    Both = 3
}
