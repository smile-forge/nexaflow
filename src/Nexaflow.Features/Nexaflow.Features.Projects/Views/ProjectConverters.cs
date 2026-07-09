using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Features.Projects.Views;

/// <summary><see cref="CompletionStatus"/> → the shared app-theme <c>Status.*</c> brush.</summary>
public sealed class CompletionStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is CompletionStatus s
            ? s switch
            {
                CompletionStatus.Done    => "Status.Done",
                CompletionStatus.Faulted => "Status.Faulted",
                CompletionStatus.Should  => "Status.Should",
                _                        => "Status.Shouldnt",
            }
            : "Status.Shouldnt";
        return Application.Current?.Resources[key] as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary><see cref="CompletionStatus"/> → display text (with the apostrophe in "shouldn't").</summary>
public sealed class CompletionStatusToDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is CompletionStatus s
            ? (s == CompletionStatus.Shouldnt ? "shouldn't" : s.ToString().ToLowerInvariant())
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>A <c>Swatch.*</c> key string → its themeable brush (via <see cref="SwatchPalette"/>).</summary>
public sealed class SwatchKeyToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => SwatchPalette.Resolve(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>True when the bound value equals the <c>ConverterParameter</c> — for radio-style tab
/// selection (checking one writes that value back). Used for the Projects / Shelf / Archives tabs.</summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.Equals(parameter) == true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is not null ? parameter : Binding.DoNothing;
}
