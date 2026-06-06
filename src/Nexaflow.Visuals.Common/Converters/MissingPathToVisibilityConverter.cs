using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound value is a non-empty path that no longer
/// exists on disk (neither a file nor a directory), otherwise <see cref="Visibility.Collapsed"/>.
/// Used to flag a referenced attachment whose file has been moved or deleted.
/// </summary>
public sealed class MissingPathToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value as string;
        bool missing = !string.IsNullOrWhiteSpace(path) && !File.Exists(path) && !Directory.Exists(path);
        return missing ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
