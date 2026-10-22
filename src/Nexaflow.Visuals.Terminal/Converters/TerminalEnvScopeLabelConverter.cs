using System.Globalization;
using System.Windows.Data;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Terminal.Models;

namespace Nexaflow.Visuals.Terminal.Converters;

/// <summary>Shows an <see cref="EnvScope"/> in the user's language; the enum value is what the edit writes to.</summary>
public sealed class TerminalEnvScopeLabelConverter : IValueConverter
{
    public static readonly TerminalEnvScopeLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        EnvScope.Session => Str.Get("Terminal.EnvEdit.Scope.Session"),
        EnvScope.User    => Str.Get("Terminal.EnvEdit.Scope.User"),
        EnvScope.Machine => Str.Get("Terminal.EnvEdit.Scope.Machine"),
        _                => value?.ToString() ?? "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
