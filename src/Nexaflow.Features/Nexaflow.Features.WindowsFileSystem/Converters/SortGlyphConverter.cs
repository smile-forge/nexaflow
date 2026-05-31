using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Features.WindowsFileSystem.Converters;

/// <summary>
/// Produces the sort-direction glyph for a GridView column header.
/// Bound values: [0] active sort column key (string), [1] ascending (bool),
/// [2] this column's key (string). Returns ▲ / ▼ for the active column,
/// ↕ for an inactive (but sortable) column — mirroring the Tabular viewer.
/// </summary>
public sealed class SortGlyphConverter : IMultiValueConverter
{
    public static readonly SortGlyphConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3) return "↕";
        var active = values[0] as string;
        var key    = values[2] as string;
        if (string.IsNullOrEmpty(key) || !string.Equals(active, key, StringComparison.Ordinal))
            return "↕";
        return values[1] is bool b && b ? "▲" : "▼";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Brush for the sort glyph: accent when this column is the active sort, dim otherwise.
/// Bound values: [0] active sort column key (string), [1] this column's key (string).
/// </summary>
public sealed class SortBrushConverter : IMultiValueConverter
{
    public static readonly SortBrushConverter Instance = new();

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool active = values.Length >= 2
                      && values[1] is string key && !string.IsNullOrEmpty(key)
                      && string.Equals(values[0] as string, key, StringComparison.Ordinal);
        return Application.Current.Resources[active ? "AccentBrush" : "TextDimBrush"];
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Foreground for a footer count: accent when its filter is active, muted otherwise.
/// Value is the active <c>EntryFilter</c>; ConverterParameter is "Folders" or "Files".
/// </summary>
public sealed class FilterBrushConverter : IValueConverter
{
    public static readonly FilterBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var active = value?.ToString();
        var target = parameter as string;
        bool on = (active == "FoldersOnly" && target == "Folders")
                  || (active == "FilesOnly" && target == "Files");
        return Application.Current.Resources[on ? "AccentBrush" : "TextMutedBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
