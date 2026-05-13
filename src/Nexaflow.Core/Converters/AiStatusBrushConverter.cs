using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Core.Converters
{
    /// <summary>AI status → green or orange brush</summary>
    [ValueConversion(typeof(bool), typeof(Brush))]
    public class AiStatusBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Green = new(Color.FromRgb(0x22, 0xD3, 0xA5));
        private static readonly SolidColorBrush Orange = new(Color.FromRgb(0xF9, 0x73, 0x16));
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? Orange : Green;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }
}
