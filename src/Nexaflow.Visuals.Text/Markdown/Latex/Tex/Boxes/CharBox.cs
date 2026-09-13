using System.Collections.Generic;
using XamlMath.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace XamlMath.Boxes;

/// <summary>Box representing single character.</summary>
internal sealed class CharBox : Box
{
    public CharBox(TexEnvironment environment, CharInfo charInfo)
        : base(environment)
    {
        this.Character = charInfo;
        this.Width = charInfo.Metrics.Width;
        this.Height = charInfo.Metrics.Height;
        this.Depth = charInfo.Metrics.Depth;
        this.Italic = charInfo.Metrics.Italic;
    }

    public CharInfo Character { get; }

    internal override void Lay(LatexCapture layer, double x, double y)
    {
        layer.Glyph(Character, x, y, this.Foreground);
    }

    public override int GetLastFontId()
    {
        return this.Character.FontId;
    }
}
