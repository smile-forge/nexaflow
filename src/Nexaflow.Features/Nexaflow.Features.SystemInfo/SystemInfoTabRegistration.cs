using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Features.SystemInfo.Views;

namespace Nexaflow.Features.SystemInfo;

public sealed class SystemInfoTabRegistration(IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "SystemInfo";
    public string PageKind => StaticPageKind;

    /// <summary>Standalone device summary (no params) — offer it to the AI "add context" menu.</summary>
    public bool CanBeContextItem => true;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null) => new()
    {
        Title       = "System Info",
        Icon        = "🖥️",
        Breadcrumbs = { new BreadcrumbSegment { Label = "System Info" } },
        ContentFactory = () => new SystemInfoView(new SystemInfoViewModel(shellServices))
    };
}
