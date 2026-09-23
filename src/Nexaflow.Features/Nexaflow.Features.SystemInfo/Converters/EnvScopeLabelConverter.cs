using System.Globalization;
using System.Windows.Data;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.SystemInfo.Converters;

/// <summary>Shows an <see cref="EnvScope"/> in the user's language.</summary>
public sealed class EnvScopeLabelConverter : IValueConverter
{
    public static readonly EnvScopeLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        EnvScope.User    => Str.Get("SystemInfo.EnvVars.Scope.User"),
        EnvScope.Machine => Str.Get("SystemInfo.EnvVars.Scope.Machine"),
        _                => value?.ToString() ?? "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
