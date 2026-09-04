using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Graph.Store;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Services.Initiatives.Hosting;

/// <summary>
/// One working tree's graph, held in memory and kept current.
/// <para>
/// This is the type that makes a warm process worth having. Every graph command used to begin by reading
/// the archive from disk and end by forgetting it; holding it instead turns the second command into
/// milliseconds. Nothing about that is specific to the daemon, which is the point — the Product page hosts
/// the same object for as long as it is open, so the CLI and the in-app assistant answer from identical
/// state rather than from two implementations that agree by inspection.
/// </para>
/// <para>
/// Currency is checked rather than assumed, on every access, because the alternative is a warm process that
/// is confidently wrong. The check is a stat per known file — the stamps recorded beside the graph say what
/// each file looked like when it was extracted — and only files that disagree are re-read.
/// </para>
/// </summary>
public sealed class GraphWorkspace
{
    private readonly object _gate = new();
    private GraphSnapshot? _snapshot;
    private bool _dirty;

    /// <param name="store">Where this tree's archive lives — the main checkout's, or a worktree's own.</param>
    /// <param name="productRoot">The checkout holding <c>.product</c>: always the main one.</param>
    /// <param name="codeRoot">Where source is read from — this branch's tree, or null to mean the product root.</param>
    public GraphWorkspace(ProductStore store, string productRoot, string? codeRoot,
                      Func<ProductState>? tree = null)
{
    Store       = store;
    ProductRoot = productRoot;
    CodeRoot    = codeRoot;
    _tree       = tree;
}

/// <summary>The authored tree, when someone is holding one. Supplied rather than loaded here because the
/// host already keeps it current, and two copies of it would be one too many.</summary>
private readonly Func<ProductState>? _tree;

    public ProductStore Store { get; }
    public string ProductRoot { get; }
    public string? CodeRoot { get; }

    /// <summary>Where source is actually read from, with the null meaning resolved.</summary>
    public string SourceRoot => CodeRoot ?? ProductRoot;

    /// <summary>Whether a graph has been built for this tree at all. False on a first run, and the one
    /// condition the caller has to handle rather than wait through.</summary>
    public bool Exists => File.Exists(Store.GraphFilePath);

    /// <summary>
    /// The graph, loaded if it has not been and brought up to date with the files on disk. Null only when no
    /// graph has ever been built for this tree — a caller that gets null should say so and offer to build,
    /// not build silently: a first build is minutes, and a caller who asked for one node did not ask for that.
    /// </summary>
    public KnowledgeGraph? Graph => Current()?.Graph;

    /// <summary>The whole snapshot, for a caller that needs the per-file material as well — a rebuild, or an
    /// edit that will merge a file back in.</summary>
    public GraphSnapshot? Current()
    {
        lock (_gate)
        {
            _snapshot ??= GraphArchive.Read(Store.GraphFilePath);
            if (_snapshot is null) return null;

            NoteTreeStamp(_snapshot);
            Reconcile(_snapshot);
            return _snapshot;
        }
    }

    /// <summary>Replaces what is held — after a full build, which produces a snapshot rather than amending
    /// one. Persisted immediately: a build is expensive enough that losing it to an idle timeout would be
    /// its own bug.</summary>
    public void Replace(KnowledgeGraph graph, GraphCache cache)
    {
        lock (_gate)
        {
            _snapshot = new GraphSnapshot { Graph = graph, Cache = cache, Files = Stamps(cache) };
            _dirty    = true;
            Flush();
        }
    }

    /// <summary>Runs <paramref name="mutate"/> against the held snapshot and records that it changed. The
    /// lock is held throughout, so an edit and the currency check it depends on cannot interleave.</summary>
    public T Mutate<T>(Func<GraphSnapshot, T> mutate)
    {
        lock (_gate)
        {
            var snapshot = _snapshot ??= GraphArchive.Read(Store.GraphFilePath) ?? new GraphSnapshot();
            Reconcile(snapshot);

            var result = mutate(snapshot);
            _dirty     = true;
            return result;
        }
    }

    /// <summary>Writes the archive if anything has changed since it was last written. Cheap to call and safe
    /// to call often — a clean snapshot writes nothing.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty || _snapshot is null) return;

            _snapshot.Files = Stamps(_snapshot.Cache);
            Store.SaveSnapshot(_snapshot);
            _dirty = false;
        }
    }

    /// <summary>
    /// Notes that the authored tree has moved on, without doing anything about it here.
    /// <para>
    /// Re-deriving takes seconds on a repository this size — seeding a hundred thousand code nodes back in,
    /// re-resolving every snaplink, rewriting the archive — so doing it on the path of whoever happens to ask
    /// next turns one person's <c>set-concern</c> into someone else's twenty-second query. The host watches the
    /// file and does the work off the request path instead; a query in the meantime answers from the product
    /// layer as it was, which is exactly what it did before any of this existed, and self-heals within seconds.
    /// </para>
    /// </summary>
    private void NoteTreeStamp(GraphSnapshot snapshot)
    {
        var info = Info(Store.TreeFilePath);
        if (info is null) return;

        if (!snapshot.Tree.IsKnown || !snapshot.Tree.Matches(info.Length, info.LastWriteTimeUtc))
            TreeIsBehind = true;
    }

    /// <summary>Whether the product half of the graph predates the tree it was derived from. Set by the
    /// currency check, cleared by <see cref="RebuildProductLayer"/>.</summary>
    public bool TreeIsBehind { get; private set; }

    /// <summary>
    /// Re-derives the product half from the authored tree. Called by the host off the request path — on the
    /// watcher, or once after a load that found the stamp behind — never by a query.
    /// </summary>
    public void RebuildProductLayer(ProductState tree)
    {
        lock (_gate)
        {
            if (_snapshot is null) return;

            var changed = GraphBuilder.ApplyTreeDelta(_snapshot.Graph, tree, ProductRoot);

            var info = Info(Store.TreeFilePath);
            _snapshot.Tree = info is null ? default : new FileStamp(string.Empty, info.Length, info.LastWriteTimeUtc);
            TreeIsBehind   = false;
            _dirty         |= changed;
        }
    }

    /// <summary>
    /// Brings the held snapshot back in line with the files on disk: re-extract what changed, leave alone what
    /// this tree simply does not have, and refuse to quietly turn into a rebuild.
    /// <para>
    /// A file is judged by its stamp — length and write time, one stat, no read — and only a disagreement costs
    /// a parse. A stamp of nothing means the archive predates stamps being recorded, and unknown falls back to
    /// what the freshness report has always used: the archive's own write time as the baseline.
    /// </para>
    /// <para>
    /// The cap is the part that matters. A worktree seeded from the main checkout has every file stamped at
    /// checkout time and an archive built before that, so <i>everything</i> reads as changed — and refreshing
    /// it all is a full build, taking minutes, triggered by someone asking for one node. This repo's rule for
    /// that situation is already written down elsewhere and is the right one: drift is <b>reported, never acted
    /// on</b>. So a handful of edits — a working session — is folded in, and anything larger is left for the
    /// freshness line to describe and for <c>graph build</c> to fix, deliberately.
    /// </para>
    /// </summary>
    private void Reconcile(GraphSnapshot snapshot)
    {
        if (snapshot.Cache.Files.Count == 0) return;

        DateTime baseline;
        try { baseline = File.GetLastWriteTimeUtc(Store.GraphFilePath); }
        catch (IOException) { return; }

        // Collected before anything is parsed, because the count is what decides whether parsing is the right
        // thing to do at all. Stats only: this is the cheap half.
        var stale = new List<string>();
        foreach (var rel in snapshot.Cache.Files.Keys)
        {
            var info = Info(Path.Combine(SourceRoot, rel.Replace('/', Path.DirectorySeparatorChar)));

            // Absent is not deleted. The archive is shared with checkouts that have files this one does not —
            // a submodule, another branch's work — and forgetting on sight would have each tree quietly erase
            // the others' code from a graph they all read.
            if (info is null) continue;

            var stamp = snapshot.Files.TryGetValue(rel, out var s) ? s : default;
            var ok    = stamp.IsKnown
                ? stamp.Matches(info.Length, info.LastWriteTimeUtc)
                : info.LastWriteTimeUtc <= baseline;

            if (!ok) stale.Add(rel);
        }

        Drifted = stale.Count;
        if (stale.Count == 0 || stale.Count > RefreshLimit) return;

        foreach (var rel in stale)
            if (GraphBuilder.RefreshFile(snapshot.Graph, snapshot.Cache, ProductRoot, rel, CodeRoot))
                _dirty = true;

        Drifted = 0;
    }

    /// <summary>
    /// How many changed files this will fold in before deciding the graph wants rebuilding instead. Set to a
    /// working session rather than a branch: dozens of edits is someone working, thousands is a checkout that
    /// was seeded from somewhere else, and re-parsing thousands is the ninety-second build nobody asked for.
    /// </summary>
    private const int RefreshLimit = 200;

    /// <summary>How many files were found changed and NOT folded in — zero whenever the held graph is current.
    /// Non-zero means the graph is knowingly behind, which the freshness report is there to say out loud.</summary>
    public int Drifted { get; private set; }

    private static FileInfo? Info(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            return info.Exists ? info : null;
        }
        catch { return null; }
    }

    /// <summary>What every extracted file looks like right now, so the next process can tell in one stat
    /// apiece what it needs to re-read.</summary>
    private Dictionary<string, FileStamp> Stamps(GraphCache cache)
    {
        var stamps = new Dictionary<string, FileStamp>(cache.Files.Count, StringComparer.Ordinal);
        foreach (var (rel, contribution) in cache.Files)
        {
            var info = Info(Path.Combine(SourceRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            stamps[rel] = info is null
                ? new FileStamp(contribution.Hash, 0, default)
                : new FileStamp(contribution.Hash, info.Length, info.LastWriteTimeUtc);
        }
        return stamps;
    }

    /// <summary>Says the held snapshot has been changed in place by a caller holding it, so the next flush
    /// writes it. The alternative — every caller writing the archive itself — leaves the warm copy and the
    /// file disagreeing for as long as the process lives.</summary>
    public void MarkChanged()
    {
        lock (_gate) _dirty = true;
    }
}
