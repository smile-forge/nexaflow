using Nexaflow.Features.Common;

namespace Nexaflow.Features.Model3D.Theming;

/// <summary>
/// Contributes the viewer's <c>Model3D.*</c> colour tokens (viewport background, wireframe, grid, edges)
/// as theme fallbacks. FeatureManager discovers this by reflection (like <see cref="IPageRegistration"/>)
/// and ThemeManager merges the dictionary below the active theme, so the defaults apply everywhere and any
/// theme may retune a <c>Model3D.*</c> key by name — no Core⇄feature reference either way.
/// </summary>
public sealed class Model3DThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Model3D;component/Theming/Model3DTheme.xaml"),
    ];
}
