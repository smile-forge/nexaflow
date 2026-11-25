using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Localization;

namespace Nexaflow.Features.SystemInfo;

public sealed class SystemInfoTabRegistration(IShellServices shellServices, IAIService ai) : IPageRegistration
{
    public static string StaticPageKind => "SystemInfo";
    public string PageKind => StaticPageKind;

    /// <summary>Standalone device summary (no params) — offer it to the AI "add context" menu.</summary>
    public bool CanBeContextItem => true;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null) => new()
    {
        Title       = Str.Get("SystemInfo.Page.Title"),
        Icon        = "🖥️",
        Breadcrumbs = { new BreadcrumbSegment { Label = Str.Get("SystemInfo.Page.Title") } },
        ViewModelFactory = () => new SystemInfoViewModel(shellServices, ai)
    };
}
