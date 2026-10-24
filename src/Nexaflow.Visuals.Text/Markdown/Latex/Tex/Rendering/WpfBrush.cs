using System.Windows.Media;
using Nexaflow.Markdown.Typesetting.Tex.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;

public sealed record WpfBrush : GenericBrush<Brush>
{
    private WpfBrush(Brush brush) : base(brush)
    {
    }

    public static WpfBrush FromBrush(Brush value) => new(value);
}
