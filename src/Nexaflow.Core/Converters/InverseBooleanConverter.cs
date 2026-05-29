using System;
using System.Globalization;
using System.Windows.Data;

namespace Nexaflow.Core.Converters;

/// <summary>Negates a boolean — used to disable controls while a flag is true.</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}
