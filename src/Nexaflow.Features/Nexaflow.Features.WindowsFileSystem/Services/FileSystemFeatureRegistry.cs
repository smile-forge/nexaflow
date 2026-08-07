using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Owns discovery, construction and caching of the file-system feature
/// contracts: <see cref="IFileAction"/>, <see cref="IFolderAction"/>,
/// <see cref="IFileCreateAction"/> and <see cref="IFolderViewlet"/>.
/// </summary>
public sealed class FileSystemFeatureRegistry
{
    // Weak-keyed so a registry is collected once its owning workspace's IShellServices is gone
    // (a workspace rebuild creates a new shell and orphans the old entry — see WorkspaceManager.RestartWorkspace).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IShellServices, FileSystemFeatureRegistry> _instances = new();
    private static readonly object _instancesLock = new();

    /// <summary>
    /// Returns the canonical registry for <paramref name="shell"/>. Keyed by the
    /// shell-services instance so every consumer in a given window/work-context
    /// shares the same action instances.
    /// </summary>
    public static FileSystemFeatureRegistry For(
        IShellServices shell, IAIService ai,
        IReadOnlyDictionary<Type, IFeatureConfig> configs)
    {
        lock (_instancesLock)
        {
            if (!_instances.TryGetValue(shell, out var r))
                _instances.Add(shell, r = new FileSystemFeatureRegistry(shell, ai, configs));
            return r;
        }
    }

    private readonly IShellServices _shell;
    private readonly IAIService _ai;
    private readonly IReadOnlyDictionary<Type, IFeatureConfig> _configs;

    private readonly List<Type> _fileActionTypes       = [];
    private readonly List<Type> _folderActionTypes     = [];
    private readonly List<Type> _fileCreateActionTypes = [];
    private readonly List<Type> _folderViewletTypes    = [];
    private readonly List<Type> _thisPcProviderTypes   = [];

    /// <summary>Mount ids this registry has registered, so a location that disappears from a provider
    /// takes its mount with it. Guarded by <see cref="_cacheLock"/>.</summary>
    private readonly HashSet<string> _ownedMounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The VFS mounts are published into. Settable for tests; defaults to the process singleton
    /// because a mount is process-wide — every window browses the same namespace.</summary>
    internal IVirtualFileSystem Vfs { get; set; } = VirtualFileSystem.Instance;

    private readonly Dictionary<Type, object> _cache = new();
    private readonly object _cacheLock = new();

    private readonly IReadOnlyList<string> _allExperiences;

    private FileSystemFeatureRegistry(
        IShellServices shell, IAIService ai,
        IReadOnlyDictionary<Type, IFeatureConfig> configs)
    {
        _shell = shell;
        _ai = ai;
        _configs = configs;

        foreach (var t in shell.DiscoverImplementations<IFolderViewlet>())
            _folderViewletTypes.Add(t);

        // Only cache types whose instances are equivalent per (IShellServices, IAIService).
        // Dynamic types (ShellVerbAction, FolderActionAdapter) are excluded; they
        // are reached via GetReinitParams/Rehydrate or FindFileAction fallbacks.
        foreach (var t in shell.DiscoverImplementations<IFileAction>())
            if (typeof(ICacheable).IsAssignableFrom(t)) _fileActionTypes.Add(t);

        foreach (var t in shell.DiscoverImplementations<IFolderAction>())
            if (typeof(ICacheable).IsAssignableFrom(t)) _folderActionTypes.Add(t);

        foreach (var t in shell.DiscoverImplementations<IFileCreateAction>())
            if (typeof(ICacheable).IsAssignableFrom(t)) _fileCreateActionTypes.Add(t);

        // No ICacheable filter: a provider is inherently one-per-shell, and the SAME instance must be
        // handed out every time or the Changed subscription the browser holds would be on a dead object.
        foreach (var t in shell.DiscoverImplementations<IThisPcItemProvider>())
            _thisPcProviderTypes.Add(t);

        _allExperiences = _fileActionTypes
            .Select(t => t.GetProperty("StaticExperienceId",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                ?.GetValue(null) as string)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;

        // Mounts must exist before anything navigates: a restored session can hand NavigateTo a
        // ::mount path, and this registry is built in the view model's constructor, ahead of it.
        foreach (var provider in ThisPcItemProviders) provider.Changed += SyncMounts;
        SyncMounts();
    }

    // ── Typed accessors ──────────────────────────────────────────────────

    public IReadOnlyList<IFileAction>       FileActions       => Materialize<IFileAction>(_fileActionTypes);
    public IReadOnlyList<IFolderAction>     FolderActions     => Materialize<IFolderAction>(_folderActionTypes);
    public IReadOnlyList<IFileCreateAction> FileCreateActions => Materialize<IFileCreateAction>(_fileCreateActionTypes);
    public IReadOnlyList<IFolderViewlet>    FolderViewlets    => Materialize<IFolderViewlet>(_folderViewletTypes);

    /// <summary>The This PC row contributors. Instances are cached, so the object returned here is the
    /// one the browser subscribes to and the one <see cref="SyncMounts"/> reads.</summary>
    public IReadOnlyList<IThisPcItemProvider> ThisPcItemProviders => Materialize<IThisPcItemProvider>(_thisPcProviderTypes);

    public IReadOnlyList<string> AllExperiences => _allExperiences;

    // ── Mounts ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Publishes a pass-through mount for every local-path row the providers offer, and withdraws any
    /// this registry previously published that no longer appears. Registration is idempotent by id, so
    /// calling this repeatedly — and from more than one window — is free and raises no spurious change
    /// events. Safe to call from any thread.
    /// </summary>
    public void SyncMounts()
    {
        var wanted = new Dictionary<string, VirtualMount>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in ThisPcItemProviders)
        {
            IReadOnlyList<ThisPcItem> items;
            try { items = provider.GetItems() ?? []; }
            catch { continue; }   // one broken provider must not unmount everyone else's locations

            foreach (var item in items)
            {
                if (item is null || item.Backing != ThisPcItemBacking.LocalPath) continue;
                if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.TargetPath)) continue;
                wanted.TryAdd(item.Id, new VirtualMount(item.Id, item.Label, item.TargetPath));
            }
        }

        lock (_cacheLock)
        {
            foreach (var stale in _ownedMounts.Where(id => !wanted.ContainsKey(id)).ToList())
            {
                Vfs.UnregisterMount(stale);
                _ownedMounts.Remove(stale);
            }
            foreach (var (id, mount) in wanted)
            {
                try { Vfs.RegisterMount(mount); _ownedMounts.Add(id); }
                catch (ArgumentException) { /* a provider offered an id that can't be a path segment */ }
            }
        }
    }

    private IReadOnlyList<T> Materialize<T>(List<Type> types)
    {
        var result = new List<T>(types.Count);
        foreach (var t in types)
            if (Instantiate(t) is T instance) result.Add(instance);
        return result;
    }

    // ── Instantiation (no WorkContext) ───────────────────────────────────

    private object? Instantiate(Type targetType)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(targetType, out var cached)) return cached;

            foreach (var ctor in targetType.GetConstructors()
                                           .OrderByDescending(c => c.GetParameters().Length))
            {
                var args = TryResolveArgs(ctor);
                if (args is null) continue;
                try
                {
                    var instance = ctor.Invoke(args);
                    _cache[targetType] = instance;
                    return instance;
                }
                catch { }
            }
            return null;
        }
    }

    private object?[]? TryResolveArgs(ConstructorInfo ctor)
    {
        var parms = ctor.GetParameters();
        var args = new object?[parms.Length];
        for (int i = 0; i < parms.Length; i++)
        {
            var pt = parms[i].ParameterType;
            if (typeof(IShellServices).IsAssignableFrom(pt))
                args[i] = _shell;
            else if (typeof(IAIService).IsAssignableFrom(pt))
                args[i] = _ai;
            else if (_configs.TryGetValue(pt, out var cfg))
                args[i] = cfg;
            else if (parms[i].IsOptional)
                args[i] = Type.Missing;
            else
                return null;
        }
        return args;
    }

    // ── FindFileAction (rehydration for non-cacheable / ribbon-pinned actions) ──

    /// <summary>
    /// Finds an <see cref="IFileAction"/> by its concrete type's full or short name.
    /// First checks the cached set; if that misses and <paramref name="reinitParams"/>
    /// is supplied, locates the type and invokes its
    /// <c>public static IFileAction Rehydrate(Dictionary&lt;string,string&gt;)</c>
    /// factory — the pattern runtime-constructed types (e.g. <c>ShellVerbAction</c>) use.
    /// </summary>
    public IFileAction? FindFileAction(string typeName, Dictionary<string, string>? reinitParams = null)
    {
        var singleton = FileActions.FirstOrDefault(a =>
            a.GetType().FullName == typeName || a.GetType().Name == typeName);
        if (singleton is not null) return singleton;

        // FolderActionAdapter: non-cacheable wrapper whose inner type is stored in reinit params.
        if ((typeName == typeof(FolderActionAdapter).FullName || typeName == nameof(FolderActionAdapter)) &&
            reinitParams?.TryGetValue("innerType", out var innerTypeName) == true &&
            innerTypeName is not null)
        {
            var inner = FolderActions.FirstOrDefault(a =>
                a.GetType().FullName == innerTypeName || a.GetType().Name == innerTypeName);
            return inner is not null ? new FolderActionAdapter(inner) : null;
        }

        if (reinitParams is null) return null;

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName, throwOnError: false, ignoreCase: false))
            .FirstOrDefault(t => t is not null);
        if (type is null) return null;

        var factory = type.GetMethod(
            "Rehydrate",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(Dictionary<string, string>)],
            modifiers: null);
        if (factory is null) return null;

        try { return factory.Invoke(null, [reinitParams]) as IFileAction; }
        catch { return null; }
    }
}
