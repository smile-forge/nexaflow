using Nexaflow.Features.Common;

namespace Nexaflow.Features.Svg.Theming;

/// <summary>
/// Contributes the viewer's <c>Svg.*</c> colour tokens (canvas background, transparency checkerboard) as
/// theme fallbacks. FeatureManager discovers this by reflection (like <see cref="IPageRegistration"/>) and
/// ThemeManager merges the dictionary below the active theme, so the defaults apply everywhere and any theme
/// may retune an <c>Svg.*</c> key by name — no Core⇄feature reference either way.
/// </summary>
public sealed class SvgThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Svg;component/Theming/SvgTheme.xaml"),
    ];
}
