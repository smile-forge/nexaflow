using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// for a writer or sees one half-way. Building the next version is what is serialised; writing the archive is not —
/// it is done from a published version, which nothing changes, outside the lock (<see cref="Flush"/>), and a holder
/// that lives on does it a moment after the change rather than on anyone's request path.
/// </para>
/// <para>
/// <b>Currency is tracked, not checked per question.</b> A workspace that watches (<c>watch: true</c>, which a
/// resident host asks for) collects what changes under its tree as it happens, folds it into the next version a
/// quiet moment later, and answers a query without looking at the disk at all. What it has collected and not yet
/// folded, a reader judges for itself against the published version — a stat per path, no lock — and only a path
/// that version does not describe sends it to wait for the catch-up, rather than answer from what it knows is behind.
/// That is the common case made free: most of what a watcher reports is the graph's own edits, already folded in by
/// the time the notification lands. Known is the word: a change is known once the system reports it, which is
/// milliseconds after it lands — the one window in which a question can beat its own tree. Everything else — a
/// process starting, a watcher that lost track, a workspace not watching — is one scan of the stamps
/// (<see cref="GraphFreshness"/>). Whatever is found is folded in, however much: a branch switched or merged under a
/// session is that session's code now, and a graph that only reported it left every query describing the old one.
/// When so much has moved that reading it all is the cost, git vouches for what it can (<see cref="GraphAnchor"/>)
/// and only the rest is read.
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

    /// <summary>
    /// How many reported paths a reader judges for itself before it leaves them to the catch-up. An edit reports a
    /// handful, and judging them is a stat each; a checkout reports thousands, almost all of them really changed, and
    /// a reader that stat-ed every one only to wait anyway would have paid twice.
    /// </summary>
    private const int JudgeAtMost = 256;

    /// <summary>How soon after a change a holder that lives on writes the archive: long enough that a burst of edits is one
    /// write, short enough that a process ending abruptly loses little — and what it loses costs a re-read, not a
    /// wrong answer, since a stamp is only ever saved for a file that was read.</summary>
    private static readonly TimeSpan SaveWithin = TimeSpan.FromSeconds(5);

    /// <summary>The files git rewrites on every checkout, merge, reset and commit — the moments the anchor can move on.</summary>
    private static readonly HashSet<string> GitMoments = new(StringComparer.OrdinalIgnoreCase)
    {
        "HEAD", "index", "ORIG_HEAD", "MERGE_HEAD",
    };

    /// <summary>Building the next version and loading: one at a time. A reader takes it only to catch up.</summary>
    private readonly object _gate = new();

    /// <summary>Writing the archive: one at a time, and never while holding <see cref="_gate"/> — taken first, always.</summary>
    private readonly object _saving = new();

    /// <summary>What readers are handed. Replaced whole, never changed.</summary>
    private GraphSnapshot? _published;

    private volatile bool _dirty;

    /// <summary>Counts every change the archive does not have yet, so a save can tell whether one arrived while it wrote.</summary>
    private long _version;

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
    private readonly IGraphWorkspaceMeter? _meter;
    private TreeChangeWatcher? _changes;
    private TreeChangeWatcher? _gitChanges;

    /// <summary>The write a change arms, for a holder that lives on; one at a time — see <see cref="SaveSoon"/>.</summary>
    private ITimer? _saveTimer;
    private bool _saveArmed;

    /// <summary>The whole tree has been scanned since the held version was loaded, so the watcher accounts for every
    /// change since — the condition for answering without looking.</summary>
    private volatile bool _verified;

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
    /// <param name="watch">Whether this is held for longer than one command: the tree is watched for changes, and the
    /// archive written a moment after each. A workspace that does not watch scans the stamps on every read instead, and
    /// is written when its holder says so.</param>
    /// <param name="meter">Told where the time went, for a holder that accounts for it.</param>
    public GraphWorkspace(ProductStore store, string productRoot, string? codeRoot, Func<ProductState>? tree = null,
                          Func<GraphSnapshot?>? seed = null, bool watch = false, IGraphWorkspaceMeter? meter = null)
    {
        Store       = store;
        ProductRoot = productRoot;
        CodeRoot    = codeRoot;
        _tree       = tree;
        _seed       = seed;
        _watch      = watch;
        _meter      = meter;
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
    /// When the published version accounts for everything the watcher has reported and the archive has not been
    /// replaced underneath, this is a field read and a stat per reported path: the cost a warm process exists to reach.
    /// Otherwise it catches up first — loading, folding in what moved — and a reader that arrives while a catch-up runs
    /// waits for it rather than read the version it is replacing.
    /// </para>
    /// </summary>
    /// <param name="recheck">Scan every file however quiet the watcher has been — for a caller that suspects something
    /// changed without saying so.</param>
    /// <param name="progress">Told how far a catch-up has got through the files it has to read, for a holder that shows it.</param>
    public GraphReading Read(bool recheck = false, Action<int, int>? progress = null,
                             CancellationToken cancellation = default)
    {
        if (!recheck && Accounted() is { } held) return new GraphReading(held, GraphCatchUp.None);

        Enter();
        try
        {
            var seeded = _published is null || ArchiveMovedOn() ? Load() : false;
            if (_published is null) return new GraphReading(null, GraphCatchUp.None);

            var caught = CatchUp(recheck || !_verified || !_watch, progress, cancellation);
            return new GraphReading(_published, seeded ? caught with { Seeded = true } : caught);
        }
        finally { Monitor.Exit(_gate); }
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
        Enter();
        try
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
        finally { Monitor.Exit(_gate); }
    }

    /// <summary>
    /// Publishes the result of <paramref name="change"/>, made to a fork of the current version — for a change no list of
    /// files describes. A save that finds another process got there first then writes over it, since there is nothing
    /// to make again on its archive.
    /// </summary>
    public T Mutate<T>(Func<GraphSnapshot, T> change)
    {
        Enter();
        try
        {
            if (_published is null || ArchiveMovedOn()) Load();

            var draft  = _published?.Fork() ?? new GraphSnapshot();
            var result = change(draft);

            Volatile.Write(ref _published, draft);
            _refreshed = _forgotten = null;
            MarkDirty();
            return result;
        }
        finally { Monitor.Exit(_gate); }
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
            _refreshed = _forgotten = null;
            MarkDirty();
            StartWatching();
        }
        Flush();
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
            if (changed) MarkDirty();
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
    /// <para>
    /// The write is of a published version, which nothing changes, so it is made outside the lock: a reader catching up
    /// and an edit folding itself in carry on while the archive — tens of megabytes — goes to disk. A change that lands
    /// meanwhile is the next write's.
    /// </para>
    /// </summary>
    public void Flush()
    {
        lock (_saving)
        {
            GraphSnapshot snapshot;
            long version;
            HashSet<string>? refreshed, forgotten;
            lock (_gate)
            {
                if (!_dirty || _published is not { } held) return;
                snapshot = held;

                // Another process saved since this one read the archive — the app beside the resident process, say. Writing
                // this version over it would drop whatever that process added, so when this one's changes are described by
                // the files it touched, they are made again on the newer archive and that is what is saved.
                if (_refreshed is { } mine && _forgotten is { } gone && _archive is { } mark
                    && Info(Store.GraphFilePath) is { } info && !mark.Matches(info)
                    && GraphArchive.Read(Store.GraphFilePath) is { } newer)
                {
                    GraphBuilder.RefreshFiles(newer, ProductRoot, mine, CodeRoot);
                    foreach (var rel in gone) GraphBuilder.ForgetFile(newer, rel);
                    newer.Anchor = newer.Anchor?.Without(mine.Concat(gone));
                    Volatile.Write(ref _published, snapshot = newer);
                }

                // What changes from here on is the next write's to describe.
                version    = _version;
                refreshed  = _refreshed;
                forgotten  = _forgotten;
                _refreshed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _forgotten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var took = Stopwatch.StartNew();
            try { Store.SaveSnapshot(snapshot); }
            catch
            {
                // Not written, so what it would have described is still owed to the next one.
                lock (_gate)
                {
                    _refreshed = Owed(refreshed, _refreshed);
                    _forgotten = Owed(forgotten, _forgotten);
                }
                throw;
            }

            lock (_gate)
            {
                // Still marked while it wrote, so no reader took the archive going to disk for someone else's; stamped before
                // it is cleared, for the same reason. What we just wrote is now what the held version corresponds to: without
                // the stamp the next read would see an archive it had never stamped, and re-load the file it had itself just
                // produced.
                StampArchive();
                if (_version == version) _dirty = false;
            }
            _meter?.Saved(this, took.Elapsed);
        }
    }

    /// <summary>What an unwritten save still owes: both sets of files, or null — "not described by files" — if either was.</summary>
    private static HashSet<string>? Owed(HashSet<string>? taken, HashSet<string>? since) =>
        taken is null || since is null ? null : new HashSet<string>(taken.Concat(since), StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _saveTimer?.Dispose();
        }
        _changes?.Dispose();
        _gitChanges?.Dispose();
    }

    // ── Reading without the lock ────────────────────────────────────────────

    /// <summary>
    /// The published version, when it accounts for everything reported since it was verified — null when the reader has
    /// to catch up first.
    /// <para>
    /// The order is what makes it safe without the lock. What is pending is read before the version, and a catch-up
    /// publishes before it settles what it took, so a batch is never out of the one without being in the other; and a load
    /// clears <see cref="_verified"/> before it publishes, so the second look at it catches a version nobody has scanned.
    /// </para>
    /// </summary>
    private GraphSnapshot? Accounted()
    {
        if (!_watch || !_verified || _changes is not { } changes) return null;

        var pending = changes.Pending();
        if (Volatile.Read(ref _published) is not { } held || !_verified || ArchiveMovedOn()) return null;

        if (pending.IsEmpty) return held;
        if (pending.Overflowed || pending.Paths.Count > JudgeAtMost) return null;
        return GraphFreshness.Of(held, SourceRoot, pending).IsEmpty ? held : null;
    }

    /// <summary>Takes the lock, telling the meter how long that took when it was not at once.</summary>
    private void Enter()
    {
        if (Monitor.TryEnter(_gate)) return;

        var waited = Stopwatch.StartNew();
        Monitor.Enter(_gate);
        _meter?.Waited(this, waited.Elapsed);
    }

    // ── Loading and catching up ─────────────────────────────────────────────

    /// <summary>
    /// Reads the archive — or, when this tree has none, starts from <see cref="_seed"/> — and records what it was, so the
    /// next call can tell in one stat whether it still is. Under the gate.
    /// </summary>
    /// <returns>Whether it started from the seed.</returns>
    private bool Load()
    {
        var took   = Stopwatch.StartNew();
        var read   = GraphArchive.Read(Store.GraphFilePath);
        var seeded = false;
        if (read is null && !File.Exists(Store.GraphFilePath) && _seed?.Invoke() is { } from)
        {
            read   = from.Fork();
            seeded = true;
        }

        // Unverified before it is published, so a reader outside the lock never takes it for a scanned one.
        _verified  = false;
        Volatile.Write(ref _published, read);
        _dirty     = false;
        _refreshed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _forgotten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (seeded) MarkDirty();   // a graph started from another tree's is this one's from now on, and is written as such
        StampArchive();
        StartWatching();
        _meter?.Loaded(this, seeded, took.Elapsed);

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
        var took     = Stopwatch.StartNew();

        // Held, not taken: until what it is folded into is published, a reader outside the lock still sees it pending.
        var batch = _changes?.Hold() ?? default;
        try
        {
            full |= batch.Overflowed;

            var drift = full           ? GraphFreshness.Scan(snapshot, SourceRoot)
                      : !batch.IsEmpty ? GraphFreshness.Of(snapshot, SourceRoot, batch)
                      :                  GraphFreshness.Drift.None;

            var caught = drift.IsEmpty ? GraphCatchUp.None : Fold(snapshot, drift, progress, cancellation);
            if (full)
            {
                _verified = true;
                // A tree never anchored is anchored once it is known to describe the tree, off the request path.
                if (_watch && _published?.Anchor is null) ReanchorLater();
            }
            _meter?.CaughtUp(this, caught, full, took.Elapsed);
            return caught;
        }
        catch
        {
            // What was taken from the watcher is not in the graph, so the next reader scans for it.
            _verified = false;
            throw;
        }
        finally { _changes?.Settle(batch); }
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

    /// <summary>Under the gate.</summary>
    private void Publish(GraphSnapshot draft, IEnumerable<string> refreshed, IEnumerable<string> forgotten)
    {
        Volatile.Write(ref _published, draft);
        _refreshed?.UnionWith(refreshed);
        _forgotten?.UnionWith(forgotten);
        MarkDirty();
    }

    /// <summary>Under the gate: the held version is ahead of the archive, and a holder that lives on writes it soon.</summary>
    private void MarkDirty()
    {
        _dirty = true;
        _version++;
        if (_watch) SaveSoon();
    }

    /// <summary>
    /// Arms the write, unless it is armed already — so a burst of changes is one write, made <see cref="SaveWithin"/>
    /// after the first of them and on the pool, not on the path of whoever made the change. Under the gate.
    /// </summary>
    private void SaveSoon()
    {
        if (_disposed || _saveArmed) return;

        if (_saveTimer is null)
        {
            // Without the ambient context of the change that armed it first: the write is the workspace's, not that caller's.
            var flow = ExecutionContext.IsFlowSuppressed() ? (AsyncFlowControl?)null : ExecutionContext.SuppressFlow();
            try
            {
                _saveTimer = TimeProvider.System.CreateTimer(_ => SaveInBackground(), null,
                                                             Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            finally { flow?.Undo(); }
        }
        _saveArmed = true;
        _saveTimer.Change(SaveWithin, Timeout.InfiniteTimeSpan);
    }

    private void SaveInBackground()
    {
        // Disarmed before writing, so a change made while it writes arms the next write rather than being left for shutdown.
        // A workspace let go of is not written: whoever let go of it meant the next command to load from disk.
        lock (_gate)
        {
            _saveArmed = false;
            if (_disposed) return;
        }

        try { Flush(); }
        catch (Exception)
        {
            // A pool thread has nobody to tell, and an exception escaping it ends the process. Nothing is lost by stopping
            // here: the version is still marked unsaved, so the next change, or the holder's last flush, writes it.
        }
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
        MarkDirty();
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
    /// Not asked while this workspace has unsaved changes of its own, or is writing them. Re-reading would drop them, and
    /// the alternative — writing ours over theirs — is the same loss facing the other way. Holding what we have
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
        return Volatile.Read(ref _archive) is not { } mark || !mark.Matches(info);
    }

    /// <summary>Records the archive's current identity as the one the held version corresponds to.</summary>
    private void StampArchive() =>
        Volatile.Write(ref _archive,
                       Info(Store.GraphFilePath) is { } info ? new ArchiveMark(info.Length, info.LastWriteTimeUtc) : null);

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
