using System.Globalization;
using System.Windows.Data;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.SystemInfo.Converters;

/// <summary>Shows a service's WMI state in the user's language; the row keeps the invariant state the
/// command guards and the status colour compare against.</summary>
public sealed class ServiceStatusLabelConverter : IValueConverter
{
    public static readonly ServiceStatusLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "Running"          => Str.Get("SystemInfo.Services.Status.Running"),
        "Stopped"          => Str.Get("SystemInfo.Services.Status.Stopped"),
        "Paused"           => Str.Get("SystemInfo.Services.Status.Paused"),
        "Start Pending"    => Str.Get("SystemInfo.Services.Status.StartPending"),
        "Stop Pending"     => Str.Get("SystemInfo.Services.Status.StopPending"),
        "Continue Pending" => Str.Get("SystemInfo.Services.Status.ContinuePending"),
        "Pause Pending"    => Str.Get("SystemInfo.Services.Status.PausePending"),
        "Unknown"          => Str.Get("SystemInfo.Unknown"),
        _                  => value?.ToString() ?? "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
