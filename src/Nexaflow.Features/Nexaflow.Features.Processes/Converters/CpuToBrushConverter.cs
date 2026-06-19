using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Features.Processes.Converters;

/// <summary>Colours a CPU% value so heavy consumers stand out: danger ≥ 50%, warning ≥ 15%, normal text
/// otherwise. Resolves theme brushes at convert time (no hard-coded colours).</summary>
public sealed class CpuToBrushConverter : IValueConverter
{
    public static readonly CpuToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double cpu = value switch { double d => d, _ => 0 };
        var key = cpu >= 50 ? "DangerBrush" : cpu >= 15 ? "WarningBrush" : "TextBrush";
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
