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
    public GraphWorkspace(ProductStore store, string productRoot, string? codeRoot)
    {
        Store       = store;
        ProductRoot = productRoot;
        CodeRoot    = codeRoot;
    }

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
            Store.SaveSnapshot(_snapshot.Graph, _snapshot.Cache, _snapshot.Files);
            _dirty = false;
        }
    }

    /// <summary>
    /// Brings the held snapshot back in line with the files on disk: re-extract what changed, add what
    /// appeared, forget what went.
    /// <para>
    /// A file is judged by its stamp — length and write time, one stat, no read — and only a disagreement
    /// costs a parse. An archive written before stamps existed has none, and every file then looks changed,
    /// which is correct but slow exactly once: the flush that follows records them.
    /// </para>
    /// </summary>
    private void Reconcile(GraphSnapshot snapshot)
    {
        var known = snapshot.Cache.Files.Keys.ToList();
        if (known.Count == 0) return;

        foreach (var rel in known)
        {
            var full = Path.Combine(SourceRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            var info = Info(full);

            if (info is null)
            {
                // Absent is not deleted. The archive is shared with checkouts that have files this one does
                // not — a submodule, another branch's work — and forgetting on sight would have each tree
                // quietly erase the others' code from a graph they all read.
                continue;
            }

            if (snapshot.Files.TryGetValue(rel, out var stamp) && stamp.Matches(info.Length, info.LastWriteTimeUtc))
                continue;

            if (GraphBuilder.RefreshFile(snapshot.Graph, snapshot.Cache, ProductRoot, rel, CodeRoot))
                _dirty = true;
        }
    }

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
}
