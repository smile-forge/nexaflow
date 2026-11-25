using Microsoft.Extensions.Logging;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;



namespace Nexaflow.Core;

/// <summary>
/// The session's feature system. One per session — <see cref="ShellHost"/> makes it and hands it to every window
/// (<see cref="WindowServices"/>) and runtime (<see cref="RuntimeSessionServices"/>, carried as
/// <see cref="WorkspaceRuntime.FeatureManager"/>). Its parts each do one thing: the <see cref="Catalog"/> knows which feature type
/// implements what, from a cached index, without loading every feature assembly; <see cref="Activation"/> takes each assembly into
/// the session as it loads; <see cref="Configs"/> holds the features' configs; and the <see cref="Injector"/> builds a feature's
/// types for a runtime from what <see cref="FeatureInjectables"/> supplies. What is left here is what the shell asks of the
/// features as a whole: a tab of a page kind, the pages there are, and the handlers its chat bar, ribbon and file views offer.
///
/// File-system contracts (<c>IFileAction</c>, <c>IFolderAction</c>, <c>IFileCreateAction</c>, <c>IFolderViewlet</c>) are discovered
/// through <see cref="IShellServices.DiscoverImplementations{T}"/> by the file system's own registry, not here.
/// </summary>
public sealed class FeatureManager
{
    /// <summary>Resolves the sets of subfeatures a feature asks for, from the catalogue.</summary>
    private readonly SubfeatureCatalog _subfeatures;

    /// <summary>
    /// The session's feature system over <paramref name="catalog"/>: builds the catalogue's (cached) discovery index — no feature
    /// assembly is loaded on the cache-hit path — and activates Core. A feature's global configs are registered in
    /// <paramref name="config"/>, its theme is folded in by <paramref name="themes"/> on <paramref name="ui"/>, however its assembly
    /// came to load, and its location backends are registered in <paramref name="locationProviders"/>. A feature whose constructor
    /// asks for an <see cref="ILogger{T}"/> is given one made by <paramref name="logs"/>, the session's logs, which also hear of a
    /// feature's setup that failed.
    /// </summary>
    public FeatureManager(FeatureCatalog catalog, ConfigManager config, IUiThread ui, IThemeHost themes,
                          LocationProviderRegistry locationProviders, ILoggerFactory logs)
    {
        Catalog      = catalog;
        Configs      = new FeatureConfigs(catalog, config);
        Injector     = new FeatureInjector(new FeatureInjectables());
        _subfeatures = new SubfeatureCatalog(catalog, Injector);
        Supply(Injector.Injectables, logs);
        Activation   = new FeatureActivation(catalog, Configs, ui, themes, locationProviders, logs.CreateLogger<FeatureActivation>());
        Activation.Start();
    }

    /// <summary>Which feature type implements which contract, and the assemblies loaded as they are needed.</summary>
    public FeatureCatalog Catalog { get; }

    /// <summary>The features' configs: the global ones registered as features activate, and which are a workspace's own.</summary>
    public FeatureConfigs Configs { get; }

    /// <summary>Each feature assembly taken into the session as it loads, with what it put in.</summary>
    public FeatureActivation Activation { get; }

    /// <summary>Builds a feature's types for a runtime.</summary>
    public FeatureInjector Injector { get; }

    /// <summary>Gives <paramref name="tool"/> to every feature whose constructor asks for <typeparamref name="T"/>: the app-wide tools
    /// — <see cref="IPictureTools"/> and the like — each provided once, at start, and shared by every workspace's features.</summary>
    public void ProvideTool<T>(T tool) where T : class => Injector.Injectables.Add<T>(typeof(T).Name, _ => tool);

    /// <summary>What the session supplies every feature's constructor, in the order a parameter is matched against it.</summary>
    private void Supply(FeatureInjectables injectables, ILoggerFactory logs)
    {
        injectables.Add<WorkspaceRuntime>(nameof(WorkspaceRuntime), runtime => runtime);
        injectables.Add<IShellServices>(nameof(IShellServices), runtime => runtime.ShellServices);
        // A workspace with no AI provider still has an AIService, which answers that it has none.
        injectables.Add<IAIService>(nameof(IAIService), runtime => runtime.Assistant);
        // The runtime's own face of the file system — it routes the locations this runtime connected, which the process-wide
        // VirtualFileSystem.Instance cannot see; where a feature publishes a tree of its own for as long as it serves it; and the
        // locations the runtime has mounted, as a page lists them.
        injectables.Add<IVirtualFileSystem>(nameof(IVirtualFileSystem), runtime => runtime.Vfs);
        injectables.Add<ILocationPublisher>(nameof(ILocationPublisher), runtime => runtime.Vfs);
        injectables.Add<ILocationDirectory>(nameof(ILocationDirectory), runtime => runtime.Vfs);
        // The feature's own log, in the session's: its entries reach every destination under the category the type argument names.
        injectables.Add("ILogger<T>",
            asked => asked.IsGenericType && asked.GetGenericTypeDefinition() == typeof(ILogger<>),
            (asked, _) => Supplied.With(Activator.CreateInstance(typeof(Logger<>).MakeGenericType(asked.GetGenericArguments()), logs)));
        // A feature's own service: the runtime's one instance, built with the same injection the first time something asks for it.
        injectables.Add("a feature's own [FeatureService] classes",
            asked => asked.IsDefined(typeof(FeatureServiceAttribute), inherit: false),
            (asked, runtime) =>
            {
                if (!runtime.Features.BeginService(asked))
                    return Supplied.Refused($"the feature service '{asked.Name}' needs itself to be built");
                try { return Supplied.With(Injector.Instantiate(asked, runtime)); }
                finally { runtime.Features.EndService(asked); }
            });
        injectables.Add<IReadOnlyDictionary<Type, IFeatureConfig>>("the configs as IReadOnlyDictionary<Type, IFeatureConfig>",
            runtime => Configs.ViewFor(runtime));
        injectables.Add("IFeatureConfig",
            asked => typeof(IFeatureConfig).IsAssignableFrom(asked),
            (asked, runtime) =>
                runtime.Workspace.FindWorkspaceConfig(asked, Configs) is { } scoped ? Supplied.With(scoped)
                : Configs.IsWorkspaceScoped(asked)
                    ? Supplied.Refused($"the workspace-scoped config '{asked.Name}' could not be materialized for this workspace "
                                       + "(its construction or its load from disk threw)")
                : Configs.Find(asked) is { } global ? Supplied.With(global)
                    // Activation is what registers a global config, so reaching here means the owning assembly didn't ship one,
                    // its ConfigName is missing, or its construction threw.
                    : Supplied.Refused($"the global config '{asked.Name}' is not registered, even after activating the assembly that owns it"));
        // The lazy form of a subfeature set, and the one a feature should ask for: every discovered plugin implementing T as a
        // descriptor, with no assembly loaded.
        injectables.Add("subfeature sets as IReadOnlyList<ISubfeatureHandle<T>>",
            asked => SubfeatureArg.IsHandleList(asked, out _),
            (asked, runtime) => SubfeatureArg.IsHandleList(asked, out var contract) ? Supplied.With(_subfeatures.Handles(contract!, runtime)) : default);
        // The eager form: loads every owning assembly, so it only suits a small fixed plugin set.
        injectables.Add("subfeature sets as IReadOnlyList<T>",
            asked => SubfeatureArg.IsInstanceList(asked, out _),
            (asked, runtime) => SubfeatureArg.IsInstanceList(asked, out var contract) ? Supplied.With(_subfeatures.Resolve(contract!, runtime)) : default);
    }

    // ── Typed Get* helpers ────────────────────────────────────────────────

    public IReadOnlyList<IQueryHandler> GetQueryHandlers(WorkspaceRuntime ctx)
        => Injector.Instantiate<IQueryHandler>(Catalog.TypesImplementing<IQueryHandler>(), ctx);

    public IReadOnlyList<IRibbonPinHandler> GetRibbonPinHandlers(WorkspaceRuntime ctx)
        => Injector.Instantiate<IRibbonPinHandler>(Catalog.TypesImplementing<IRibbonPinHandler>(), ctx);

    /// <summary>Contributors of extra "This PC" rows, for shell surfaces that show them outside the file
    /// browser — the themed file and folder pickers, which have no <see cref="IShellServices"/> of their
    /// own and so cannot go through <c>FileSystemFeatureRegistry</c>.</summary>
    public IReadOnlyList<IThisPcItemProvider> GetThisPcItemProviders(WorkspaceRuntime ctx)
        => Injector.Instantiate<IThisPcItemProvider>(Catalog.TypesImplementing<IThisPcItemProvider>(), ctx);

    /// <summary>Chat-bar key handlers (Up/Down history, Tab completion) for this workspace.</summary>
    public IReadOnlyList<IChatKeyHandler> GetChatKeyHandlers(WorkspaceRuntime ctx)
        => Injector.Instantiate<IChatKeyHandler>(Catalog.TypesImplementing<IChatKeyHandler>(), ctx);

    /// <summary>Chat-bar drop handlers (drag a file → insert its path) for this workspace.</summary>
    public IReadOnlyList<IChatDropHandler> GetChatDropHandlers(WorkspaceRuntime ctx)
        => Injector.Instantiate<IChatDropHandler>(Catalog.TypesImplementing<IChatDropHandler>(), ctx);

    /// <summary>The runtime's breadcrumb display filters in stacking order (<see cref="IBreadcrumbDisplayFilter.Order"/>).</summary>
    public IReadOnlyList<IBreadcrumbDisplayFilter> GetBreadcrumbDisplayFilters(WorkspaceRuntime ctx)
        => [.. Injector.Instantiate<IBreadcrumbDisplayFilter>(Catalog.TypesImplementing<IBreadcrumbDisplayFilter>(), ctx)
               .OrderBy(f => f.Order)];

    /// <summary>Chat-bar input-preview handlers (live echo of what you're typing) for this workspace.</summary>
    public IReadOnlyList<IChatInputPreview> GetChatInputPreviews(WorkspaceRuntime ctx)
        => Injector.Instantiate<IChatInputPreview>(Catalog.TypesImplementing<IChatInputPreview>(), ctx);

    /// <summary>The tab-pin handler that snapshots tabs of <paramref name="tabPageKind"/>, or null.</summary>
    public ITabPinHandler? GetTabPinHandler(string tabPageKind, WorkspaceRuntime ctx)
        => Injector.Instantiate<ITabPinHandler>(Catalog.TypesImplementing<ITabPinHandler>(), ctx)
            .FirstOrDefault(h => h.TabPageKind == tabPageKind);

    /// <summary>The click-time executor for ribbon buttons of <paramref name="pageKind"/>, or null
    /// (in which case the button simply opens that page kind as a tab).</summary>
    public IRibbonItemExecutor? GetRibbonItemExecutor(string pageKind, WorkspaceRuntime ctx)
        => Injector.Instantiate<IRibbonItemExecutor>(Catalog.TypesImplementing<IRibbonItemExecutor>(), ctx)
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

    private IPageRegistration? ResolveRegistration(string pageKind, WorkspaceRuntime ctx)
        => Catalog.TryGetPageRegistrationType(pageKind, out var regType) && regType is not null
            ? Injector.Instantiate(regType, ctx) as IPageRegistration
            : null;

    // ── Tab creation ──────────────────────────────────────────────────────

    public bool IsRegistered(string pageKind) => Catalog.HasPageKind(pageKind);

    /// <summary>The version of the feature assembly that owns <paramref name="pageKind"/>, or null if unknown.</summary>
    public string? GetPageKindVersion(string pageKind) => Catalog.GetPageKindVersion(pageKind);

    public Page? CreateTab(string pageKind, WorkspaceRuntime workspace,
                           Dictionary<string, string>? pageParams = null)
    {
        if (ResolveRegistration(pageKind, workspace) is not { } reg) return null;
        Activation.EnsureThemeApplied();   // make sure this feature's theme contribution is merged before its view loads
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
            if (Injector.Instantiate(regType, ctx) is IPageRegistration reg)
                pageKinds.Add(reg.PageKind);
        return pageKinds;
    }
}

