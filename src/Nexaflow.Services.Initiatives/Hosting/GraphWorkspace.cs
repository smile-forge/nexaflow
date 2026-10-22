using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.IO.Common;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Graph.Store;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Services.Initiatives.Hosting;

/// <summary>What bringing a graph current did, for a caller that says so.</summary>
/// <param name="Read">Files read whose content was new to the graph — changed, or never seen.</param>
/// <param name="Dropped">Files taken out because this tree no longer has them.</param>
/// <param name="Seeded">This tree had no graph of its own, so it started from another's.</param>
public sealed record GraphCatchUp(IReadOnlyList<string> Read, IReadOnlyList<string> Dropped, bool Seeded = false)
{
    public static readonly GraphCatchUp None = new([], []);

    public bool IsEmpty => Read.Count == 0 && Dropped.Count == 0 && !Seeded;
}

/// <summary>A graph as one caller read it — null when none has been built — and what reading it took.</summary>
public sealed record GraphReading(GraphSnapshot? Snapshot, GraphCatchUp CatchUp);

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
/// <b>Versions, not a lock.</b> What a reader is handed (<see cref="Current"/>) is never changed again. A change —
/// a refresh, an edit merged back, the product layer redone — is made to a fork and published in its place, so any
/// number of commands read one tree at once, a long one keeps the version it started with, and none of them waits
/// for a writer or sees one half-way. Building the next version is what is serialised, and that is milliseconds.
/// </para>
/// <para>
/// <b>Currency is tracked, not checked per question.</b> A workspace that watches (<c>watch: true</c>, which a
/// resident host asks for) collects what changes under its tree as it happens, folds it into the next version a
/// quiet moment later, and answers a query without looking at the disk at all; a reader arriving before that
/// catches up first rather than answer from what it knows is behind. Known is the word: a change is known once the system
/// reports it, which is milliseconds after it lands — the one window in which a question can beat its own tree. Everything else — a process starting, a
/// watcher that lost track, a workspace not watching — is one scan of the stamps (<see cref="GraphFreshness"/>).
/// Whatever is found is folded in, however much: a branch switched or merged under a session is that session's code
/// now, and a graph that only reported it left every query describing the old one. When so much has moved that
/// reading it all is the cost, git vouches for what it can (<see cref="GraphAnchor"/>) and only the rest is read.
/// </para>
/// </summary>
public sealed class GraphWorkspace : IDisposable
{
    /// <summary>
    /// How many files a catch-up reads before it asks git which of them it can skip. A status of the tree is hundreds of
    /// milliseconds, and a few hundred files read and hashed cost about that; past it, git is the cheaper answer — and a
    /// checkout makes it ten thousand files to one look.
    /// </summary>
    private const int VouchAbove = 256;

    /// <summary>The files git rewrites on every checkout, merge, reset and commit — the moments the anchor can move on.</summary>
    private static readonly HashSet<string> GitMoments = new(StringComparer.OrdinalIgnoreCase)
    {
        "HEAD", "index", "ORIG_HEAD", "MERGE_HEAD",
    };

    /// <summary>Building the next version, loading, flushing: one at a time. A reader takes it only to catch up.</summary>
    private readonly object _gate = new();

    /// <summary>What readers are handed. Replaced whole, never changed.</summary>
    private GraphSnapshot? _published;

    private volatile bool _dirty;

    /// <summary>The files this process has refreshed and dropped since it read the archive — what it would have to do again
    /// to a newer archive another process saved meanwhile. Null once a change arrives that is not described by files, such
    /// as a rebuild, whose save is meant to replace whatever is there.</summary>
    private HashSet<string>? _refreshed = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _forgotten = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The archive as it was when the held version was read from it, so that a rebuild performed by anyone
    /// else is noticed rather than ignored forever.
    /// <para>
    /// Without this the snapshot was read once and kept for the life of the process, and a <c>graph build</c>
    /// run anywhere but inside this daemon — another process, another session, the CLI while a daemon
    /// happened to be resident — wrote a correct archive that was then never looked at again. What that
    /// looked like from outside was far worse than staleness: <c>graph search</c> answered "no nodes match"
    /// for types that plainly existed, and the freshness line reported the same drift after every rebuild,
    /// because the build fixed the file and the answer came from memory.
    /// </para>
    /// </summary>
    private ArchiveMark? _archive;

    private readonly Func<ProductState>? _tree;
    private readonly Func<GraphSnapshot?>? _seed;
    private readonly bool _watch;
    private TreeChangeWatcher? _changes;
    private TreeChangeWatcher? _gitChanges;

    /// <summary>The whole tree has been scanned since the held version was loaded, so the watcher accounts for every
    /// change since — the condition for answering without looking.</summary>
    private volatile bool _verified;

    /// <summary>Set while a catch-up has taken the watcher's changes and not yet published what they made: a reader then
    /// waits for it, rather than read the version those changes are about to replace.</summary>
    private volatile bool _catchingUp;

    /// <summary>Git has been seen to move, so the anchor is due a fresh look once the tree is caught up.</summary>
    private volatile bool _reanchor;

    private bool _disposed;

    /// <param name="store">Where this tree's archive lives — the main checkout's, or a worktree's own.</param>
    /// <param name="productRoot">The checkout holding <c>.product</c>: always the main one.</param>
    /// <param name="codeRoot">Where source is read from — this branch's tree, or null to mean the product root.</param>
    /// <param name="tree">The authored tree, when someone is holding one — supplied rather than loaded here because the
    /// host already keeps it current, and two copies of it would be one too many.</param>
    /// <param name="seed">Where to start when this tree has no archive yet — the main checkout's graph, for a worktree.
    /// Most of a branch is the same code, so starting from it and catching up is the cheap way in.</param>
    /// <param name="watch">Whether to watch the tree for changes. For a holder that lives longer than one command; a
    /// workspace that does not watch scans the stamps on every read instead.</param>
    public GraphWorkspace(ProductStore store, string productRoot, string? codeRoot, Func<ProductState>? tree = null,
                          Func<GraphSnapshot?>? seed = null, bool watch = false)
    {
        Store       = store;
        ProductRoot = productRoot;
        CodeRoot    = codeRoot;
        _tree       = tree;
        _seed       = seed;
        _watch      = watch;
    }

    public ProductStore Store { get; }
    public string ProductRoot { get; }
    public string? CodeRoot { get; }

    /// <summary>Where source is actually read from, with the null meaning resolved.</summary>
    public string SourceRoot => CodeRoot ?? ProductRoot;

    /// <summary>
    /// Whether this graph is its tree's alone, so a file absent from the tree is one the tree no longer has. The main
    /// checkout's graph is read on behalf of other trees too (<c>--main</c>), and there an absence proves nothing.
    /// </summary>
    public bool OwnsItsFiles => CodeRoot is { Length: > 0 } code
                             && !string.Equals(Path.GetFullPath(code).TrimEnd('\\', '/'),
                                               Path.GetFullPath(ProductRoot).TrimEnd('\\', '/'),
                                               StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a graph has been built for this tree at all. False on a first run, and the one condition the
    /// caller has to handle rather than wait through.</summary>
    public bool Exists => Volatile.Read(ref _published) is not null || File.Exists(Store.GraphFilePath);

    /// <summary>
    /// The graph, current with the files on disk. Null only when no graph has ever been built for this tree — a
    /// caller that gets null should say so and offer to build, not build silently: a first build is minutes, and a
    /// caller who asked for one node did not ask for that.
    /// </summary>
    public KnowledgeGraph? Graph => Current()?.Graph;

    /// <summary>The whole version, for a caller that needs the per-file material as well.</summary>
    public GraphSnapshot? Current() => Read().Snapshot;

    /// <summary>
    /// The current version, and what bringing it current took.
    /// <para>
    /// When the watcher has nothing new and the archive has not been replaced underneath, this is a field read: the
    /// cost a warm process exists to reach. Otherwise it catches up first — loading, folding in what moved — and a
    /// reader that arrives while a catch-up runs waits for it rather than read the version it is replacing.
    /// </para>
    /// </summary>
    /// <param name="recheck">Scan every file however quiet the watcher has been — for a caller that suspects something
    /// changed without saying so.</param>
    /// <param name="progress">Told how far a catch-up has got through the files it has to read, for a holder that shows it.</param>
    public GraphReading Read(bool recheck = false, Action<int, int>? progress = null,
                             CancellationToken cancellation = default)
    {
        if (!recheck && Volatile.Read(ref _published) is { } held && IsQuiet() && !ArchiveMovedOn())
            return new GraphReading(held, GraphCatchUp.None);

        lock (_gate)
        {
            var seeded = _published is null || ArchiveMovedOn() ? Load() : false;
            if (_published is null) return new GraphReading(null, GraphCatchUp.None);

            var caught = CatchUp(recheck || !_verified || !_watch, progress, cancellation);
            return new GraphReading(_published, seeded ? caught with { Seeded = true } : caught);
        }
    }

    /// <summary>
    /// Folds <paramref name="refresh"/> back in and drops <paramref name="forget"/> — what an edit does after writing, so
    /// the next question is about the code as it now is. A file whose stamp still holds is not read: the edit before
    /// the write asks for its targets too, and they are usually already current.
    /// </summary>
    /// <returns>What changed; nothing when there is no graph to change.</returns>
    public GraphCatchUp Update(IReadOnlyCollection<string> refresh, IReadOnlyCollection<string>? forget = null,
                               CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            if (_published is null) Load();
            if (_published is not { } snapshot) return GraphCatchUp.None;

            var reread = refresh.Where(rel => !StampHolds(snapshot, rel)).Distinct(StringComparer.Ordinal).ToList();
            if (reread.Count == 0 && forget is not { Count: > 0 }) return GraphCatchUp.None;

            var draft   = snapshot.Fork();
            var read    = GraphBuilder.RefreshFiles(draft, ProductRoot, reread, CodeRoot, cancellation);
            var dropped = forget?.Where(rel => GraphBuilder.ForgetFile(draft, rel)).ToList() ?? [];

            draft.Anchor = draft.Anchor?.Without(read.Concat(dropped));
            Publish(draft, reread, dropped);
            return new GraphCatchUp(read, dropped);
        }
    }

    /// <summary>
    /// Publishes the result of <paramref name="change"/>, made to a fork of the current version — for a change no list of
    /// files describes. A save that finds another process got there first then writes over it, since there is nothing
    /// to make again on its archive.
    /// </summary>
    public T Mutate<T>(Func<GraphSnapshot, T> change)
    {
        lock (_gate)
        {
            if (_published is null || ArchiveMovedOn()) Load();

            var draft  = _published?.Fork() ?? new GraphSnapshot();
            var result = change(draft);

            Volatile.Write(ref _published, draft);
            _dirty     = true;
            _refreshed = _forgotten = null;
            return result;
        }
    }

    /// <summary>
    /// Replaces the graph with a full build's, and persists it at once: a build is expensive enough that losing it to an
    /// idle timeout would be its own bug. The build read every file, so git is asked once what it now describes.
    /// </summary>
    public void Replace(GraphSnapshot built)
    {
        lock (_gate)
        {
            if (GraphGit.Look(SourceRoot, null) is { } look) built.Anchor = new GraphAnchor(look.Head, look.Dirty);

            Volatile.Write(ref _published, built);
            _dirty     = true;
            _refreshed = _forgotten = null;
            StartWatching();
            Flush();
        }
    }

    /// <summary>
    /// Re-derives the product half from the authored tree. Called by the host off the request path — on the
    /// watcher, or once after a load that found the stamp behind — never by a query.
    /// </summary>
    public void RebuildProductLayer(ProductState tree)
    {
        lock (_gate)
        {
            if (_published is not { } snapshot) return;

            var draft   = snapshot.Fork();
            var changed = GraphBuilder.ApplyTreeDelta(draft.Graph, tree, ProductRoot);
            var info    = Info(Store.TreeFilePath);
            draft.Tree  = info is null ? default : new FileStamp(string.Empty, info.Length, info.LastWriteTimeUtc);
            if (!changed && draft.Tree == snapshot.Tree) return;

            Volatile.Write(ref _published, draft);
            _dirty |= changed;
        }
    }

    /// <summary>
    /// Writes the archive if anything has changed since it was last written. Cheap to call and safe to call often — a
    /// clean workspace writes nothing.
    /// <para>
    /// What it writes is what it holds, stamps included, and nothing else. A flush that stamped every file from its
    /// directory entry vouched for files nobody had read: a change made outside nfi, saved over by anything else on the
    /// tree, became invisible until the next full build.
    /// </para>
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty || _published is not { } snapshot) return;

            // Another process saved since this one read the archive — the app beside the resident process, say. Writing
            // this version over it would drop whatever that process added, so when this one's changes are described by
            // the files it touched, they are made again on the newer archive and that is what is saved.
            if (_refreshed is { } refreshed && _forgotten is { } forgotten && _archive is { } mark
                && Info(Store.GraphFilePath) is { } info && !mark.Matches(info)
                && GraphArchive.Read(Store.GraphFilePath) is { } newer)
            {
                GraphBuilder.RefreshFiles(newer, ProductRoot, refreshed, CodeRoot);
                foreach (var rel in forgotten) GraphBuilder.ForgetFile(newer, rel);
                newer.Anchor = newer.Anchor?.Without(refreshed.Concat(forgotten));
                Volatile.Write(ref _published, snapshot = newer);
            }

            Store.SaveSnapshot(snapshot);
            _dirty     = false;
            _refreshed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _forgotten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // What we just wrote is now what the held version corresponds to. Without this the next read
            // would see an archive it had never stamped, and re-load the file it had itself just produced.
            StampArchive();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _changes?.Dispose();
        _gitChanges?.Dispose();
    }

    // ── Loading and catching up ─────────────────────────────────────────────

    /// <summary>Whether a reader may take the held version as it stands: watched, scanned since loading, nothing
    /// collected since, and no catch-up in flight.</summary>
    private bool IsQuiet() =>
        _watch && _verified && !_catchingUp && _changes is { HasChanges: false };

    /// <summary>
    /// Reads the archive — or, when this tree has none, starts from <see cref="_seed"/> — and records what it was, so the
    /// next call can tell in one stat whether it still is.
    /// </summary>
    /// <returns>Whether it started from the seed.</returns>
    private bool Load()
    {
        var read   = GraphArchive.Read(Store.GraphFilePath);
        var seeded = false;
        if (read is null && !File.Exists(Store.GraphFilePath) && _seed?.Invoke() is { } from)
        {
            read   = from.Fork();
            seeded = true;
        }

        Volatile.Write(ref _published, read);
        _verified  = false;
        _dirty     = seeded;   // a graph started from another tree's is this one's from now on, and is written as such
        _refreshed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _forgotten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        StampArchive();
        StartWatching();

        // Re-deriving the product layer takes seconds, so a load that finds it behind the tree leaves it to the pool
        // rather than to whoever asked: they answer from the layer as it was, and the next reader from the new one.
        if (read is not null && _tree is { } tree && TreeMovedOn(read))
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { RebuildProductLayer(tree()); }
                catch (Exception) { /* a pool thread's exception ends the process; the next tree change redoes this */ }
            });

        return seeded;
    }

    /// <summary>
    /// Brings the held version in line with the tree. <paramref name="full"/> judges every file the graph knows;
    /// otherwise only what the watcher collected. Under the gate.
    /// </summary>
    private GraphCatchUp CatchUp(bool full, Action<int, int>? progress, CancellationToken cancellation)
    {
        var snapshot = _published!;
        _catchingUp = true;
        try
        {
            var batch = _changes?.Take() ?? default;
            full |= batch.Overflowed;

            var drift = full                                 ? GraphFreshness.Scan(snapshot, SourceRoot)
                      : batch.Paths is { Count: > 0 } paths ? GraphFreshness.Of(snapshot, SourceRoot, paths)
                      :                                       GraphFreshness.Drift.None;

            var caught = drift.IsEmpty ? GraphCatchUp.None : Fold(snapshot, drift, progress, cancellation);
            if (full)
            {
                _verified = true;
                // A tree never anchored is anchored once it is known to describe the tree, off the request path.
                if (_watch && _published?.Anchor is null) ReanchorLater();
            }
            return caught;
        }
        catch
        {
            // What was taken from the watcher is not in the graph, so the next reader scans for it.
            _verified = false;
            throw;
        }
        finally { _catchingUp = false; }
    }

    /// <summary>Folds what <paramref name="drift"/> found into the next version and publishes it.</summary>
    private GraphCatchUp Fold(GraphSnapshot snapshot, GraphFreshness.Drift drift, Action<int, int>? progress,
                              CancellationToken cancellation)
    {
        var draft = snapshot.Fork();
        var stale = new Dictionary<string, FileStamp>(drift.Stale, StringComparer.Ordinal);

        // Too much to read: git vouches for every file that is clean and unchanged since the commit the graph
        // describes, and the stamp it read moments ago becomes that file's — the graph already holds its content.
        GitLook? look = null;
        if (stale.Count > VouchAbove && draft.Anchor is { } anchor
            && (look = GraphGit.Look(SourceRoot, anchor.Commit)) is not null)
            foreach (var (rel, seen) in drift.Stale)
                if (draft.Cache.Files.TryGetValue(rel, out var held) && look.Vouches(rel, anchor))
                {
                    draft.Files[rel] = seen with { Hash = held.Hash };
                    stale.Remove(rel);
                }

        List<string> toRead = [.. stale.Keys, .. drift.Added];
        var read    = GraphBuilder.RefreshFiles(draft, ProductRoot, toRead, CodeRoot, cancellation, progress);
        var dropped = OwnsItsFiles ? drift.Absent.Where(rel => GraphBuilder.ForgetFile(draft, rel)).ToList() : [];

        // With git just asked, the graph now describes the working tree — read or vouched — so the anchor moves to HEAD,
        // departing wherever the tree does and wherever a file could not be read.
        if (look is not null)
        {
            var changed = read.ToHashSet(StringComparer.Ordinal);
            var unread  = stale.Where(s => !changed.Contains(s.Key)
                                       && !(draft.Files.TryGetValue(s.Key, out var now) && now.Matches(s.Value.Length, s.Value.ModifiedUtc)))
                              .Select(s => s.Key);
            draft.Anchor = new GraphAnchor(look.Head, look.Dirty).Without(unread);
        }
        else draft.Anchor = draft.Anchor?.Without(read.Concat(dropped));

        Publish(draft, [.. drift.Stale.Keys, .. drift.Added], dropped);
        return new GraphCatchUp(read, dropped);
    }

    private void Publish(GraphSnapshot draft, IEnumerable<string> refreshed, IEnumerable<string> forgotten)
    {
        Volatile.Write(ref _published, draft);
        _dirty = true;
        _refreshed?.UnionWith(refreshed);
        _forgotten?.UnionWith(forgotten);
    }

    /// <summary>
    /// Starts watching the tree, and git beside it. Watching is what makes a read free; without it — a folder the
    /// system will not watch — every read scans, which is correct and only slower.
    /// </summary>
    private void StartWatching()
    {
        if (!_watch || _disposed || _changes is not null || !Directory.Exists(SourceRoot)) return;

        try
        {
            var root = SourceRoot;
            _changes = new TreeChangeWatcher(root, path => !RepoFiles.UnderSkippedDirectory(root, path));
            _changes.Changed += CatchUpInBackground;

            if (GraphGit.GitDirectory(root) is { } gitDirectory)
            {
                _gitChanges = new TreeChangeWatcher(gitDirectory, path => GitMoments.Contains(Path.GetFileName(path)),
                                                    subdirectories: false);
                _gitChanges.Changed += () =>
                {
                    _reanchor = true;
                    CatchUpInBackground();
                };
            }
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _changes?.Dispose();
            _changes = null;
        }
    }

    /// <summary>
    /// Folds what the watcher collected a quiet moment after it collected it, so the next reader finds the graph
    /// already current — and, when git has moved and the tree is caught up, takes a fresh anchor.
    /// </summary>
    private void CatchUpInBackground()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || _published is null) return;

                CatchUp(!_verified, null, CancellationToken.None);
                _gitChanges?.Take();
                if (_reanchor && _verified && _changes is { HasChanges: false })
                {
                    _reanchor = false;
                    Reanchor();
                }
            }
        }
        catch (Exception)
        {
            // A pool thread has nobody to tell, and an exception escaping it ends the process — every tree it serves with
            // it. Nothing is lost by stopping here: what was taken is not in the graph, so the next reader scans for it.
            _verified = false;
        }
    }

    private void ReanchorLater()
    {
        _reanchor = true;
        ThreadPool.QueueUserWorkItem(_ => CatchUpInBackground());
    }

    /// <summary>Asks git what the held version — known to describe the working tree — describes, and records it.</summary>
    private void Reanchor()
    {
        if (_published is not { } snapshot || GraphGit.Look(SourceRoot, null) is not { } look) return;
        if (snapshot.Anchor is { } was && was.Commit == look.Head && was.Departed.SetEquals(look.Dirty)) return;

        // Only the anchor differs, and nothing changes a published version's collections, so they are shared.
        Volatile.Write(ref _published, new GraphSnapshot
        {
            Graph  = snapshot.Graph,
            Cache  = snapshot.Cache,
            Tree   = snapshot.Tree,
            Files  = snapshot.Files,
            Anchor = new GraphAnchor(look.Head, look.Dirty),
        });
        _dirty = true;
    }

    // ── Stamps ──────────────────────────────────────────────────────────────

    /// <summary>Whether <paramref name="rel"/>'s stamp in <paramref name="snapshot"/> still describes it.</summary>
    private bool StampHolds(GraphSnapshot snapshot, string rel) =>
        snapshot.Files.TryGetValue(rel, out var stamp) && stamp.IsKnown
        && Info(Path.Combine(SourceRoot, rel.Replace('/', Path.DirectorySeparatorChar))) is { } info
        && stamp.Matches(info.Length, info.LastWriteTimeUtc);

    /// <summary>
    /// Whether the archive on disk is no longer the one the held version came from — someone else rebuilt it.
    /// <para>
    /// Not asked while this workspace has unflushed changes of its own. Re-reading would drop them, and the
    /// alternative — writing ours over theirs — is the same loss facing the other way. Holding what we have
    /// until <see cref="Flush"/> has written it keeps the choice out of a query's path; the flush re-stamps,
    /// so the very next call sees a clean version and can take their build in then.
    /// </para>
    /// </summary>
    private bool ArchiveMovedOn()
    {
        if (_dirty) return false;

        var info = Info(Store.GraphFilePath);
        if (info is null) return false;                 // no archive at all is not a newer one

        // An archive read before this was recorded has no stamp; re-reading once settles it.
        return _archive is not { } mark || !mark.Matches(info);
    }

    /// <summary>Records the archive's current identity as the one the held version corresponds to.</summary>
    private void StampArchive() =>
        _archive = Info(Store.GraphFilePath) is { } info ? new ArchiveMark(info.Length, info.LastWriteTimeUtc) : null;

    /// <summary>Whether the authored tree has moved on since <paramref name="snapshot"/>'s product layer was derived.</summary>
    private bool TreeMovedOn(GraphSnapshot snapshot) =>
        Info(Store.TreeFilePath) is { } info
        && (!snapshot.Tree.IsKnown || !snapshot.Tree.Matches(info.Length, info.LastWriteTimeUtc));

    private static FileInfo? Info(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            return info.Exists ? info : null;
        }
        catch { return null; }
    }

    /// <summary>The archive's identity: a reference, so a reader outside the gate sees one whole or the other.</summary>
    private sealed record ArchiveMark(long Length, DateTime Modified)
    {
        public bool Matches(FileInfo info) => Length == info.Length && Modified == info.LastWriteTimeUtc;
    }
}
