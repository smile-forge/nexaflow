using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.Converters;

/// <summary>
/// Maps a <see cref="ContextSecurityRisk"/> to <see cref="Visibility.Visible"/> when it matches the
/// risk level named in the converter parameter (e.g. "Medium" / "High"); otherwise collapsed. Bound to
/// the observable <see cref="Page.SecurityRisk"/> so the per-chip risk badges update live.
/// </summary>
public sealed class ContextRiskToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ContextSecurityRisk risk) return Visibility.Collapsed;
        if (parameter is not string s || !Enum.TryParse<ContextSecurityRisk>(s, out var want))
            return Visibility.Collapsed;

        return risk == want ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
