using System;
using System.Globalization;
using System.Windows.Data;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>Formats a byte count (<c>long</c>) as a compact human string, e.g. <c>1.2 MB</c>. A
/// non-positive value renders blank. Also exposed as <see cref="Format"/> for non-XAML callers.</summary>
public sealed class BytesToTextConverter : IValueConverter
{
    public static readonly BytesToTextConverter Instance = new();

    public static string Format(long bytes) => bytes <= 0 ? "" : SizeFormatter.FormatBytes(bytes);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Format(value switch { long l => l, int i => i, double d => (long)d, _ => 0 });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
