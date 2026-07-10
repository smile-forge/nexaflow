using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Features.ProductManager.ViewModels;

namespace Nexaflow.Features.ProductManager.Views;

/// <summary>Renders a <see cref="Status"/> as its display text (should / shouldn't / done / faulted).</summary>
public sealed class StatusDisplayConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is Status s ? StatusInfo.Display(s) : string.Empty;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="Status"/> to its theme-resolved brush (for pills / chips).</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is Status s ? StatusInfo.Brush(s) : Brushes.Transparent;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
