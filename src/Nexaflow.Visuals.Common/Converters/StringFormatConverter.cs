using System;
using System.Globalization;
using System.Windows.Data;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>
/// Formats a bound value with the <see cref="System.Windows.Data.Binding.ConverterParameter"/> as a
/// composite format string (e.g. parameter <c>"{0} Workspace"</c>). Use where <c>StringFormat</c> is
/// ignored because the target property is object-typed (ToolTip, Content, …).
/// </summary>
public sealed class StringFormatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => parameter is string fmt ? string.Format(culture, fmt, value) : value?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
