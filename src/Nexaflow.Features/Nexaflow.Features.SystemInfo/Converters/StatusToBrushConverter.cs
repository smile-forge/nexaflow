using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nexaflow.Features.SystemInfo.Models;

namespace Nexaflow.Features.SystemInfo.Converters;

/// <summary>
/// Maps a <see cref="SystemInfoStatus"/> to a themed brush so value text colours track the active theme
/// (Good→Success, Warning→Warning, Bad→Danger, Neutral→Text). Resolves the brush from app resources at
/// convert time — never a hard-coded colour.
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public static readonly StatusToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = (value as SystemInfoStatus?) switch
        {
            SystemInfoStatus.Good    => "SuccessBrush",
            SystemInfoStatus.Warning => "WarningBrush",
            SystemInfoStatus.Bad     => "DangerBrush",
            _                        => "TextBrush",
        };
        return Application.Current?.TryFindResource(key) as Brush
               ?? Application.Current?.TryFindResource("TextBrush") as Brush
               ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
