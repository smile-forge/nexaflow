using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Help;

/// <summary>
/// The Help page: a showcase of what a page can do, opened beside it by the shell's Help button or F1
/// (<see cref="HelpPaneController"/>). Core's own page kind, discovered like any feature's. <c>topic</c> names the page
/// kind to explain — omit it for the list of every help page — and <c>query</c> searches as it opens. Neither says
/// which tab this is, only where to look, so a window keeps one Help tab and every open re-points it.
/// </summary>
public sealed class HelpPageRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Help";

    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters { get; } =
    [
        new("topic", "The page kind whose help to show, e.g. \"Text\" or \"FileSystem\"; omit it for the list of every help page.",
            Required: false, Identity: false),
        new("query", "Text to search for as the help opens: the page shown first, then every other help page.",
            Required: false, Identity: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var page = new Page { Title = Str.Get("Help.Tab.Title"), Icon = "❓" };
        // Closes over the page rather than the params: the shell stamps PageParams after this returns, and a re-point
        // that lands before the tab is first shown has to be what the view model reads when it is.
        page.ContentFactory = () => new HelpView(new HelpViewModel(HelpLibrary.Shared, shell, page));
        return page;
    }
}
