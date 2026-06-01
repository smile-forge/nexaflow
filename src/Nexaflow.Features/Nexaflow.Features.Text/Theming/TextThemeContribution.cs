using Nexaflow.Features.Common;

namespace Nexaflow.Features.Text.Theming;

/// <summary>
/// Contributes the text-viewer highlight tokens (<c>Text.*</c>) as theme fallbacks. Discovered by
/// reflection and merged below the active theme, so the defaults apply everywhere and any theme can
/// retune a <c>Text.*</c> key by name.
/// </summary>
public sealed class TextThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Text;component/Theming/TextTheme.xaml"),
    ];
}
