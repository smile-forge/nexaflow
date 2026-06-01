using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.Scratchpad.Converters;

/// <summary>
/// Returns the themed <c>PostIt.&lt;name&gt;.Header</c> brush for the post-it header strip (shipped via
/// the scratchpad theme contribution). Throws at runtime if the key is missing (the contribution is
/// always merged, so that's a typo/bug); yields no value at design time where contributions aren't loaded.
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class PostItColorToHeaderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name) return DependencyProperty.UnsetValue;
        if (Application.Current?.Resources[$"PostIt.{name}.Header"] is Brush themed) return themed;
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return DependencyProperty.UnsetValue;
        throw new InvalidOperationException(
            $"Theme brush 'PostIt.{name}.Header' not found — is the scratchpad theme contribution merged?");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
