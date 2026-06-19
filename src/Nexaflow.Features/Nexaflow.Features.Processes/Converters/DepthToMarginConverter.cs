using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Features.Processes.Converters;

/// <summary>Indents the tree's Name cell: depth → a left margin of <c>depth × 16</c>. The optional
/// converter parameter overrides the per-level indent.</summary>
public sealed class DepthToMarginConverter : IValueConverter
{
    public static readonly DepthToMarginConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int depth = value is int d ? d : 0;
        double step = parameter is string s && double.TryParse(s, out var p) ? p : 16.0;
        return new Thickness(depth * step, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
