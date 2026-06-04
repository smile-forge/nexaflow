using Nexaflow.Core.Models;
using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Core;

/// <summary>
/// Singleton registry of feature-level tab factories, configs, query handlers,
/// keyboard handlers, drop targets and ribbon-pin handlers. Call
/// <see cref="RegisterFeatures"/> at startup to load every
/// <c>Nexaflow.Features.*.dll</c> and record the relevant types without
/// instantiation. Call <see cref="Instantiate"/> or the typed <c>Get*</c>
/// helpers at runtime with a <see cref="Workspace"/> so each instance
/// receives the correct scoped <see cref="IShellServices"/> and
/// <see cref="IAIService"/>.
///
/// File-system contracts (<c>IFileAction</c>, <c>IFolderAction</c>,
/// <c>IFileCreateAction</c>, <c>IFolderViewlet</c>) are intentionally NOT
/// managed here — they live in
/// <see cref="Services.FileSystemFeatureRegistry"/>.
/// </summary>
public sealed class FeatureManager
{
    public static FeatureManager Instance { get; } = new();

    // ── Type registries ───────────────────────────────────────────────────

    private readonly Dictionary<string, Type> _registrationTypes        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, List<Type>> _configToRegTypes     = new();
    // Per-assembly GLOBAL config instances — populated during RegisterFeatures, never changed after.
    private readonly Dictionary<Type, IFeatureConfig> _configs          = new();
    // Config TYPES marked [WorkspaceScopedConfig]: never registered globally; one instance is built
    // per profile (see Profile.WorkspaceConfigs) and injected from the owning workspace's profile.
    private readonly List<Type> _workspaceScopedConfigTypes             = [];

    /// <summary>
    /// Read-only view of the per-assembly <see cref="IFeatureConfig"/> instances
    /// discovered during <see cref="RegisterFeatures"/>. Exposed for feature-scoped
    /// registries (e.g. <see cref="Services.FileSystemFeatureRegistry"/>) that need
    /// to resolve config types as constructor args without going through the shell.
    /// </summary>
    public IReadOnlyDictionary<Type, IFeatureConfig> Configs => _configs;

    /// <summary>
    /// Config types marked <see cref="WorkspaceScopedConfigAttribute"/>. A <see cref="Profile"/>
    /// instantiates one of each and loads it from its own folder; the per-workspace Configure panel
    /// edits them. See <see cref="Models.Profile.WorkspaceConfigs"/>.
    /// </summary>
    public IReadOnlyList<Type> WorkspaceScopedConfigTypes => _workspaceScopedConfigTypes;

    private readonly List<Type> _keyboardHandlerTypes  = [];
    private readonly List<Type> _dropTargetTypes       = [];
    private readonly List<Type> _queryHandlerTypes     = [];
    private readonly List<Type> _ribbonPinHandlerTypes = [];

    // Theme resource dictionaries contributed by features (see IThemeContribution) — pack URIs only,
    // gathered during RegisterFeatures so ThemeManager can merge them without Core referencing any feature.
    private readonly List<Uri> _themeContributionUris = [];

    // ── Read-only type lists (for callers that need the raw types) ────────

    public IReadOnlyList<Type> KeyboardHandlerTypes  => _keyboardHandlerTypes;
    public IReadOnlyList<Type> DropTargetTypes       => _dropTargetTypes;

    /// <summary>
    /// Pack URIs of every feature-contributed theme <c>ResourceDictionary</c>, discovered by
    /// reflection during <see cref="RegisterFeatures"/>. Passed to <c>ThemeManager.Apply</c> so the
    /// dictionaries merge below the active theme (fallbacks a theme may override). Usually empty.
    /// </summary>
    public IReadOnlyList<Uri> ThemeContributionUris => _themeContributionUris;

    // ── Per-(Type, Workspace) instance cache ────────────────────────────

    private readonly Dictionary<Workspace, Dictionary<Type, object>> _cache = new();
    private readonly object _cacheLock = new();

    // ── Registration ──────────────────────────────────────────────────────

    public void RegisterFeatures()
    {
        string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var featureDlls = Directory.GetFiles(exeDir, "Nexaflow.Features.*.dll");

        foreach (var dll in featureDlls)
        {
            var asmName = AssemblyName.GetAssemblyName(dll);
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == asmName.Name))
                Assembly.LoadFrom(dll);
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name!.StartsWith("Nexaflow.Features."))
                Register(asm);
        }

        // Core itself contains action / handler / viewlet / page-registration implementations.
        Register(typeof(FeatureManager).Assembly);
    }

    public void Register(Assembly asm)
    {
        var types = asm.GetTypes();

        // 1. Discover IFeatureConfig types. Global ones are registered with ConfigManager and shared
        //    across all profiles; [WorkspaceScopedConfig] ones are NOT registered globally — only their
        //    type is recorded, so each Profile builds its own instance under Contexts\<name>\.
        var localConfigs = new Dictionary<Type, IFeatureConfig>();
        foreach (var t in types.Where(t => !t.IsAbstract && !t.IsInterface
                                           && typeof(IFeatureConfig).IsAssignableFrom(t)))
        {
            if (t.GetCustomAttribute<WorkspaceScopedConfigAttribute>() is not null)
            {
                if (!_workspaceScopedConfigTypes.Contains(t))
                    _workspaceScopedConfigTypes.Add(t);
                continue;
            }

            var cfg = (IFeatureConfig)Activator.CreateInstance(t)!;
            ConfigManager.Instance.Register(cfg, cfg.ConfigName);
            localConfigs[t]  = cfg;
            _configs[t]      = cfg;
        }

        // 2. Discover IPageRegistration types and build config → reg-type mapping.
        var registrationTypes = types
            .Where(t => !t.IsAbstract && !t.IsInterface
                        && typeof(IPageRegistration).IsAssignableFrom(t))
            .ToList();

        foreach (var regType in registrationTypes)
        {
            // Read the static PageKind via reflection — no instantiation needed.
            var pageKind = regType
                .GetProperty("StaticPageKind",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                ?.GetValue(null) as string;
            if (pageKind is null) continue;

            _registrationTypes[pageKind] = regType;

            // Build config → registration-type mapping by inspecting ctor parameters.
            var ctor = BestConstructor(regType);
            foreach (var p in ctor.GetParameters())
            {
                if (typeof(IFeatureConfig).IsAssignableFrom(p.ParameterType))
                {
                    if (!_configToRegTypes.TryGetValue(p.ParameterType, out var list))
                    {
                        list = [];
                        _configToRegTypes[p.ParameterType] = list;
                    }
                    list.Add(regType);
                }
            }
        }

        RegisterTypes(asm, localConfigs);
    }

    /// <summary>
    /// Scans <paramref name="asm"/> for all discoverable non-registration types and
    /// appends them to the appropriate type lists. No instantiation occurs here.
    /// </summary>
    private void RegisterTypes(Assembly asm, Dictionary<Type, IFeatureConfig> localConfigs)
    {
        foreach (var t in asm.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface))
        {
            if (typeof(IKeyboardHandler).IsAssignableFrom(t)) _keyboardHandlerTypes.Add(t);
            if (typeof(IDropTarget).IsAssignableFrom(t))      _dropTargetTypes.Add(t);
            if (typeof(IQueryHandler).IsAssignableFrom(t))    _queryHandlerTypes.Add(t);
            if (typeof(IRibbonPinHandler).IsAssignableFrom(t)) _ribbonPinHandlerTypes.Add(t);

            // Theme contributions are read once, up front (no Workspace needed) — a feature opts in
            // by advertising pack URIs of dictionaries to merge below the active theme.
            if (typeof(IThemeContribution).IsAssignableFrom(t))
            {
                try
                {
                    if (Activator.CreateInstance(t) is IThemeContribution contribution)
                        _themeContributionUris.AddRange(contribution.ResourceDictionaryUris);
                }
                catch { }
            }
        }
    }

    // ── Instantiation ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates (or returns a cached) instance of <paramref name="targetType"/>
    /// with constructor args resolved from <paramref name="workspace"/> and
    /// the per-assembly config instances discovered during <see cref="RegisterFeatures"/>.
    /// Returns null when no satisfiable constructor is found.
    /// </summary>
    public object? Instantiate(Type targetType, Workspace workspace)
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
    /// Drops all cached feature/handler instances built for <paramref name="workspace"/>. Called
    /// when a Workspace is reconfigured (its AIService/providers were replaced) or disposed, so the
    /// next request rebuilds handlers against the live services. See WorkspaceManager.
    /// </summary>
    public void EvictWorkspace(Workspace workspace)
    {
        lock (_cacheLock)
            _cache.Remove(workspace);
    }

    private object? TryInstantiateInternal(Type t, Workspace workspace)
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

    private object?[]? TryResolveArgs(ConstructorInfo ctor, Workspace? workspace)
    {
        var parms = ctor.GetParameters();
        var args  = new object?[parms.Length];
        for (int i = 0; i < parms.Length; i++)
        {
            var pt = parms[i].ParameterType;
            if (pt == typeof(Workspace))
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
            else if (workspace?.Profile.FindWorkspaceConfig(pt) is { } scoped)
            {
                args[i] = scoped;
            }
            else if (_workspaceScopedConfigTypes.Contains(pt))
            {
                // Scoped type that this (null/window-less) workspace can't supply — unsatisfiable.
                return null;
            }
            else if (_configs.TryGetValue(pt, out var cfg))
            {
                args[i] = cfg;
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

    public IReadOnlyList<IQueryHandler> GetQueryHandlers(Workspace ctx)
        => Instantiate<IQueryHandler>(_queryHandlerTypes, ctx);

    public IReadOnlyList<IRibbonPinHandler> GetRibbonPinHandlers(Workspace ctx)
        => Instantiate<IRibbonPinHandler>(_ribbonPinHandlerTypes, ctx);

    public IRibbonPinHandler? GetRibbonPinHandler(string contentKind, Workspace ctx)
        => GetRibbonPinHandlers(ctx).FirstOrDefault(h => h.ContentKind == contentKind);

    /// <summary>
    /// Lightweight <see cref="Page"/> definitions for the page kinds that can be created without
    /// specific context (<see cref="IPageRegistration.CanBeContextItem"/>) in this workspace — offered
    /// in the AI conversation's "add context" menu. Content is not realized (the menu reads Title/Icon
    /// from the definition); the chosen page is realized via <c>GetOrCreateContent</c> when pinned.
    /// </summary>
    public IReadOnlyList<Page> GetContextItemPages(Workspace ctx)
    {
        var pages = new List<Page>();
        foreach (var (pageKind, regType) in _registrationTypes)
            if (Instantiate(regType, ctx) is IPageRegistration { CanBeContextItem: true }
                && CreateTab(pageKind, ctx) is { } page)
                pages.Add(page);
        return pages;
    }

    private IReadOnlyList<T> Instantiate<T>(List<Type> types, Workspace ctx)
    {
        var result = new List<T>(types.Count);
        foreach (var t in types)
        {
            if (Instantiate(t, ctx) is T instance)
                result.Add(instance);
        }
        return result;
    }

    // ── Tab creation ──────────────────────────────────────────────────────

    public bool IsRegistered(string pageKind) => _registrationTypes.ContainsKey(pageKind);

    public Page? CreateTab(string pageKind, Workspace workspace,
                           Dictionary<string, string>? pageParams = null)
    {
        if (!_registrationTypes.TryGetValue(pageKind, out var regType)) return null;
        var reg = Instantiate(regType, workspace) as IPageRegistration;
        if (reg is null) return null;
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
    /// built per-workspace. Used to describe available pages to the AI.
    /// </summary>
    public IReadOnlyList<(string PageKind, IReadOnlyList<PageParameter> Parameters)> GetPageCatalog(Workspace ctx)
    {
        var result = new List<(string, IReadOnlyList<PageParameter>)>(_registrationTypes.Count);
        foreach (var (pageKind, regType) in _registrationTypes)
        {
            if (Instantiate(regType, ctx) is IPageRegistration reg)
                result.Add((pageKind, reg.Parameters));
        }
        return result;
    }

    public IReadOnlyList<string> GetPageKindsForConfig(Type configType, Workspace ctx)
    {
        if (!_configToRegTypes.TryGetValue(configType, out var regTypes)) return [];
        var pageKinds = new List<string>(regTypes.Count);
        foreach (var regType in regTypes)
        {
            if (Instantiate(regType, ctx) is IPageRegistration reg)
                pageKinds.Add(reg.PageKind);
        }
        return pageKinds;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ConstructorInfo BestConstructor(Type t)
        => t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

    /// <summary>
    /// The <see cref="IFeatureConfig"/> view handed to feature-scoped registries
    /// (e.g. <see cref="Services.FileSystemFeatureRegistry"/>). For a live workspace this is the
    /// global <see cref="_configs"/> overlaid with that workspace's profile's per-workspace scoped
    /// instances, so a viewlet ctor that takes a scoped config (e.g. ProjectsConfig) resolves the
    /// profile-specific instance. Falls back to the raw global map when there's no workspace.
    /// </summary>
    private IReadOnlyDictionary<Type, IFeatureConfig> BuildScopedConfigView(Workspace? workspace)
    {
        if (workspace?.Profile.WorkspaceConfigs is not { Count: > 0 } scoped)
            return _configs;

        var merged = new Dictionary<Type, IFeatureConfig>(_configs);
        foreach (var cfg in scoped)
            merged[cfg.GetType()] = cfg;
        return merged;
    }
}
