using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Features.WindowsFileSystem.Converters;

// SortGlyphConverter / SortBrushConverter moved to Nexaflow.Visuals.Common.Converters (they were
// byte-identical across WindowsFileSystem / WindowsApps / Processes). Only the feature-specific
// FilterBrushConverter remains here.

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
