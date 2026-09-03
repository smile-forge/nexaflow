using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.IO.Common;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

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
    /// The warm graph for one working tree. <paramref name="codeRoot"/> is that tree's path, or null for the
    /// main checkout — the same distinction every caller of this domain already makes, so the daemon's
    /// protocol carries it verbatim rather than inventing a second vocabulary for it.
    /// </summary>
    public GraphWorkspace Workspace(string? codeRoot)
    {
        var key = codeRoot is { Length: > 0 } ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(codeRoot)) : "";

        lock (_gate)
        {
            if (_workspaces.TryGetValue(key, out var existing)) return existing;

            var scope = key.Length == 0 ? null : Path.GetFileName(key);
            var store = scope is null ? _store : new ProductStore(ProductRoot, scope);
            return _workspaces[key] = new GraphWorkspace(store, ProductRoot, key.Length == 0 ? null : key);
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
            _tree = _store.Load();
        }
        TreeChanged?.Invoke();
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
