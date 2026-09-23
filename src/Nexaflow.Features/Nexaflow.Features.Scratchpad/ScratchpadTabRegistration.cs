using Nexaflow.Features.Common;
using Nexaflow.Features.Scratchpad.ViewModels;
using Nexaflow.Features.Scratchpad.Views;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Scratchpad;

public sealed class ScratchpadTabRegistration(ScratchpadConfig config, IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "Scratchpad";
    public string PageKind => StaticPageKind;

    /// <summary>The Scratchpad opens standalone — offer it as a context item / ribbon "add page" entry.</summary>
    public bool CanBeContextItem => true;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null) => new()
    {
        Title       = Str.Get("Scratchpad.Page.Title"),
        Icon        = "📌",
        Breadcrumbs = {new BreadcrumbSegment { Label = Str.Get("Scratchpad.Page.Title") }},
        ContentFactory = () => new ScratchpadView(new ScratchpadViewModel(config, shellServices))
    };
}
