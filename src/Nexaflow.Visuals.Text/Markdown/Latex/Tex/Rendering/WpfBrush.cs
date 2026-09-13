using System.Windows.Media;
using XamlMath.Rendering;

namespace WpfMath.Rendering;

public sealed record WpfBrush : GenericBrush<Brush>
{
    private WpfBrush(Brush brush) : base(brush)
    {
    }

    public static WpfBrush FromBrush(Brush value) => new(value);
}
