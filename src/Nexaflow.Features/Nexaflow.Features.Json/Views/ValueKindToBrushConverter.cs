using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.Json.Views;

[ValueConversion(typeof(JsonValueKind), typeof(Brush))]
internal sealed class ValueKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not JsonValueKind kind) return GetResource("TextBrush");
        return kind switch
        {
            JsonValueKind.String            => GetResource("AccentBrush"),
            JsonValueKind.Number            => GetResource("TextBrush"),
            JsonValueKind.True or
            JsonValueKind.False             => GetResource("Swatch.Orange"),
            JsonValueKind.Null              => GetResource("TextMutedBrush"),
            _                              => GetResource("TextMutedBrush"),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush GetResource(string key)
        => Application.Current.Resources[key] as Brush
           ?? throw new InvalidOperationException($"Theme brush '{key}' not found.");
}
