using System;
using System.Globalization;
using System.Windows.Data;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>
/// Two-way maps an enum value to a bool for radio-button-style toggles: returns true when the bound
/// enum equals the <c>ConverterParameter</c>; converting back returns that enum value when checked and
/// <see cref="Binding.DoNothing"/> when unchecked (so the unchecked sibling never clears the source).
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null
        && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is not null
            ? Enum.Parse(targetType, parameter.ToString()!)
            : Binding.DoNothing;
}
