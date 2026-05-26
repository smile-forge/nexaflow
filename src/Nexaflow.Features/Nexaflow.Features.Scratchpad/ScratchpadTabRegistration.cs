using Nexaflow.Features.Common;
using Nexaflow.Features.Scratchpad.ViewModels;
using Nexaflow.Features.Scratchpad.Views;

namespace Nexaflow.Features.Scratchpad;

public sealed class ScratchpadTabRegistration(ScratchpadConfig config, IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "Scratchpad";
    public string PageKind => StaticPageKind;

    public Page CreatePage(Dictionary<string, string>? pageParams = null) => new()
    {
        Title       = "Scratchpad",
        Icon        = "📌",
        Breadcrumbs = {new BreadcrumbSegment { Label = "Scratchpad" }},
        ContentFactory = () => new ScratchpadView(new ScratchpadViewModel(config, shellServices))
    };
}
