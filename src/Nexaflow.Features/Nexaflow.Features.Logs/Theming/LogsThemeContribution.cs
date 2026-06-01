using Nexaflow.Features.Common;

namespace Nexaflow.Features.Logs.Theming;

/// <summary>
/// Contributes the log-viewer colour tokens (<c>Log.*</c>) as theme fallbacks. Discovered by
/// reflection (like <see cref="IPageRegistration"/>) and merged below the active theme, so the
/// defaults apply everywhere and any theme can retune a <c>Log.*</c> key by name.
/// </summary>
public sealed class LogsThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Logs;component/Theming/LogsTheme.xaml"),
    ];
}
