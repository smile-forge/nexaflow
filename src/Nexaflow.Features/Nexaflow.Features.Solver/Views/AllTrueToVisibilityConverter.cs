using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Features.Solver.Views;

/// <summary>
/// Visible only when every bound condition is true. Used for affordances that depend on more than
/// one thing at once — the clear button needs both "this editor is showing" and "there is something
/// to clear", and either alone would put it in the wrong place or on an empty field.
/// </summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    /// <inheritdoc/>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var v in values)
            if (v is not true) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    /// <inheritdoc/>
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("A visibility cannot be split back into its conditions.");
}
