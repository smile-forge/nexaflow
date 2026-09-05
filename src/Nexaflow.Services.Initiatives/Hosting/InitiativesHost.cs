using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.IO.Common;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using System.Threading.Tasks;

namespace Nexaflow.Services.Initiatives.Hosting;

/// <summary>
/// One product's live state: the authored tree, loaded once and watched, plus a warm graph per working tree.
/// <para>
/// It exists so that the two things which drive this domain — <c>nfi</c> and the Product page's assistant —
/// run the same object rather than two implementations that happen to agree. The daemon holds one of these
/// for as long as it is up; the Product page holds one for as long as it is open; a one-shot process holds
/// one for the length of a command and throws it away. Nothing but lifetime differs, which is what makes
/// "the CLI and the app can do the same things" a structural fact instead of a promise.
/// </para>
/// <para>
/// The tree is watched rather than re-read per request because it is authored, small, and changes rarely —
/// and because a warm process that keeps serving a tree someone edited ten minutes ago is worse than a cold
/// one. The graphs are not watched: they are derived, large, and change constantly under a build, so they
/// are checked against the filesystem at the moment they are used instead.
/// </para>
/// </summary>
public sealed class InitiativesHost : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GraphWorkspace> _workspaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProductStore _store;
    private FileChangeWatcher? _treeWatcher;
    private ProductState? _tree;

    /// <summary>The file as this process last wrote it, so the notification that write causes can be told
    /// apart from someone else's edit. Cleared by the one event it accounts for.</summary>
    private (long Length, DateTime Modified)? _selfWrite;
    private bool _disposed;

    /// <param name="productRoot">The checkout that holds <c>.product</c> — the main one, always.</param>
    public InitiativesHost(string productRoot)
    {
        ProductRoot = productRoot;
        _store      = new ProductStore(productRoot);
    }

    public string ProductRoot { get; }

    /// <summary>Raised after the authored tree has been reloaded because the file changed. On a thread-pool
    /// thread — a caller with a dispatcher marshals it itself.</summary>
    public event Action? TreeChanged;

    /// <summary>
    /// The authored tree: nodes, concerns and snaplinks. Loaded on first ask and then kept, with the file
    /// watched so an edit made through any other surface — the CLI, the Product page, another agent — is
    /// picked up rather than served stale.
    /// </summary>
    public ProductState Tree
    {
        get
        {
            lock (_gate)
            {
                if (_tree is not null) return _tree;

                _tree = _store.Load();
                Watch();
                return _tree;
            }
        }
    }

    /// <summary>
    /// A private deep copy of <see cref="Tree"/>, for a caller that intends to <em>change</em> it.
    /// <para>
    /// Every mutating command edits the state it is handed and only its write decides whether to keep the
    /// result — so a command handed the live instance leaves its edits behind even when it refuses them. That
    /// is how a <c>set-snaplink</c> could print "nothing was written", leave a half-applied link in the tree
    /// this process serves, and have the next unrelated command persist it. The copy is the transaction: a
    /// refusal simply drops it, and only <see cref="TreeSaved"/> publishes anything.
    /// </para>
    /// </summary>
    public ProductState WorkingCopy() => Tree.Copy();

    /// <summary>
    /// The warm graph for one working tree. <paramref name="codeRoot"/> is that tree, or null for the main
    /// checkout — the same distinction every caller of this domain already makes, so the daemon carries it
    /// verbatim rather than inventing a second vocabulary for it.
    /// <para>
    /// The store is supplied rather than derived: which archive a worktree reads, and whether to seed it from
    /// the main checkout on first use, is a policy its caller owns. The host owns the lifetime, not the rule.
    /// </para>
    /// </summary>
    public GraphWorkspace Workspace(string? codeRoot, ProductStore? store = null)
    {
        var key = codeRoot is { Length: > 0 } ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(codeRoot)) : "";

        lock (_gate)
        {
            if (_workspaces.TryGetValue(key, out var existing)) return existing;

            var scope = key.Length == 0 ? null : Path.GetFileName(key);
            var bound = store ?? (scope is null ? _store : new ProductStore(ProductRoot, scope));
            return _workspaces[key] = new GraphWorkspace(bound, ProductRoot, key.Length == 0 ? null : key, () => Tree);
        }
    }

    /// <summary>Writes every workspace that has unsaved changes. Called before the process goes away, so an
    /// idle timeout costs the next caller a load and not a rebuild.</summary>
    public void Flush()
    {
        GraphWorkspace[] workspaces;
        lock (_gate) workspaces = [.. _workspaces.Values];

        foreach (var workspace in workspaces)
            try { workspace.Flush(); } catch (IOException) { /* a tree that cannot be written is not worth failing shutdown over */ }
    }

    /// <summary>
    /// Records a tree written by this process: updates the copy held here, folds the change into every graph,
    /// and stops the watcher rediscovering what we already know.
    /// <para>
    /// The watcher exists for edits made elsewhere — a person with the file open, another checkout's tooling.
    /// For our own writes it is pure rediscovery: re-reading a file we just produced, re-hashing a tree we just
    /// held, to reach a conclusion we already had. Worse, it arrives a debounce later, so a command could return
    /// before the graph it just changed agreed with it.
    /// </para>
    /// </summary>
    public void TreeSaved(ProductState state)
    {
        GraphWorkspace[] workspaces;
        ProductState saved;
        lock (_gate)
        {
            if (_disposed) return;

            // Adopted as a copy, not by reference: the caller goes on editing the state it saved (a branch's
            // pending links go back on for reporting), and none of that is what the file now says.
            _tree = saved = state.Copy();

            // Armed from the file as it now stands, so the notification this write is about to cause is matched
            // by what it wrote rather than by a timer. An edit that lands in the same millisecond from somewhere
            // else is indistinguishable and would be skipped — which is why this is only armed for one event.
            _selfWrite = Stamp();
            workspaces = [.. _workspaces.Values];
        }

        foreach (var workspace in workspaces)
            try { workspace.RebuildProductLayer(saved); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The authored tree's file as it stands, for telling our own write apart from someone else's.</summary>
    private (long Length, DateTime Modified)? Stamp()
    {
        try
        {
            var info = new FileInfo(_store.TreeFilePath);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (IOException) { return null; }
    }

    private void Watch()
    {
        if (_treeWatcher is not null || !File.Exists(_store.TreeFilePath)) return;

        _treeWatcher = new FileChangeWatcher(_store.TreeFilePath);
        _treeWatcher.Changed += OnTreeChanged;
    }

    private void OnTreeChanged()
    {
        lock (_gate)
        {
            if (_disposed) return;

            // Our own write, already folded in by TreeSaved. Re-reading it would reach the same answer a
            // debounce late and for nothing.
            if (_selfWrite is { } mine && Stamp() is { } now
                && mine.Length == now.Length && mine.Modified == now.Modified)
            {
                _selfWrite = null;
                return;
            }

            _tree = _store.Load();
        }

        // Off the request path, deliberately. Re-deriving the product layer takes seconds on a large graph, and
        // whoever asks a question next did not cause this and should not pay for it — they answer from the layer
        // as it stands, which is what they did before any of this existed, and the next query is current.
        _ = Task.Run(RebuildProductLayers);
        TreeChanged?.Invoke();
    }

    /// <summary>Brings every held graph's product half back in line with the tree that has just changed.</summary>
    private void RebuildProductLayers()
    {
        ProductState tree;
        GraphWorkspace[] workspaces;
        lock (_gate)
        {
            if (_disposed || _tree is null) return;
            tree       = _tree;
            workspaces = [.. _workspaces.Values];
        }

        foreach (var workspace in workspaces)
            try { workspace.RebuildProductLayer(tree); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A tree we cannot rewrite stays behind, and its next currency check says so again. Losing the
                // whole watcher over one locked file would be the worse failure.
            }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _treeWatcher?.Dispose();
        _treeWatcher = null;
        Flush();
    }
}
