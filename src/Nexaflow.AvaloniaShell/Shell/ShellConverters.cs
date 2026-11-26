using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>Conversions the shell's views bind through.</summary>
public static class ShellConverters
{
    /// <summary>A colour the session keeps as text (a workspace's, <c>#RRGGBB</c>) as the brush that paints it; none where it does not parse.</summary>
    public static readonly IValueConverter ColourText =
        new FuncValueConverter<string?, IBrush?>(text => Color.TryParse(text, out var colour) ? new ImmutableSolidColorBrush(colour) : null);
}
