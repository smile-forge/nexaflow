using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Nexaflow.Icons;

namespace Nexaflow.Visuals.Icons;

/// <summary>
/// What a button shows: an icon, and beside it a label where one is given — the icon alone for what it plainly says, the two
/// together for what it does not. Both take the button's foreground; the label its font size, the icon a little larger.
/// </summary>
public sealed class IconLabel : StackPanel
{
    private readonly IconGlyph _glyph = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

    public IconLabel()
    {
        Orientation = Orientation.Horizontal;
        Children.Add(_glyph);
        Children.Add(_label);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconRef), typeof(IconLabel),
        new PropertyMetadata(default(IconRef), (d, e) => ((IconLabel)d)._glyph.Icon = (IconRef)e.NewValue));

    public IconRef Icon
    {
        get => (IconRef)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(IconLabel),
        new PropertyMetadata(null, (d, e) => ((IconLabel)d).ShowText((string?)e.NewValue)));

    /// <summary>The label beside the icon; none, the icon alone — the button's tooltip and accessible name then say what it does.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The label's size — the icon stays a little larger — inherited from the button unless set.</summary>
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(IconLabel));

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private void ShowText(string? text)
    {
        _label.Text = text ?? string.Empty;
        _label.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
