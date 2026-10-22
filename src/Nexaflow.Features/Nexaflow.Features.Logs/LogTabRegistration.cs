using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Features.Logs.Views;
using Nexaflow.Visuals.Common.Localization;
using System.IO;

namespace Nexaflow.Features.Logs;

public sealed class LogTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Logs";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Full path of the log file to open.", OnDisk: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? Str.Get("Logs.Page.Title") : Path.GetFileName(path);

        var page = new Page
        {
            Title       = title,
            Icon        = "📋",
            ContentFactory = () => new LogView(new LogViewModel(path, shell)),
        };
        page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
