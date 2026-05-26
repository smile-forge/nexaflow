using Nexaflow.Core.FileActions;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Core;

/// <summary>
/// Singleton registry of all feature tab factories.
/// Call <see cref="RegisterFeatures"/> at startup; the manager scans all
/// Nexaflow.Features.*.dll assemblies for <see cref="IPageRegistration"/>,
/// <see cref="IFileAction"/>, and related types, recording them without
/// instantiation. Call <see cref="Instantiate"/> or the typed Get* helpers
/// at runtime, passing the active <see cref="WorkContext"/> so each instance
/// receives the correct scoped <see cref="IShellServices"/> and
/// <see cref="IAIService"/>.
/// </summary>
public sealed class FeatureManager
{
    public static FeatureManager Instance { get; } = new();

    // ── Type registries ───────────────────────────────────────────────────

    private readonly Dictionary<string, Type> _registrationTypes        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, List<Type>> _configToRegTypes     = new();
    // Per-assembly config instances — populated during RegisterFeatures, never changed after.
    private readonly Dictionary<Type, IFeatureConfig> _configs          = new();

    private readonly List<Type> _fileActionTypes       = [];
    private readonly List<Type> _folderActionTypes     = [];
    private readonly List<Type> _fileCreateActionTypes = [];
    private readonly List<Type> _keyboardHandlerTypes  = [];
    private readonly List<Type> _dropTargetTypes       = [];
    private readonly List<Type> _queryHandlerTypes     = [];
    private readonly List<Type> _folderViewletTypes    = [];
    private readonly List<Type> _ribbonPinHandlerTypes = [];

    // ── Read-only type lists (for callers that need the raw types) ────────

    public IReadOnlyList<Type> FileActionTypes       => _fileActionTypes;
    public IReadOnlyList<Type> FolderActionTypes     => _folderActionTypes;
    public IReadOnlyList<Type> FileCreateActionTypes => _fileCreateActionTypes;
    public IReadOnlyList<Type> KeyboardHandlerTypes  => _keyboardHandlerTypes;
    public IReadOnlyList<Type> DropTargetTypes       => _dropTargetTypes;

    // ── Experiences (type-level, built from static StaticExperienceId) ────

    private List<string> _allExperiences = [];
    public IReadOnlyList<string> AllExperiences => _allExperiences;

    // ── Per-(Type, WorkContext) instance cache ────────────────────────────

    private readonly Dictionary<WorkContext, Dictionary<Type, object>> _cache = new();
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

        // Core itself contains action / handler / viewlet implementations.
        RegisterTypes(typeof(FeatureManager).Assembly, new Dictionary<Type, IFeatureConfig>());

        // Build the experience list from static metadata on file action types.
        _allExperiences = _fileActionTypes
            .Select(t => t.GetProperty("StaticExperienceId",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                ?.GetValue(null) as string)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public void Register(Assembly asm)
    {
        var types = asm.GetTypes();

        // 1. Discover and register IFeatureConfig types.
        var localConfigs = new Dictionary<Type, IFeatureConfig>();
        foreach (var t in types.Where(t => !t.IsAbstract && !t.IsInterface
                                           && typeof(IFeatureConfig).IsAssignableFrom(t)))
        {
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
            if (typeof(IFolderViewlet).IsAssignableFrom(t))   _folderViewletTypes.Add(t);
            if (typeof(IRibbonPinHandler).IsAssignableFrom(t)) _ribbonPinHandlerTypes.Add(t);

            bool isFile   = typeof(IFileAction).IsAssignableFrom(t);
            bool isFolder = typeof(IFolderAction).IsAssignableFrom(t);
            bool isCreate = typeof(IFileCreateAction).IsAssignableFrom(t);

            // Only cache types whose instances are equivalent per WorkContext.
            // Dynamic types (ShellVerbAction, FolderActionAdapter) are excluded here;
            // they are reached via GetReinitParams/Rehydrate or FindFileAction fallbacks.
            bool isCacheable = typeof(ICacheable).IsAssignableFrom(t);

            if (isFile   && isCacheable && !_fileActionTypes.Contains(t))       _fileActionTypes.Add(t);
            if (isFolder && isCacheable && !_folderActionTypes.Contains(t))     _folderActionTypes.Add(t);
            if (isCreate && isCacheable && !_fileCreateActionTypes.Contains(t)) _fileCreateActionTypes.Add(t);
        }
    }

    // ── Instantiation ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates (or returns a cached) instance of <paramref name="targetType"/>
    /// with constructor args resolved from <paramref name="workContext"/> and
    /// the per-assembly config instances discovered during <see cref="RegisterFeatures"/>.
    /// Returns null when no satisfiable constructor is found.
    /// </summary>
    public object? Instantiate(Type targetType, WorkContext workContext)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(workContext, out var ctxCache))
            {
                ctxCache = new Dictionary<Type, object>();
                _cache[workContext] = ctxCache;
            }

            if (ctxCache.TryGetValue(targetType, out var cached))
                return cached;

            var instance = TryInstantiateInternal(targetType, workContext);
            if (instance is not null)
                ctxCache[targetType] = instance;
            return instance;
        }
    }

    private object? TryInstantiateInternal(Type t, WorkContext workContext)
    {
        foreach (var ctor in t.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var args = TryResolveArgs(ctor, workContext);
            if (args is null) continue;
            try { return ctor.Invoke(args); }
            catch { }
        }
        return null;
    }

    private object?[]? TryResolveArgs(ConstructorInfo ctor, WorkContext? workContext)
    {
        var parms = ctor.GetParameters();
        var args  = new object?[parms.Length];
        for (int i = 0; i < parms.Length; i++)
        {
            var pt = parms[i].ParameterType;
            if (pt == typeof(WorkContext))
            {
                if (workContext is null) return null;
                args[i] = workContext;
            }
            else if (typeof(IShellServices).IsAssignableFrom(pt))
            {
                if (workContext?.ShellServices is null) return null;
                args[i] = workContext.ShellServices;
            }
            else if (typeof(IAIService).IsAssignableFrom(pt))
            {
                if (workContext?.AiService is null) return null;
                args[i] = workContext.AiService;
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

    public IReadOnlyList<IFileAction> GetFileActions(WorkContext ctx)
        => Instantiate<IFileAction>(_fileActionTypes, ctx);

    public IReadOnlyList<IFolderAction> GetFolderActions(WorkContext ctx)
        => Instantiate<IFolderAction>(_folderActionTypes, ctx);

    public IReadOnlyList<IFileCreateAction> GetFileCreateActions(WorkContext ctx)
        => Instantiate<IFileCreateAction>(_fileCreateActionTypes, ctx);

    public IReadOnlyList<IQueryHandler> GetQueryHandlers(WorkContext ctx)
        => Instantiate<IQueryHandler>(_queryHandlerTypes, ctx);

    public IReadOnlyList<IFolderViewlet> GetFolderViewlets(WorkContext ctx)
        => Instantiate<IFolderViewlet>(_folderViewletTypes, ctx);

    public IReadOnlyList<IRibbonPinHandler> GetRibbonPinHandlers(WorkContext ctx)
        => Instantiate<IRibbonPinHandler>(_ribbonPinHandlerTypes, ctx);

    public IRibbonPinHandler? GetRibbonPinHandler(string contentKind, WorkContext ctx)
        => GetRibbonPinHandlers(ctx).FirstOrDefault(h => h.ContentKind == contentKind);

    private IReadOnlyList<T> Instantiate<T>(List<Type> types, WorkContext ctx)
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

    public Page? CreateTab(string pageKind, WorkContext workContext,
                           Dictionary<string, string>? pageParams = null)
    {
        if (!_registrationTypes.TryGetValue(pageKind, out var regType)) return null;
        var reg = Instantiate(regType, workContext) as IPageRegistration;
        if (reg is null) return null;
        var tab = reg.CreatePage(pageParams);
        if (tab is not null)
        {
            tab.PageKind   = pageKind;
            tab.PageParams = pageParams;
        }
        return tab;
    }

    public IReadOnlyList<string> GetPageKindsForConfig(Type configType, WorkContext ctx)
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

    // ── FindFileAction ────────────────────────────────────────────────────

    /// <summary>
    /// Finds an <see cref="IFileAction"/> by its concrete type's full or short name.
    /// Looks first in the cached set for <paramref name="workContext"/>; when that
    /// misses and <paramref name="reinitParams"/> is supplied, locates the type and
    /// invokes its <c>public static IFileAction Rehydrate(Dictionary&lt;string,string&gt;)</c>
    /// factory — the pattern runtime-constructed types (e.g. ShellVerbAction) use.
    /// </summary>
    public IFileAction? FindFileAction(string typeName, WorkContext workContext,
                                       Dictionary<string, string>? reinitParams = null)
    {
        var singleton = GetFileActions(workContext).FirstOrDefault(a =>
            a.GetType().FullName == typeName || a.GetType().Name == typeName);
        if (singleton is not null) return singleton;

        // FolderActionAdapter: non-cacheable wrapper whose inner type is stored in reinit params.
        // Reconstruct by finding the inner IFolderAction in the cached set and re-wrapping it.
        if ((typeName == typeof(FolderActionAdapter).FullName || typeName == nameof(FolderActionAdapter)) &&
            reinitParams?.TryGetValue("innerType", out var innerTypeName) == true &&
            innerTypeName is not null)
        {
            var inner = GetFolderActions(workContext).FirstOrDefault(a =>
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

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ConstructorInfo BestConstructor(Type t)
        => t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
}
