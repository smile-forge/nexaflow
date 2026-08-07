using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.ProductManager.Graph.Loaders;
using Nexaflow.Features.ProductManager.Graph.ViewModels;
using Nexaflow.Features.ProductManager.Graph.Views;

namespace Nexaflow.Features.ProductManager.Graph;

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
        new("node", "Id of a node to select on open (deep link from the graph search results).", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Graph" : Path.GetFileName(path);

        // `node` is a one-shot navigation hint, NOT part of this tab's identity — same rule as the Product
        // tab's. A tab keyed on {path,node} would stop matching a request for a different node and open a
        // second graph tab per node instead of re-pointing the one that is already showing this graph.
        string? focusNode = null;
        if (pageParams is not null && pageParams.Remove("node", out var requested)) focusNode = requested;

        var page = new Page
        {
            Title = title,
            Icon = "🕸",
            ContentFactory = () => new GraphView(new GraphViewModel(path, _loader, _shell, focusNode)),
        };
        page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
