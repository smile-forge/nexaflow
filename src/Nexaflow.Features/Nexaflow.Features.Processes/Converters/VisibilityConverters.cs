using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Features.Processes.Converters;

/// <summary>Empty/whitespace string → Visible, otherwise Collapsed. Drives the search placeholder.</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public static readonly EmptyToVisibleConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible, otherwise Collapsed. Drives the search clear (✕) button.</summary>
public sealed class NonEmptyToVisibleConverter : IValueConverter
{
    public static readonly NonEmptyToVisibleConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
