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
    // Per-assembly config instances — populated during RegisterFeatures, never changed after.
    private readonly Dictionary<Type, IFeatureConfig> _configs          = new();

    /// <summary>
    /// Read-only view of the per-assembly <see cref="IFeatureConfig"/> instances
    /// discovered during <see cref="RegisterFeatures"/>. Exposed for feature-scoped
    /// registries (e.g. <see cref="Services.FileSystemFeatureRegistry"/>) that need
    /// to resolve config types as constructor args without going through the shell.
    /// </summary>
    public IReadOnlyDictionary<Type, IFeatureConfig> Configs => _configs;

    private readonly List<Type> _keyboardHandlerTypes  = [];
    private readonly List<Type> _dropTargetTypes       = [];
    private readonly List<Type> _queryHandlerTypes     = [];
    private readonly List<Type> _ribbonPinHandlerTypes = [];

    // ── Read-only type lists (for callers that need the raw types) ────────

    public IReadOnlyList<Type> KeyboardHandlerTypes  => _keyboardHandlerTypes;
    public IReadOnlyList<Type> DropTargetTypes       => _dropTargetTypes;

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
            if (typeof(IRibbonPinHandler).IsAssignableFrom(t)) _ribbonPinHandlerTypes.Add(t);
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
                args[i] = (IReadOnlyDictionary<Type, IFeatureConfig>)_configs;
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
        var tab = reg.CreatePage(pageParams);
        if (tab is not null)
        {
            tab.PageKind   = pageKind;
            tab.PageParams = pageParams;
        }
        return tab;
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
}
