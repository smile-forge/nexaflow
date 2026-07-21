using Nexaflow.Features.Git.ViewModels;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;

namespace Nexaflow.Features.Git.Converters;

/// <summary>
/// Resolves a status segment's semantic <see cref="GitTone"/> to the active theme's brush. The view-model
/// decides meaning, this decides paint — so a theme can retune every Git status colour and no colour literal
/// lives in the feature (see docs/theming.md → "a feature never hard-codes a colour").
/// </summary>
public sealed class GitToneToBrushConverter : IValueConverter
{
    private static string KeyFor(GitTone tone) => tone switch
    {
        GitTone.Muted   => "TextMutedBrush",
        GitTone.Good    => "SuccessBrush",
        GitTone.Caution => "WarningBrush",
        GitTone.Bad     => "DangerBrush",
        _               => "TextBrush",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = KeyFor(value is GitTone t ? t : GitTone.Normal);
        // Throw rather than fall back to a literal: a missing token is a theming bug that should surface.
        return Application.Current?.Resources[key] as Brush
            ?? throw new InvalidOperationException($"Theme brush '{key}' not found.");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
