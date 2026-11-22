using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Nexaflow.Visuals.Common.Dropping;

/// <summary>What a drop would do, said beside the pointer while it hovers. A popup, so it is above every surface.</summary>
internal sealed class DropHint(UIElement root)
{
    private const double Right = 14, Below = 18;

    private Popup? _popup;
    private TextBlock? _words;

    public void Show(string? words, Point at)
    {
        if (string.IsNullOrEmpty(words))
        {
            Hide();
            return;
        }

        var popup = _popup ??= Build();
        popup.HorizontalOffset = at.X + Right;
        popup.VerticalOffset   = at.Y + Below;
        _words!.Text = words;
        popup.IsOpen = true;
    }

    public void Hide()
    {
        if (_popup is { } popup) popup.IsOpen = false;
    }

    private Popup Build()
    {
        _words = new TextBlock { FontSize = 12 };
        _words.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var card = new Border { Child = _words, Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        return new Popup { Child = card, PlacementTarget = root, Placement = PlacementMode.Relative, AllowsTransparency = true, IsHitTestVisible = false };
    }
}
