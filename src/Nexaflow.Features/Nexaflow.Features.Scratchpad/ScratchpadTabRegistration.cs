using Nexaflow.Features.Common;
using Nexaflow.Features.Scratchpad.ViewModels;
using Nexaflow.Features.Scratchpad.Views;

namespace Nexaflow.Features.Scratchpad;

public sealed class ScratchpadTabRegistration(ScratchpadConfig config, IShellServices shellServices) : ITabRegistration
{
    public string PageKind => "Scratchpad";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null) => new()
    {
        Title       = "Scratchpad",
        Icon        = "📌",
        Breadcrumbs = [new BreadcrumbSegment { Label = "Scratchpad" }],
        PageFactory = () => new ScratchpadView(new ScratchpadViewModel(config, shellServices))
    };
}
