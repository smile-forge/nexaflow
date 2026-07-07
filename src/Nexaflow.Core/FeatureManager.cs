using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;

namespace Nexaflow.Core;

/// <summary>
/// Singleton registry of feature-level tab factories, configs, query handlers, keyboard handlers,
/// drop targets and ribbon-pin handlers. Discovery is delegated to <see cref="FeatureCatalog"/>: call
/// <see cref="RegisterFeatures"/> at startup to build the (cached) discovery index without eagerly loading
/// every feature assembly. Feature assemblies load lazily — when <see cref="CreateTab"/> / the typed
/// <c>Get*</c> helpers resolve a type, or during the post-paint background warm-up. Each assembly's first
/// load runs <see cref="OnAssemblyActivated"/>, which registers that assembly's global configs / archive
/// handlers / theme contributions (the work that used to happen eagerly at startup).
///
/// File-system contracts (<c>IFileAction</c>, <c>IFolderAction</c>, <c>IFileCreateAction</c>,
/// <c>IFolderViewlet</c>) are still discovered via <see cref="IShellServices.DiscoverImplementations{T}"/>
/// (now catalog-backed) by <see cref="Services.FileSystemFeatureRegistry"/>, not here.
/// </summary>
public sealed class FeatureManager
{
    public static FeatureManager Instance { get; } = new();

    // Per-assembly GLOBAL config instances — populated as assemblies activate (warm-up thread or UI thread).
    private readonly ConcurrentDictionary<Type, IFeatureConfig> _configs = new();

    /// <summary>
    /// Read-only view of the per-assembly global <see cref="IFeatureConfig"/> instances registered so far.
    /// Exposed for feature-scoped registries (e.g. <see cref="Services.FileSystemFeatureRegistry"/>) that
    /// need to resolve config types as constructor args without going through the shell.
    /// </summary>
    public IReadOnlyDictionary<Type, IFeatureConfig> Configs => _configs;

    /// <summary>
    /// Workspace-scoped config types (marked <see cref="WorkspaceScopedConfigAttribute"/>), resolved from
    /// the catalog. Accessing this loads every owning assembly, so it's only used by the Manage-AI /
    /// Configure panels (which need every scoped config). Startup paths use the lazy
    /// <see cref="Models.Profile.FindWorkspaceConfig"/> instead.
    /// </summary>
    public IReadOnlyList<Type> WorkspaceScopedConfigTypes => FeatureCatalog.Instance.ResolveWorkspaceScopedConfigTypes();

    /// <summary>Scoped config types whose assembly is already loaded — never forces a new load.</summary>
    public IReadOnlyList<Type> LoadedWorkspaceScopedConfigTypes => FeatureCatalog.Instance.ResolveLoadedWorkspaceScopedConfigTypes();

    /// <summary>True if <paramref name="configType"/> is a <c>[WorkspaceScopedConfig]</c> — index-only, no load.</summary>
    public bool IsWorkspaceScopedConfig(Type configType)
        => configType.FullName is { } n && FeatureCatalog.Instance.IsWorkspaceScopedConfig(n);

    // Theme resource dictionaries contributed by features, accumulated as assemblies activate.
    private readonly HashSet<Uri> _themeContributionUris = [];
    private readonly object _themeLock = new();
    private Dispatcher? _ui;
    private volatile bool _themeRefreshQueued;
    private int _themeGen;        // bumped when new contribution URIs are added
    private int _themeApplied;    // last generation merged into Application.Resources

    /// <summary>Pack URIs of every feature-contributed theme <c>ResourceDictionary</c> activated so far.
    /// Re-applied below the active theme on a theme switch (see ShellServices).</summary>
    public IReadOnlyList<Uri> ThemeContributionUris { get { lock (_themeLock) return _themeContributionUris.ToList(); } }

    // ── Per-(Type, Workspace) instance cache ────────────────────────────
    private readonly Dictionary<WorkspaceRuntime, Dictionary<Type, object>> _cache = new();
    private readonly object _cacheLock = new();

    // ── Registration ──────────────────────────────────────────────────────

    /// <summary>Builds the (cached) discovery index. No feature assemblies are loaded on the cache-hit path.</summary>
    public void RegisterFeatures()
    {
        _ui = Dispatcher.CurrentDispatcher;   // captured on the UI thread for theme marshaling
        FeatureCatalog.Instance.Initialize(OnAssemblyActivated);
    }

    private static readonly string ArchiveHandlerName = typeof(IArchiveHandler).FullName!;
    private static readonly string StreamCodecName    = typeof(IStreamCodec).FullName!;

    /// <summary>
    /// Side-effects of loading a feature assembly, performed once on its first load: register its global
    /// configs with <see cref="ConfigManager"/>, register its archive handlers / stream codecs into the VFS,
    /// and fold its theme contributions into the active theme. Runs on whatever thread triggered the load
    /// (the background warm-up or the UI thread); theme work is marshalled to the UI thread.
    /// </summary>
    private void OnAssemblyActivated(Assembly asm, FeatureCatalog.AssemblyEntry entry)
    {
        bool themeChanged = false;

        foreach (var te in entry.Types)
        {
            // Global feature configs (scoped ones are built per-profile by Profile, not here).
            if (te.ConfigName is not null && !te.Scoped && asm.GetType(te.Name) is { } configType
                && !_configs.ContainsKey(configType))
            {
                try
                {
                    var cfg = (IFeatureConfig)Activator.CreateInstance(configType)!;
                    // ConfigManager.Register is first-wins and returns the canonical instance — startup may
                    // have pre-registered some (e.g. FileMapConfig); use the canonical so all consumers share it.
                    _configs[configType] = (IFeatureConfig)ConfigManager.Instance.Register(cfg, cfg.ConfigName);
                }
                catch { }
            }

            // Archive backends ship in feature assemblies — register into the process-wide VFS so files
            // inside archives browse/open like a folder.
            if (te.Contracts.Contains(ArchiveHandlerName) && asm.GetType(te.Name) is { } at)
                try { if (Activator.CreateInstance(at) is IArchiveHandler h) VirtualFileSystem.Instance.RegisterHandler(h); } catch { }
            if (te.Contracts.Contains(StreamCodecName) && asm.GetType(te.Name) is { } ct)
                try { if (Activator.CreateInstance(ct) is IStreamCodec c) VirtualFileSystem.Instance.RegisterCodec(c); } catch { }

            // Theme contributions (pack URIs captured during the scan).
            if (te.ThemeUris is { Count: > 0 } uris)
                lock (_themeLock)
                    foreach (var u in uris)
                        if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && _themeContributionUris.Add(uri))
                            themeChanged = true;
        }

        if (themeChanged)
        {
            lock (_themeLock) _themeGen++;
            RequestThemeRefresh();
        }
    }

    /// <summary>Synchronously folds any pending feature theme contributions into the active theme — called
    /// from <see cref="CreateTab"/> (UI thread) so a feature whose tab is being opened has its theme
    /// resources merged before its view loads, even if a background warm-up only just activated it and its
    /// queued (async) theme refresh hasn't run yet.</summary>
    private void EnsureThemeApplied()
    {
        if (_ui?.CheckAccess() == true) ApplyThemeNow();
    }

    // Re-merges the accumulated feature theme contributions below the active theme. On the UI thread this
    // runs inline (so the first page's theme is ready before it renders); off-thread (warm-up) it is posted
    // asynchronously and coalesced — never a synchronous Invoke, which would deadlock against an on-demand
    // load holding the catalog lock on the UI thread.
    private void RequestThemeRefresh()
    {
        var ui = _ui;
        if (ui is null) return;
        if (ui.CheckAccess()) { ApplyThemeNow(); return; }
        if (_themeRefreshQueued) return;
        _themeRefreshQueued = true;
        ui.BeginInvoke(DispatcherPriority.Background, () => { _themeRefreshQueued = false; ApplyThemeNow(); });
    }

    private void ApplyThemeNow()
    {
        Uri[] uris; int gen;
        lock (_themeLock)
        {
            gen = _themeGen;
            if (gen == _themeApplied) return;   // nothing new since the last merge
            uris = _themeContributionUris.ToArray();
        }
        ThemeManager.Apply(ThemeManager.Current, uris);
        lock (_themeLock) _themeApplied = gen;
    }

    // ── Instantiation ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates (or returns a cached) instance of <paramref name="targetType"/> with constructor args
    /// resolved from <paramref name="workspace"/> and the registered global config instances. Returns null
    /// when no satisfiable constructor is found.
    /// </summary>
    public object? Instantiate(Type targetType, WorkspaceRuntime workspace)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(workspace, out var ctxCache))
            {
                ctxCache = new Dictionary<Type, object>();
                _cache[workspace] = ctxCache;
            }

            if (ctxCache.TryGetValue(targetType, out var cached))
                return cached;

            var instance = TryInstantiateInternal(targetType, workspace);
            if (instance is not null)
                ctxCache[targetType] = instance;
            return instance;
        }
    }

    /// <summary>
    /// Drops all cached feature/handler instances built for <paramref name="workspace"/>. Called when a
    /// Workspace is reconfigured (its AIService/providers were replaced) or disposed, so the next request
    /// rebuilds handlers against the live services. See WorkspaceManager.
    /// </summary>
    public void EvictWorkspace(WorkspaceRuntime workspace)
    {
        lock (_cacheLock)
            _cache.Remove(workspace);
    }

    private object? TryInstantiateInternal(Type t, WorkspaceRuntime workspace)
    {
        foreach (var ctor in t.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var args = TryResolveArgs(ctor, workspace);
            if (args is null) continue;
            try { return ctor.Invoke(args); }
            catch { }
        }
        return null;
    }

    private object?[]? TryResolveArgs(ConstructorInfo ctor, WorkspaceRuntime? workspace)
    {
        var parms = ctor.GetParameters();
        var args  = new object?[parms.Length];
        for (int i = 0; i < parms.Length; i++)
        {
            var pt = parms[i].ParameterType;
            if (pt == typeof(WorkspaceRuntime))
            {
                if (workspace is null) return null;
                args[i] = workspace;
            }
            else if (typeof(IShellServices).IsAssignableFrom(pt))
            {
                if (workspace?.ShellServices is null) return null;
                args[i] = workspace.ShellServices;
            }
            else if (typeof(IAIService).IsAssignableFrom(pt))
            {
                if (workspace?.AiService is null) return null;
                args[i] = workspace.AiService;
            }
            else if (pt == typeof(IReadOnlyDictionary<Type, IFeatureConfig>))
            {
                args[i] = BuildScopedConfigView(workspace);
            }
            else if (typeof(IFeatureConfig).IsAssignableFrom(pt))
            {
                // Scoped config: profile-specific instance (lazily materialized).
                if (workspace?.Profile.FindWorkspaceConfig(pt) is { } scoped)
                    args[i] = scoped;
                else if (IsWorkspaceScopedConfig(pt))
                    return null;   // scoped, but this (null/window-less) workspace can't supply it
                else
                {
                    // Global config — ensure its owning assembly has been activated, then resolve.
                    if (!_configs.ContainsKey(pt) && pt.FullName is { } fn)
                        FeatureCatalog.Instance.ActivateAssemblyOwning(fn);
                    if (_configs.TryGetValue(pt, out var cfg)) args[i] = cfg;
                    else return null;
                }
            }
            else if (parms[i].IsOptional)
            {
                args[i] = Type.Missing;
            }
            else
            {
                return null;
            }
        }
        return args;
    }

    // ── Typed Get* helpers ────────────────────────────────────────────────

    public IReadOnlyList<IQueryHandler> GetQueryHandlers(WorkspaceRuntime ctx)
        => Instantiate<IQueryHandler>(FeatureCatalog.Instance.TypesImplementing<IQueryHandler>(), ctx);

    public IReadOnlyList<IRibbonPinHandler> GetRibbonPinHandlers(WorkspaceRuntime ctx)
        => Instantiate<IRibbonPinHandler>(FeatureCatalog.Instance.TypesImplementing<IRibbonPinHandler>(), ctx);

    /// <summary>Chat-bar key handlers (Up/Down history, Tab completion) for this workspace.</summary>
    public IReadOnlyList<IChatKeyHandler> GetChatKeyHandlers(WorkspaceRuntime ctx)
        => Instantiate<IChatKeyHandler>(FeatureCatalog.Instance.TypesImplementing<IChatKeyHandler>(), ctx);

    /// <summary>Chat-bar drop handlers (drag a file → insert its path) for this workspace.</summary>
    public IReadOnlyList<IChatDropHandler> GetChatDropHandlers(WorkspaceRuntime ctx)
        => Instantiate<IChatDropHandler>(FeatureCatalog.Instance.TypesImplementing<IChatDropHandler>(), ctx);

    /// <summary>Chat-bar input-preview handlers (live echo of what you're typing) for this workspace.</summary>
    public IReadOnlyList<IChatInputPreview> GetChatInputPreviews(WorkspaceRuntime ctx)
        => Instantiate<IChatInputPreview>(FeatureCatalog.Instance.TypesImplementing<IChatInputPreview>(), ctx);

    /// <summary>The foreign-drop handler that accepts <paramref name="format"/> (a WPF drag-data key), or null.</summary>
    public IRibbonPinHandler? GetRibbonPinHandlerForFormat(string format, WorkspaceRuntime ctx)
        => GetRibbonPinHandlers(ctx).FirstOrDefault(h => h.AcceptedFormats.Contains(format));

    /// <summary>The tab-pin handler that snapshots tabs of <paramref name="tabPageKind"/>, or null.</summary>
    public ITabPinHandler? GetTabPinHandler(string tabPageKind, WorkspaceRuntime ctx)
        => Instantiate<ITabPinHandler>(FeatureCatalog.Instance.TypesImplementing<ITabPinHandler>(), ctx)
            .FirstOrDefault(h => h.TabPageKind == tabPageKind);

    /// <summary>The click-time executor for ribbon buttons of <paramref name="pageKind"/>, or null
    /// (in which case the button simply opens that page kind as a tab).</summary>
    public IRibbonItemExecutor? GetRibbonItemExecutor(string pageKind, WorkspaceRuntime ctx)
        => Instantiate<IRibbonItemExecutor>(FeatureCatalog.Instance.TypesImplementing<IRibbonItemExecutor>(), ctx)
            .FirstOrDefault(e => e.PageKind == pageKind);

    /// <summary>
    /// Lightweight <see cref="Page"/> definitions for the page kinds that can be created without specific
    /// context (<see cref="IPageRegistration.CanBeContextItem"/>) in this workspace — offered in the AI
    /// conversation's "add context" menu. Resolving each registration loads its feature assembly.
    /// </summary>
    public IReadOnlyList<Page> GetContextItemPages(WorkspaceRuntime ctx)
    {
        var pages = new List<Page>();
        foreach (var pageKind in FeatureCatalog.Instance.PageKinds())
            if (ResolveRegistration(pageKind, ctx) is { CanBeContextItem: true }
                && CreateTab(pageKind, ctx) is { } page)
                pages.Add(page);
        return pages;
    }

    /// <summary>
    /// Page definitions offered in the ribbon editor's "add page" dropdown: every
    /// <see cref="IPageRegistration.CanBeContextItem"/> registration expanded via
    /// <see cref="IPageRegistration.CreatePageDefinitions"/>. Cheap stubs — content is realized later.
    /// </summary>
    public IReadOnlyList<Page> GetRibbonCatalogPages(WorkspaceRuntime ctx)
    {
        var pages = new List<Page>();
        foreach (var pageKind in FeatureCatalog.Instance.PageKinds())
        {
            if (ResolveRegistration(pageKind, ctx) is not { CanBeContextItem: true } reg) continue;
            foreach (var page in reg.CreatePageDefinitions())
            {
                page.PageKind ??= pageKind;
                pages.Add(page);
            }
        }
        return pages;
    }

    private IReadOnlyList<T> Instantiate<T>(IReadOnlyList<Type> types, WorkspaceRuntime ctx)
    {
        var result = new List<T>(types.Count);
        foreach (var t in types)
            if (Instantiate(t, ctx) is T instance)
                result.Add(instance);
        return result;
    }

    private IPageRegistration? ResolveRegistration(string pageKind, WorkspaceRuntime ctx)
        => FeatureCatalog.Instance.TryGetPageRegistrationType(pageKind, out var regType) && regType is not null
            ? Instantiate(regType, ctx) as IPageRegistration
            : null;

    // ── Tab creation ──────────────────────────────────────────────────────

    public bool IsRegistered(string pageKind) => FeatureCatalog.Instance.HasPageKind(pageKind);

    /// <summary>The version of the feature assembly that owns <paramref name="pageKind"/>, or null if unknown.</summary>
    public string? GetPageKindVersion(string pageKind) => FeatureCatalog.Instance.GetPageKindVersion(pageKind);

    public Page? CreateTab(string pageKind, WorkspaceRuntime workspace,
                           Dictionary<string, string>? pageParams = null)
    {
        if (ResolveRegistration(pageKind, workspace) is not { } reg) return null;
        EnsureThemeApplied();   // make sure this feature's theme contribution is merged before its view loads
        var tab = reg.CreatePageDefinition(pageParams);
        if (tab is not null)
        {
            tab.PageKind   = pageKind;
            tab.PageParams = pageParams;
        }
        return tab;
    }

    /// <summary>
    /// The openable page kinds and the parameters each accepts (<see cref="IPageRegistration.Parameters"/>),
    /// built per-workspace. Used to describe available pages to the AI. Resolving loads the feature assemblies.
    /// </summary>
    public IReadOnlyList<(string PageKind, IReadOnlyList<PageParameter> Parameters)> GetPageCatalog(WorkspaceRuntime ctx)
    {
        var result = new List<(string, IReadOnlyList<PageParameter>)>();
        foreach (var pageKind in FeatureCatalog.Instance.PageKinds())
            if (ResolveRegistration(pageKind, ctx) is { } reg)
                result.Add((pageKind, reg.Parameters));
        return result;
    }

    public IReadOnlyList<string> GetPageKindsForConfig(Type configType, WorkspaceRuntime ctx)
    {
        if (configType.FullName is not { } fn) return [];
        var regTypes = FeatureCatalog.Instance.ResolvePageRegistrationsForConfig(fn);
        var pageKinds = new List<string>(regTypes.Count);
        foreach (var regType in regTypes)
            if (Instantiate(regType, ctx) is IPageRegistration reg)
                pageKinds.Add(reg.PageKind);
        return pageKinds;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="IFeatureConfig"/> view handed to feature registrations and feature-scoped registries
    /// (e.g. <see cref="Services.FileSystemFeatureRegistry"/>). It's a <b>live</b> read-through over the
    /// global config map (which grows as feature assemblies activate lazily) plus the workspace's scoped
    /// configs — never a snapshot, so a config registered while a registry is still discovering actions
    /// across features (e.g. Git's config loaded mid-discovery) is visible when those actions construct.
    /// </summary>
    private IReadOnlyDictionary<Type, IFeatureConfig> BuildScopedConfigView(WorkspaceRuntime? workspace)
        => new LiveFeatureConfigView(_configs, workspace);
}
