using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace Nexaflow.Core;

/// <summary>
/// The session's feature system: the registry of feature-level tab factories, configs, query handlers, keyboard handlers,
/// drop targets and ribbon-pin handlers. One per session — <see cref="ShellHost"/> makes it and hands it to every window
/// (<see cref="WindowServices"/>) and runtime (<see cref="RuntimeSessionServices"/>, carried as
/// <see cref="WorkspaceRuntime.FeatureManager"/>). Discovery is delegated to its <see cref="Catalog"/>, whose (cached) index
/// it builds as it is made, without eagerly loading every feature assembly. Feature assemblies load lazily — when
/// <see cref="CreateTab"/> / the typed <c>Get*</c> helpers resolve a type, or during the post-paint background warm-up. Each
/// assembly's first load runs <see cref="OnAssemblyActivated"/>, which registers that assembly's global configs / archive
/// handlers / theme contributions.
///
/// File-system contracts (<c>IFileAction</c>, <c>IFolderAction</c>, <c>IFileCreateAction</c>,
/// <c>IFolderViewlet</c>) are still discovered via <see cref="IShellServices.DiscoverImplementations{T}"/>
/// (now catalog-backed) by <see cref="Services.FileSystemFeatureRegistry"/>, not here.
/// </summary>
public sealed class FeatureManager
{
    // Per-assembly GLOBAL config instances — populated as assemblies activate (warm-up thread or UI thread).
    private readonly ConcurrentDictionary<Type, IFeatureConfig> _configs = new();

    /// <summary>The app-wide tools features are given by their contract — <see cref="IPictureTools"/> and the like — each
    /// provided once, at start; every workspace's features share them.</summary>
    private readonly ConcurrentDictionary<Type, object> _tools = new();

    /// <summary>Gives <paramref name="tool"/> to every feature whose constructor asks for <typeparamref name="T"/>.</summary>
    public void ProvideTool<T>(T tool) where T : class => _tools[typeof(T)] = tool;

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
    /// <see cref="Models.Workspace.FindWorkspaceConfig"/> instead.
    /// </summary>
    public IReadOnlyList<Type> WorkspaceScopedConfigTypes => Catalog.ResolveWorkspaceScopedConfigTypes();

    /// <summary>True if <paramref name="configType"/> is a <c>[WorkspaceScopedConfig]</c> — index-only, no load.</summary>
    public bool IsWorkspaceScopedConfig(Type configType)
        => configType.FullName is { } n && Catalog.IsWorkspaceScopedConfig(n);

    // Theme resource dictionaries contributed by features, accumulated as assemblies activate.
    private readonly HashSet<Uri> _themeContributionUris = [];
    private readonly object _themeLock = new();
    private readonly IUiThread _ui;

    /// <summary>The session's location backends, which every feature's are registered in.</summary>
    private readonly LocationProviderRegistry _locationProviders;

    /// <summary>Resolves the sets of subfeatures a feature asks for, from the catalog.</summary>
    private readonly SubfeatureCatalog _subfeatures;
    private volatile bool _themeRefreshQueued;
    private int _themeGen;        // bumped when new contribution URIs are added
    private int _themeApplied;    // last generation merged into Application.Resources

    /// <summary>Pack URIs of every feature-contributed theme <c>ResourceDictionary</c> activated so far.
    /// Re-applied below the active theme on a theme switch (see ShellServices).</summary>
    public IReadOnlyList<Uri> ThemeContributionUris { get { lock (_themeLock) return _themeContributionUris.ToList(); } }
    // ── Registration ──────────────────────────────────────────────────────

    /// <summary>
    /// The session's feature system over <paramref name="catalog"/>: builds the catalog's (cached) discovery index — no feature
    /// assemblies are loaded on the cache-hit path — and activates Core. A feature's theme is folded in on <paramref name="ui"/>,
    /// however its assembly came to load, and its location backends are registered in <paramref name="locationProviders"/>.
    /// </summary>
    public FeatureManager(FeatureCatalog catalog, IUiThread ui, LocationProviderRegistry locationProviders)
    {
        Catalog            = catalog;
        _ui                = ui;
        _locationProviders = locationProviders;
        _subfeatures       = new SubfeatureCatalog(catalog, this);
        catalog.Initialize(OnAssemblyActivated);
    }

    /// <summary>Which feature type implements which contract, and the assemblies loaded as they are needed.</summary>
    public FeatureCatalog Catalog { get; }

    private static readonly string ArchiveHandlerName = typeof(IArchiveHandler).FullName!;
    private static readonly string StreamCodecName    = typeof(IStreamCodec).FullName!;
    private static readonly string LocationProviderName = typeof(ILocationProvider).FullName!;

    private static readonly string FeatureStartupName = typeof(IFeatureStartup).FullName!;

    /// <summary>What a feature's setup is given of the session: the global configs, the data folder, and saving a config.</summary>
    private FeatureStartupContext StartupContext()
        => new(_configs, ConfigManager.Instance.BaseDir, config => ConfigManager.Instance.Save(config, config.ConfigName));

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
            // Global feature configs (scoped ones are built per-workspace by Workspace, not here).
            if (te.ConfigName is not null && !te.Scoped && asm.GetType(te.Name) is { } configType
                && !_configs.ContainsKey(configType))
            {
                try
                {
                    var cfg = (IFeatureConfig)Activator.CreateInstance(configType)!;
                    // ConfigManager.Register is first-wins and returns the canonical instance; use the canonical so all consumers
                    // share it.
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
            // Location backends likewise: the process's registry is where a runtime's sessions find their protocol.
            if (te.Contracts.Contains(LocationProviderName) && asm.GetType(te.Name) is { } lt)
                RegisterLocationProvider(lt);



            // Theme contributions. Fast path: the pack:// URIs captured during a WPF-up scan. Fallback: a
            // scan that ran WITHOUT a WPF Application (e.g. a unit-test run that rebuilt the shared catalog)
            // can't build pack:// URIs, so ThemeUris is null — re-read them from the freshly-loaded live
            // type here, where WPF is up. Without this a headless-built catalog silently drops every
            // feature's theme resources (PostIt.* missing → gray, or a converter throws
            // mid-layout and takes the app down).
            var uris = te.ThemeUris;
            if ((uris is null || uris.Count == 0)
                && asm.GetType(te.Name) is { } themeType
                && typeof(IThemeContribution).IsAssignableFrom(themeType))
            {
                try
                {
                    uris = (Activator.CreateInstance(themeType) as IThemeContribution)?
                        .ResourceDictionaryUris.Select(u => u.ToString()).ToList();
                }
                catch { /* a contribution that can't be instantiated just contributes nothing */ }
            }
            if (uris is { Count: > 0 })
                lock (_themeLock)
                    foreach (var u in uris)
                        if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && _themeContributionUris.Add(uri))
                            themeChanged = true;
        }

        // The feature's own setup for the session, now its global configs are registered and before anything of it is built.
        foreach (var te in entry.Types)
        {
            if (!te.Contracts.Contains(FeatureStartupName) || asm.GetType(te.Name) is not { } startupType) continue;
            try { ((IFeatureStartup)Activator.CreateInstance(startupType)!).Start(StartupContext()); }
            catch (Exception ex) { ShellSafetyLog.Instance.Failed($"setting up {startupType.FullName} for the session", ex); }
        }

        if (themeChanged)
        {
            lock (_themeLock) _themeGen++;
            RequestThemeRefresh();
        }
    }

    /// <summary>
    /// Registers every location backend the catalog knows, loading the assemblies that hold them — what a runtime calls before
    /// it mounts its connections, since a backend's assembly may not have activated yet. A backend already registered stays
    /// as it is.
    /// </summary>
    public void EnsureLocationProviders()
    {
        foreach (var type in Catalog.TypesImplementing<ILocationProvider>())
            RegisterLocationProvider(type);
    }

    private void RegisterLocationProvider(Type type)
    {
        if (_locationProviders.Providers.Any(p => p.GetType() == type)) return;
        try { if (Activator.CreateInstance(type) is ILocationProvider provider) _locationProviders.Register(provider); }
        catch { /* a backend that cannot be built reaches nothing; its connections are listed unreachable */ }
    }

    /// <summary>Synchronously folds any pending feature theme contributions into the active theme — called
    /// from <see cref="CreateTab"/> (UI thread) so a feature whose tab is being opened has its theme
    /// resources merged before its view loads, even if a background warm-up only just activated it and its
    /// queued (async) theme refresh hasn't run yet.</summary>
    private void EnsureThemeApplied()
    {
        if (_ui.IsCurrent) ApplyThemeNow();
    }

    // Re-merges the accumulated feature theme contributions below the active theme. On the UI thread this
    // runs inline (so the first page's theme is ready before it renders); off-thread (warm-up) it is posted
    // asynchronously and coalesced — never a synchronous Invoke, which would deadlock against an on-demand
    // load holding the catalog lock on the UI thread.
    private void RequestThemeRefresh()
    {
        if (_ui.IsCurrent) { ApplyThemeNow(); return; }
        if (_themeRefreshQueued) return;
        _themeRefreshQueued = true;
        _ui.PostInBackground(() => { _themeRefreshQueued = false; ApplyThemeNow(); });
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
    /// The instance of <paramref name="targetType"/> built for <paramref name="workspace"/> — built the first time, from
    /// constructor args resolved from the runtime and the registered global config instances, and kept in its
    /// <see cref="WorkspaceRuntime.Features"/> until the runtime renews its services or ends.
    /// <para>
    /// <b>Never returns null — it throws.</b> Every way this can fail is a wiring defect: an unknown
    /// constructor parameter, a global config that never registered, a scoped config whose load threw, a
    /// constructor that threw, or a runtime that has already ended. Returning null for any of those is how a
    /// feature silently disappears — the caller enumerating implementations just skips it, nothing logs, and the
    /// symptom surfaces months later as "that never worked".
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The type has no public constructor, no constructor whose parameters can be
    /// satisfied, or the runtime has ended.</exception>
    public object Instantiate(Type targetType, WorkspaceRuntime workspace)
        => workspace.Features.GetOrBuild(targetType, () => BuildOrThrow(targetType, workspace));

    private object BuildOrThrow(Type t, WorkspaceRuntime workspace)
    {
        var ctors = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).ToList();
        if (ctors.Count == 0)
            throw new InvalidOperationException(
                $"The feature DI cannot build {t.FullName}: it has no public constructor.");

        // Longest-first, so an optional dependency is taken when it can be. The reason the FIRST failure is
        // the one reported: overloads are tried in descending arity, so it names the richest constructor the
        // author meant to be used rather than whatever minimal one happened to be last.
        string? defect = null;

        foreach (var ctor in ctors)
        {
            var args = TryResolveArgs(ctor, workspace, out var detail);
            if (args is null) { defect ??= detail; continue; }

            try { return ctor.Invoke(args)!; }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                // A constructor that throws is a defect in the feature, and swallowing it here would present
                // as the feature simply not existing. Rethrow the real exception with its original stack.
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;   // unreachable; satisfies definite-assignment
            }
        }

        throw new InvalidOperationException(
            $"The feature DI cannot build {t.FullName}: {defect}. It implements a discovered contract, so "
            + "returning null instead would leave the caller enumerating implementations to skip it in "
            + "silence, and the feature would look like it was never written.");
    }

    /// <summary>
    /// The constructor arguments, or null with <paramref name="defect"/> describing why none could be found.
    /// <para>
    /// Every failure here is a defect rather than a tolerable state. A runtime is built with its
    /// <see cref="IShellServices"/> and <c>IAIService</c>, so a feature always finds both.
    /// </para>
    /// </summary>
    private object?[]? TryResolveArgs(ConstructorInfo ctor, WorkspaceRuntime workspace, out string? defect)
    {
        var parms = ctor.GetParameters();
        var args  = new object?[parms.Length];
        defect    = null;

        for (int i = 0; i < parms.Length; i++)
        {
            var pt   = parms[i].ParameterType;
            var name = parms[i].Name;

            if (pt == typeof(WorkspaceRuntime))
            {
                args[i] = workspace;
            }
            else if (typeof(IShellServices).IsAssignableFrom(pt))
            {
                args[i] = workspace.ShellServices;
            }
            else if (typeof(IAIService).IsAssignableFrom(pt))
            {
                // A workspace with no AI provider still has an AIService, which answers that it has none.
                args[i] = workspace.AiService;
            }
            else if (pt == typeof(IVirtualFileSystem))
            {
                // The runtime's own face of the file system — it routes the locations this runtime connected,
                // which the process-wide VirtualFileSystem.Instance cannot see.
                args[i] = workspace.Vfs;
            }
            else if (pt == typeof(ILocationPublisher))
            {
                // Where a feature publishes a tree of its own for as long as it serves it — the same runtime file system, so
                // what it publishes is reached by every page of the runtime.
                args[i] = workspace.Vfs;
            }
            else if (pt == typeof(ILocationDirectory))
            {
                // The locations this runtime has mounted, as a page lists them — the same runtime file system.
                args[i] = workspace.Vfs;
            }
            else if (_tools.TryGetValue(pt, out var tool))
            {
                args[i] = tool;
            }
            else if (pt.IsDefined(typeof(FeatureServiceAttribute), inherit: false))
            {
                // A feature's own service: the runtime's one instance, from the cache its registrations come from, built with
                // the same injection the first time something asks for it.
                if (!workspace.Features.BeginService(pt))
                    return Defect(out defect, $"the feature service '{pt.Name}' required by parameter '{name}' needs itself to be built");
                try { args[i] = Instantiate(pt, workspace); }
                finally { workspace.Features.EndService(pt); }
            }
            else if (pt == typeof(IReadOnlyDictionary<Type, IFeatureConfig>))
            {
                args[i] = BuildScopedConfigView(workspace);
            }
            else if (typeof(IFeatureConfig).IsAssignableFrom(pt))
            {
                // Scoped config: workspace-specific instance (lazily materialized).
                if (workspace.Workspace.FindWorkspaceConfig(pt, this) is { } scoped)
                    args[i] = scoped;
                else if (IsWorkspaceScopedConfig(pt))
                    return Defect(out defect, $"the workspace-scoped config '{pt.Name}' required by parameter "
                        + $"'{name}' could not be materialized for this workspace (its construction or its "
                        + "load from disk threw)");
                else
                {
                    // Global config — ensure its owning assembly has been activated, then resolve.
                    if (!_configs.ContainsKey(pt) && pt.FullName is { } fn)
                        Catalog.ActivateAssemblyOwning(fn);
                    if (_configs.TryGetValue(pt, out var cfg)) args[i] = cfg;
                    else
                        // Activation is what registers a global config, so reaching here means the owning
                        // assembly didn't ship one, its ConfigName is missing, or its construction threw.
                        return Defect(out defect, $"the global config '{pt.Name}' required by parameter "
                            + $"'{name}' is not registered, even after activating the assembly that owns it");
                }
            }
            // IReadOnlyList<ISubfeatureHandle<T>> — the lazy form, and the one a feature should ask for:
            // every discovered plugin implementing T as a descriptor, with NO assembly loaded.
            else if (SubfeatureArg.IsHandleList(pt, out var handleContract))
            {
                args[i] = _subfeatures.Handles(handleContract!, workspace);
            }
            // IReadOnlyList<T> where T is a contract some [Subfeature] implements — the eager form. Loads
            // every owning assembly, so it only suits a small fixed plugin set.
            else if (SubfeatureArg.IsInstanceList(pt, out var instanceContract))
            {
                args[i] = _subfeatures.Resolve(instanceContract!, workspace);
            }
            else if (parms[i].IsOptional)
            {
                args[i] = Type.Missing;
            }
            else
            {
                return Defect(out defect, $"parameter '{name}' is of type '{pt.Name}', which the feature DI "
                    + "does not supply (it injects IShellServices, IAIService, IVirtualFileSystem, ILocationPublisher, WorkspaceRuntime, the app-wide tools such as IPictureTools, "
                    + "IFeatureConfig, a feature's own [FeatureService] classes, and subfeature sets as IReadOnlyList<ISubfeatureHandle<T>> or "
                    + "IReadOnlyList<T>). If you meant a subfeature set, check that at least one type "
                    + "carrying [Subfeature] implements that contract and shipped as a scanned "
                    + "Nexaflow.Features.*.dll");
            }
        }
        return args;

        static object?[]? Defect(out string? defect, string reason)
        {
            defect = reason;
            return null;
        }
    }

    // ── Typed Get* helpers ────────────────────────────────────────────────

    public IReadOnlyList<IQueryHandler> GetQueryHandlers(WorkspaceRuntime ctx)
        => Instantiate<IQueryHandler>(Catalog.TypesImplementing<IQueryHandler>(), ctx);

    public IReadOnlyList<IRibbonPinHandler> GetRibbonPinHandlers(WorkspaceRuntime ctx)
        => Instantiate<IRibbonPinHandler>(Catalog.TypesImplementing<IRibbonPinHandler>(), ctx);

    /// <summary>Contributors of extra "This PC" rows, for shell surfaces that show them outside the file
    /// browser — the themed file and folder pickers, which have no <see cref="IShellServices"/> of their
    /// own and so cannot go through <c>FileSystemFeatureRegistry</c>.</summary>
    public IReadOnlyList<IThisPcItemProvider> GetThisPcItemProviders(WorkspaceRuntime ctx)
        => Instantiate<IThisPcItemProvider>(Catalog.TypesImplementing<IThisPcItemProvider>(), ctx);

    /// <summary>Chat-bar key handlers (Up/Down history, Tab completion) for this workspace.</summary>
    public IReadOnlyList<IChatKeyHandler> GetChatKeyHandlers(WorkspaceRuntime ctx)
        => Instantiate<IChatKeyHandler>(Catalog.TypesImplementing<IChatKeyHandler>(), ctx);

    /// <summary>Chat-bar drop handlers (drag a file → insert its path) for this workspace.</summary>
    public IReadOnlyList<IChatDropHandler> GetChatDropHandlers(WorkspaceRuntime ctx)
        => Instantiate<IChatDropHandler>(Catalog.TypesImplementing<IChatDropHandler>(), ctx);

    /// <summary>The runtime's breadcrumb display filters in stacking order (<see cref="IBreadcrumbDisplayFilter.Order"/>).</summary>
    public IReadOnlyList<IBreadcrumbDisplayFilter> GetBreadcrumbDisplayFilters(WorkspaceRuntime ctx)
        => [.. Instantiate<IBreadcrumbDisplayFilter>(Catalog.TypesImplementing<IBreadcrumbDisplayFilter>(), ctx)
               .OrderBy(f => f.Order)];

    /// <summary>Chat-bar input-preview handlers (live echo of what you're typing) for this workspace.</summary>
    public IReadOnlyList<IChatInputPreview> GetChatInputPreviews(WorkspaceRuntime ctx)
        => Instantiate<IChatInputPreview>(Catalog.TypesImplementing<IChatInputPreview>(), ctx);

    /// <summary>The tab-pin handler that snapshots tabs of <paramref name="tabPageKind"/>, or null.</summary>
    public ITabPinHandler? GetTabPinHandler(string tabPageKind, WorkspaceRuntime ctx)
        => Instantiate<ITabPinHandler>(Catalog.TypesImplementing<ITabPinHandler>(), ctx)
            .FirstOrDefault(h => h.TabPageKind == tabPageKind);

    /// <summary>The click-time executor for ribbon buttons of <paramref name="pageKind"/>, or null
    /// (in which case the button simply opens that page kind as a tab).</summary>
    public IRibbonItemExecutor? GetRibbonItemExecutor(string pageKind, WorkspaceRuntime ctx)
        => Instantiate<IRibbonItemExecutor>(Catalog.TypesImplementing<IRibbonItemExecutor>(), ctx)
            .FirstOrDefault(e => e.PageKind == pageKind);

    /// <summary>
    /// Lightweight <see cref="Page"/> definitions for the page kinds that can be created without specific
    /// context (<see cref="IPageRegistration.CanBeContextItem"/>) in this workspace — offered in the AI
    /// conversation's "add context" menu. Resolving each registration loads its feature assembly.
    /// </summary>
    public IReadOnlyList<Page> GetContextItemPages(WorkspaceRuntime ctx)
    {
        var pages = new List<Page>();
        foreach (var pageKind in Catalog.PageKinds())
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
        foreach (var pageKind in Catalog.PageKinds())
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
        => Catalog.TryGetPageRegistrationType(pageKind, out var regType) && regType is not null
            ? Instantiate(regType, ctx) as IPageRegistration
            : null;

    // ── Tab creation ──────────────────────────────────────────────────────

    public bool IsRegistered(string pageKind) => Catalog.HasPageKind(pageKind);

    /// <summary>The version of the feature assembly that owns <paramref name="pageKind"/>, or null if unknown.</summary>
    public string? GetPageKindVersion(string pageKind) => Catalog.GetPageKindVersion(pageKind);

    public Page? CreateTab(string pageKind, WorkspaceRuntime workspace,
                           Dictionary<string, string>? pageParams = null)
    {
        if (ResolveRegistration(pageKind, workspace) is not { } reg) return null;
        EnsureThemeApplied();   // make sure this feature's theme contribution is merged before its view loads
        var tab = reg.CreatePageDefinition(pageParams);
        if (tab is not null)
        {
            tab.PageKind = pageKind;
            // Record the params the tab was opened with. When opened with none, keep any defaults the
            // registration set on the page itself (e.g. a standalone tab that wants its own identity so
            // it isn't a null-param "matches anything" singleton).
            tab.PageParams     = pageParams ?? tab.PageParams;
            tab.LocationParams = LocationParamsOf(reg);
        }
        return tab;
    }

    /// <summary>
    /// The parameter names <paramref name="reg"/> declares as non-identity — the ones that locate a view
    /// inside the document rather than name it. Resolved once here, at tab creation, so tab dedup never has
    /// to reach back through the catalog (and load a feature assembly) to ask.
    /// </summary>
    private static IReadOnlySet<string>? LocationParamsOf(IPageRegistration reg)
    {
        HashSet<string>? names = null;
        foreach (var p in reg.Parameters)
            if (!p.Identity)
                (names ??= []).Add(p.Name);
        return names;
    }

    /// <summary>
    /// The openable page kinds and the parameters each accepts (<see cref="IPageRegistration.Parameters"/>),
    /// built per-workspace. Used to describe available pages to the AI. Resolving loads the feature assemblies.
    /// </summary>
    public IReadOnlyList<(string PageKind, IReadOnlyList<PageParameter> Parameters)> GetPageCatalog(WorkspaceRuntime ctx)
    {
        var result = new List<(string, IReadOnlyList<PageParameter>)>();
        foreach (var pageKind in Catalog.PageKinds())
            if (ResolveRegistration(pageKind, ctx) is { } reg)
                result.Add((pageKind, reg.Parameters));
        return result;
    }

    public IReadOnlyList<string> GetPageKindsForConfig(Type configType, WorkspaceRuntime ctx)
    {
        if (configType.FullName is not { } fn) return [];
        var regTypes = Catalog.ResolvePageRegistrationsForConfig(fn);
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
