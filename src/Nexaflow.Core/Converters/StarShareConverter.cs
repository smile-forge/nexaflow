using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Core.Converters;

/// <summary>A share, 0 to 1, as a star width: a row's columns split as the shares are.</summary>
[ValueConversion(typeof(double), typeof(GridLength))]
public sealed class StarShareConverter : IValueConverter
{
    

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new GridLength(value is double share ? share : 0, GridUnitType.Star);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
