using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nexaflow.Features.SystemInfo.Models;

namespace Nexaflow.Features.SystemInfo.Converters;

/// <summary>
/// Maps a <see cref="SystemInfoStatus"/> to a themed brush so value text colours track the active theme
/// (Good→Success, Warning→Warning, Bad→Danger, Neutral→Text). Resolves the brush from app resources at
/// convert time — never a hard-coded colour.
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public static readonly StatusToBrushConverter Instance = new();

    /// <summary>
    /// The theme resource key a health status paints with. Split out from <see cref="Convert"/> so the
    /// mapping is assertable without an <see cref="Application"/>: every status must resolve to a semantic
    /// token (never a literal colour), and anything unrecognised must fall back to plain text rather than
    /// implying a verdict.
    /// </summary>
    public static string ResourceKey(SystemInfoStatus? status) => status switch
    {
        SystemInfoStatus.Good    => "SuccessBrush",
        SystemInfoStatus.Warning => "WarningBrush",
        SystemInfoStatus.Bad     => "DangerBrush",
        _                        => "TextBrush",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Application.Current?.TryFindResource(ResourceKey(value as SystemInfoStatus?)) as Brush
           ?? Application.Current?.TryFindResource("TextBrush") as Brush
           ?? Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
