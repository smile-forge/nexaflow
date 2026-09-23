using System.Globalization;
using System.Windows.Data;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.SystemInfo.Converters;

/// <summary>Shows a <see cref="ServiceStartModes"/> value in the user's language; the picker's selection stays
/// the invariant value the elevation bridge is given.</summary>
public sealed class StartModeLabelConverter : IValueConverter
{
    public static readonly StartModeLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ServiceStartModes.Automatic        => Str.Get("SystemInfo.Services.StartMode.Automatic"),
        ServiceStartModes.AutomaticDelayed => Str.Get("SystemInfo.Services.StartMode.AutomaticDelayed"),
        ServiceStartModes.Manual           => Str.Get("SystemInfo.Services.StartMode.Manual"),
        ServiceStartModes.Disabled         => Str.Get("SystemInfo.Services.StartMode.Disabled"),
        "Unknown"                          => Str.Get("SystemInfo.Unknown"),
        _                                  => value?.ToString() ?? "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
