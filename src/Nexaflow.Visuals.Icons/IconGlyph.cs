using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Visuals.Icons;

/// <summary>
/// Draws an <see cref="IconRef"/>: the emoji in the surrounding font, or the Fluent glyph in the bundled font. It is a
/// <see cref="TextBlock"/>, so <c>FontSize</c> and <c>Foreground</c> size and tint it like any text.
/// </summary>
public class IconGlyph : TextBlock
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconRef), typeof(IconGlyph),
        new PropertyMetadata(default(IconRef), (d, _) => ((IconGlyph)d).Apply()));

    public IconRef Icon
    {
        get => (IconRef)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    private void Apply()
    {
        var icon = Icon;
        if (IconCatalog.FontFor(icon) is { } font) FontFamily = font;
        else ClearValue(FontFamilyProperty);
        Text = IconCatalog.GlyphFor(icon);
    }
}
