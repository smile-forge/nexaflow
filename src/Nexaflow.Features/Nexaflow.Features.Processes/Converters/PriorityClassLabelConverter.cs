using System.Globalization;
using System.Windows.Data;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Processes.Converters;

/// <summary>Shows a Windows priority class in the user's language; the bound value stays the invariant
/// <see cref="System.Diagnostics.ProcessPriorityClass"/> name the elevation bridge is given.</summary>
public sealed class PriorityClassLabelConverter : IValueConverter
{
    public static readonly PriorityClassLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "Idle"        => Str.Get("Processes.Priority.Idle"),
        "BelowNormal" => Str.Get("Processes.Priority.BelowNormal"),
        "Normal"      => Str.Get("Processes.Priority.Normal"),
        "AboveNormal" => Str.Get("Processes.Priority.AboveNormal"),
        "High"        => Str.Get("Processes.Priority.High"),
        "RealTime"    => Str.Get("Processes.Priority.RealTime"),
        _             => value?.ToString() ?? "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
