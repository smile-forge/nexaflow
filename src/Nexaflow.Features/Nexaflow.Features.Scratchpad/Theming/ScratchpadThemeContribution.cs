using Nexaflow.Features.Common;

namespace Nexaflow.Features.Scratchpad.Theming;

/// <summary>
/// Contributes the post-it note colour bank (<c>PostIt.*</c>) as theme fallbacks. FeatureManager
/// discovers this by reflection (like <see cref="IPageRegistration"/>) and ThemeManager merges the
/// dictionary below the active theme, so the pastel defaults apply everywhere and any theme may
/// retune a <c>PostIt.*</c> key by name — no Core⇄feature reference either way.
/// </summary>
public sealed class ScratchpadThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Scratchpad;component/Theming/ScratchpadTheme.xaml"),
    ];
}
