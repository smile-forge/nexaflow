using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.ProductManager.Services;

/// <summary>
/// Brings this product's knowledge graph up to date when a page opens, off the UI thread, saying what it is
/// doing while it does it.
/// <para>
/// It replaced a <c>graph_build</c> client tool. That tool was a real problem rather than merely an extra
/// step: every other graph tool failed until it had been called, so the assistant had to know an ordering
/// nothing in the request implied, and the failure it got when it did not — "no graph has been built" — read
/// as a fact about the product rather than an instruction about the tooling. Opening the page is the point at
/// which the intent is unambiguous, so the work belongs there and nobody has to ask for it.
/// </para>
/// <para>
/// Incremental, so this is cheap after the first time: extraction is cached content-addressed and only the
/// files that actually changed are re-parsed. When nothing has changed it says nothing at all — a banner that
/// flashes up on every page open would teach people to stop reading it.
/// </para>
/// </summary>
/// <param name="report">
/// Called with what to show a person, and with null when there is nothing to show. On a background thread.
/// </param>
public sealed class GraphWarmUpTask(InitiativesHost host, Action<string?>? report = null) : IBackgroundTask
{
    /// <summary>How often the count on screen moves. Every file would be six thousand updates for a number
    /// nobody can read that fast, and each one costs a marshal to the UI thread.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    public string Description => Str.Get("ProductManager.Task.WarmUpGraph");

    public Task RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            var workspace = host.Workspace(null);

            // Reading the archive and checking it against the filesystem is itself seconds of work, so it
            // happens here rather than in whatever decided to open a page.
            var missing = !workspace.Exists;
            var current = missing ? null : workspace.Current();

            if (!missing && workspace.Drifted == 0) return;   // nothing to say and nothing to do

            report?.Invoke(missing ? Str.Get("ProductManager.Task.GraphBuilding") : Str.Get("ProductManager.Task.GraphUpdating"));

            var since = Stopwatch.StartNew();
            var options = new GraphBuildOptions
            {
                Progress = (done, total) =>
                {
                    if (since.Elapsed < Tick) return;
                    since.Restart();
                    report?.Invoke(missing
                        ? Str.Format("ProductManager.Task.GraphBuildingProgressFormat", done, total)
                        : Str.Format("ProductManager.Task.GraphUpdatingProgressFormat", done, total));
                },
            };

            var built = GraphBuilder.BuildWithCache(host.Tree, host.ProductRoot, options,
                                                    current?.Cache ?? workspace.Store.LoadGraphCache());
            ct.ThrowIfCancellationRequested();

            // Into memory first, so questions are answered from the new graph before the write finishes, and
            // then to disk so the next process to open — this one restarted, or nfi — starts from it.
            workspace.Replace(built.Graph, built.Cache);
            workspace.Flush();
        }
        finally { report?.Invoke(null); }
    }, ct);
}
