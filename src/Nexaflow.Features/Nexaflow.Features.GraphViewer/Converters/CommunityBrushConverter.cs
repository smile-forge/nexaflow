using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.GraphViewer.Converters;

/// <summary>
/// Maps a node's community id to a stable colour from the theme's categorical <c>Swatch.*</c> bank (so colour
/// encodes community, shape/size encodes type). Colours repeat past the palette size — adjacent communities
/// rarely collide, and the Segments rail disambiguates. Falls back to a literal when the theme can't be resolved
/// (e.g. a design-time / headless context).
/// </summary>
public sealed class CommunityBrushConverter : IValueConverter
{
    private static readonly string[] Keys =
    [
        "Swatch.Blue", "Swatch.Orange", "Swatch.Green", "Swatch.Purple", "Swatch.Pink", "Swatch.Cyan",
        "Swatch.Amber", "Swatch.Teal", "Swatch.Lime", "Swatch.Red", "Swatch.Yellow", "Swatch.Slate",
    ];

    private static SolidColorBrush[]? _palette;

    private static SolidColorBrush[] Palette()
    {
        if (_palette is { }) return _palette;

        var brushes = new List<SolidColorBrush>(Keys.Length);
        var resources = Application.Current?.Resources;
        if (resources is not null)
            foreach (var key in Keys)
            {
                Color? colour = resources[key] switch
                {
                    SolidColorBrush b => b.Color,
                    Color c => c,
                    _ => null,
                };
                if (colour is { } value)
                {
                    var brush = new SolidColorBrush(value);
                    brush.Freeze();
                    brushes.Add(brush);
                }
            }

        if (brushes.Count == 0)
        {
            var fallback = new SolidColorBrush(Color.FromRgb(0x6E, 0x8A, 0x9E));
            fallback.Freeze();
            brushes.Add(fallback);
        }
        _palette = [.. brushes];
        return _palette;
    }

    /// <summary>The stable brush for a community id (shared by the node fill and the Segments rail swatch so they
    /// always agree). A null / negative id falls back to the first palette entry.</summary>
    public static SolidColorBrush ForCommunity(int? community)
    {
        var palette = Palette();
        return community is int c && c >= 0 ? palette[c % palette.Length] : palette[0];
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ForCommunity(value as int?);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
