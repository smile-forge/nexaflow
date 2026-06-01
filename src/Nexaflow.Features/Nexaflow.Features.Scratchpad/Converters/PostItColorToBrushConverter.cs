using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.Scratchpad.Converters;

/// <summary>
/// Maps a post-it colour name to its themed <c>PostIt.&lt;name&gt;.Fill</c> gradient (shipped via the
/// scratchpad theme contribution, so a theme can retune the paper). The contribution is always merged
/// at runtime, so a missing key means a typo/bug → throw (named) rather than silently painting a
/// literal. At design time feature contributions aren't loaded, so it yields no value instead.
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class PostItColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name) return DependencyProperty.UnsetValue;
        if (Application.Current?.Resources[$"PostIt.{name}.Fill"] is Brush themed) return themed;
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return DependencyProperty.UnsetValue;
        throw new InvalidOperationException(
            $"Theme brush 'PostIt.{name}.Fill' not found — is the scratchpad theme contribution merged?");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
