using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nexaflow.Features.Solver.Palette;

namespace Nexaflow.Features.Solver.Views;

/// <summary>
/// Maps a <see cref="PaletteKeyKind"/> to its face brush.
/// <para>
/// The brush is looked up from the application's resources at conversion time rather than baked in,
/// so a theme that retunes <c>Solver.Key.*</c> is honoured. The literal fallback exists only for the
/// case where the feature's theme contribution has not merged — a designer surface, or a unit test
/// with no application — and is never the value a running app uses.
/// </para>
/// </summary>
public sealed class PaletteKeyBrushConverter : IValueConverter
{
    private static readonly Brush Fallback = new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is PaletteKeyKind kind ? kind switch
        {
            PaletteKeyKind.Digit => "Solver.Key.Digit",
            PaletteKeyKind.Operator => "Solver.Key.Operator",
            PaletteKeyKind.Function => "Solver.Key.Function",
            PaletteKeyKind.Constant => "Solver.Key.Constant",
            PaletteKeyKind.Action => "Solver.Key.Action",
            _ => "Solver.Key.Bg",
        } : "Solver.Key.Bg";

        return Application.Current?.TryFindResource(key) as Brush ?? Fallback;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("A key's colour is derived from its kind, never the other way round.");
}
