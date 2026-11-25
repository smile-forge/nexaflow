using Nexaflow.Features.Common;
using Nexaflow.Features.Wsl.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Localization;
using Nexaflow.Visuals.Terminal.Shells;

namespace Nexaflow.Features.Wsl;

/// <summary>
/// Registers the WSL shell page: a WSL distro's own shell — bash, zsh or whatever its user logs in with — started in a
/// folder of the distro and spoken to as Linux speaks (<see cref="TerminalShellRoute.ForLocationShell"/>). Its tab is named
/// after the distro. The Windows command prompt is the Console's.
/// </summary>
public sealed class WslShellTabRegistration(IShellServices shell, IAIService ai, IVirtualFileSystem files) : IPageRegistration
{
    public static string StaticPageKind => "WslShell";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "The folder of a WSL distro to start its shell in, as the file system names it (::wsl-<distro>\\…).", Required: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var folder = pageParams?.GetValueOrDefault("path") is { Length: > 0 } given ? given : string.Empty;
        var title  = folder.Length > 0 ? files.GetDisplayName(files.GetBreadcrumbs(folder)[0].Path) : Str.Get("Wsl.Page.Title");

        var tab = new Page
        {
            Title       = title,
            Icon        = "🐧",
            PageParams  = new Dictionary<string, string> { ["path"] = folder },
            Breadcrumbs = { new BreadcrumbSegment { Label = title } },
        };
        tab.ViewModelFactory = () => new WslShellViewModel(TerminalShellRoute.ForLocationShell(files, folder), shell, ai) { Tab = tab };
        return tab;
    }
}
