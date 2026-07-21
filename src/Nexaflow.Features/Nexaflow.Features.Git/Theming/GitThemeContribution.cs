using Nexaflow.Features.Common;

namespace Nexaflow.Features.Git.Theming;

/// <summary>
/// Contributes the Git logo strip's colours (<c>Git.Logo*</c>) as theme fallbacks. FeatureManager
/// discovers this by reflection (like <see cref="IPageRegistration"/>) and ThemeManager merges the
/// dictionary below the active theme, so the brand orange applies everywhere and any theme may retune a
/// <c>Git.Logo*</c> key by name — no Core⇄feature reference either way.
/// </summary>
public sealed class GitThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Git;component/Theming/GitTheme.xaml"),
    ];
}
