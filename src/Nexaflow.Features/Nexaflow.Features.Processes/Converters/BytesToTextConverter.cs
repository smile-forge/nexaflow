using System.Globalization;
using System.Windows.Data;

namespace Nexaflow.Features.Processes.Converters;

/// <summary>Formats a byte count (<c>long</c>) as a compact human string, e.g. <c>1.2 MB</c>. A
/// non-positive value renders blank. Also exposed as <see cref="Format"/> for non-XAML callers.</summary>
public sealed class BytesToTextConverter : IValueConverter
{
    public static readonly BytesToTextConverter Instance = new();

    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        if (bytes <= 0) return "";
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} {Units[0]}" : $"{v:0.#} {Units[u]}";
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Format(value switch { long l => l, int i => i, double d => (long)d, _ => 0 });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
