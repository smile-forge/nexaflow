using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Features.Logs.Views;
using System.IO;

namespace Nexaflow.Features.Logs;

public sealed class LogTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Logs";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Log" : Path.GetFileName(path);

        return new Page
        {
            Title       = title,
            Icon        = "📋",
            Breadcrumbs = {new BreadcrumbSegment { Label = title }},
            ContentFactory = () => new LogView(new LogViewModel(path, shell)),
        };
    }
}
