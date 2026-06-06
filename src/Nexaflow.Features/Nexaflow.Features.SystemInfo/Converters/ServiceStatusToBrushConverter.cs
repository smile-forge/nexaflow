using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.SystemInfo.Converters;

/// <summary>
/// Maps a Windows service state string (Running / Stopped / Paused / *Pending) to a themed brush so the
/// status colour tracks the active theme. Resolves from app resources at convert time — never hard-coded.
/// </summary>
public sealed class ServiceStatusToBrushConverter : IValueConverter
{
    public static readonly ServiceStatusToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "Running"                              => "SuccessBrush",
            "Paused"                               => "WarningBrush",
            "Start Pending" or "Stop Pending"
                or "Continue Pending" or "Pause Pending" => "WarningBrush",
            "Stopped"                              => "TextMutedBrush",
            _                                      => "TextBrush",
        };
        return Application.Current?.TryFindResource(key) as Brush
               ?? Application.Current?.TryFindResource("TextBrush") as Brush
               ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
