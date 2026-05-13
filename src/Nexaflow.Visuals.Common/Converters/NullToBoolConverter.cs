using System;
using System.Globalization;
using System.Windows.Data;

namespace Nexaflow.Visuals.Common.Converters;
/// <summary>
/// Returns <c>true</c> when the bound value is not null; <c>false</c> otherwise.
/// Used in XAML triggers to toggle visibility based on nullable properties.
/// </summary>
[ValueConversion(typeof(object), typeof(bool))]
public sealed class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
