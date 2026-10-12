using Nexaflow.Features.Common;

namespace Nexaflow.Features.Rtf.Theming;

/// <summary>
/// Contributes the page's <c>Rtf.*</c> colour tokens — the paper a document is drawn on — as theme fallbacks.
/// FeatureManager discovers this by reflection and ThemeManager merges the dictionary below the active theme, so the
/// defaults apply everywhere and any theme may retune an <c>Rtf.*</c> key by name.
/// </summary>
public sealed class RtfThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Rtf;component/Theming/RtfTheme.xaml"),
    ];
}
