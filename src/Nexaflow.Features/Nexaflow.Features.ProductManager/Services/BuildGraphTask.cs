using Nexaflow.Features.Common;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Features.ProductManager.Services;

/// <summary>
/// Builds the knowledge graph off the UI thread through the shell's background-activity queue, then persists it
/// to <c>.product/graph.json</c> (the file the Graph viewer opens).
/// </summary>
/// <remarks>
/// A whole-repo build tree-sitter-parses every code file — seconds of work — so it must never run on the
/// dispatcher. The caller stamps the timestamp here (the builder itself stays clock-free).
/// </remarks>
public sealed class BuildGraphTask(ProductState state, string productRoot) : IBackgroundTask
{
    public string Description => "Generating product graph";

    /// <summary>Where the graph was written — read from the completion callback to open the viewer on it.</summary>
    public string GraphFilePath => new ProductStore(productRoot).GraphFilePath;

    public Task RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        var store = new ProductStore(productRoot);
        var cache = store.LoadGraphCache();   // reuse unchanged files' extraction — a re-generate is near-instant
        var built = GraphBuilder.BuildWithCache(state, productRoot, new GraphBuildOptions
        {
            GeneratedAt = DateTime.Now.ToString("o"),
        }, cache);
        ct.ThrowIfCancellationRequested();
        store.SaveSnapshot(built.Graph, built.Cache);
    }, ct);
}
