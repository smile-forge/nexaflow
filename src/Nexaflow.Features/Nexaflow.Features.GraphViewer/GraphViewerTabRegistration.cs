using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.GraphViewer.Loaders;
using Nexaflow.Features.GraphViewer.ViewModels;
using Nexaflow.Features.GraphViewer.Views;

namespace Nexaflow.Features.GraphViewer;

/// <summary>
/// Registers the Graph viewer page (kind <c>GraphViewer</c>). Opens the <c>graph.json</c> named by the
/// <c>"path"</c> param (the "As Graph" action, or the Product tab's Generate-Graph flow). Discovered by
/// reflection via <see cref="StaticPageKind"/>; <see cref="IShellServices"/> is injected for open-in-code + file watch.
/// </summary>
public sealed class GraphViewerTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;
    private readonly GraphLoader _loader = new();

    public GraphViewerTabRegistration(IShellServices shell) => _shell = shell;

    public static string StaticPageKind => "GraphViewer";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Path to a knowledge-graph file (graph.json).", Required: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Graph" : Path.GetFileName(path);

        var page = new Page
        {
            Title = title,
            Icon = "🕸",
            ContentFactory = () => new GraphView(new GraphViewModel(path, _loader, _shell)),
        };
        page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
