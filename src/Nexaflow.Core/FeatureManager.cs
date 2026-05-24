using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Core;

/// <summary>
/// Singleton registry of all feature tab factories.
/// Call <see cref="Register(Type)"/> at startup with any type from the feature assembly;
/// the manager scans that assembly for <see cref="IPageRegistration"/> and
/// <see cref="IFeatureConfig"/> implementations, wires configs as constructor dependencies,
/// and registers everything automatically.
/// </summary>
public sealed class FeatureManager
{
    public static FeatureManager Instance { get; } = new();

    private readonly Dictionary<string, IPageRegistration> _registrations
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<Type, IReadOnlyList<string>> _configToPageKinds = new();

    private readonly List<Type> _fileActionTypes       = [];
    private readonly List<Type> _folderActionTypes     = [];
    private readonly List<Type> _fileCreateActionTypes = [];
    private readonly List<Type> _keyboardHandlerTypes  = [];
    private readonly List<Type> _dropTargetTypes       = [];
    private readonly List<IRibbonPinHandler> _ribbonPinHandlers = [];

    // Singleton instances of file/folder/create actions. Built during Register;
    // these are the authoritative in-memory set used by both the FileSystemView
    // and the ribbon FileAction handler. Constructor args are resolved through
    // ResolveArgs (global IShellServices + registered singleton services).
    private readonly List<IFileAction>       _fileActions       = [];
    private readonly List<IFolderAction>     _folderActions     = [];
    private readonly List<IFileCreateAction> _fileCreateActions = [];

    public IReadOnlyList<Type> FileActionTypes       => _fileActionTypes;
    public IReadOnlyList<Type> FolderActionTypes     => _folderActionTypes;
    public IReadOnlyList<Type> FileCreateActionTypes => _fileCreateActionTypes;
    public IReadOnlyList<Type> KeyboardHandlerTypes  => _keyboardHandlerTypes;
    public IReadOnlyList<Type> DropTargetTypes       => _dropTargetTypes;

    public IReadOnlyList<IFileAction>       FileActions       => _fileActions;
    public IReadOnlyList<IFolderAction>     FolderActions     => _folderActions;
    public IReadOnlyList<IFileCreateAction> FileCreateActions => _fileCreateActions;

    /// <summary>
    /// Finds a registered <see cref="IFileAction"/> instance by its concrete type's full or short name.
    /// </summary>
    public IFileAction? FindFileAction(string typeName)
        => _fileActions.FirstOrDefault(a =>
               a.GetType().FullName == typeName || a.GetType().Name == typeName);

    public IReadOnlyList<IRibbonPinHandler> RibbonPinHandlers => _ribbonPinHandlers.AsReadOnly();

    public void RegisterRibbonPinHandler(IRibbonPinHandler handler)
        => _ribbonPinHandlers.Add(handler);

    public IRibbonPinHandler? GetRibbonPinHandler(string? contentKind)
        => contentKind is null ? null
           : _ribbonPinHandlers.FirstOrDefault(h => h.ContentKind == contentKind);

    // ── Shell services ────────────────────────────────────────────────────

    /// <summary>
    /// The application-level shell services singleton.
    /// Set by <see cref="App"/> before feature registration so that injected
    /// <see cref="IShellServices"/> constructor parameters are resolved.
    /// </summary>
    public IShellServices? ShellServices { get; private set; }

    public void SetShellServices(IShellServices shellServices)
        => ShellServices = shellServices;

    // ── Registration ──────────────────────────────────────────────────────


    public void RegisterFeatures()
    {
        string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var featureDlls = Directory.GetFiles(exeDir, "Nexaflow.Features.*.dll");

        foreach (var dll in featureDlls)
        {
            // Load the assembly if not already loaded
            var asmName = AssemblyName.GetAssemblyName(dll);
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == asmName.Name))
            {
                Assembly.LoadFrom(dll);
            }
        }

        // Now scan all loaded assemblies for features
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name!.StartsWith("Nexaflow.Features."))
            {
                Register(asm);
            }
        }

        // Core itself contains IFileAction / IFolderAction / IFileCreateAction
        // implementations (Copy, Cut, Delete, Open, Rename, ...). Scan them so
        // the in-memory action set is the union of Core + every feature.
        DiscoverActions(typeof(FeatureManager).Assembly, new Dictionary<Type, IFeatureConfig>());
    }

    public void Register(Assembly asm)
    {
        var types = asm.GetTypes();

        // 1. Discover and instantiate all IFeatureConfig types
        var configInstances = new Dictionary<Type, IFeatureConfig>();
        foreach (var t in types
            .Where(t => !t.IsAbstract && !t.IsInterface
                        && typeof(IFeatureConfig).IsAssignableFrom(t)))
        {
            var cfg = (IFeatureConfig)Activator.CreateInstance(t)!;
            ConfigManager.Instance.Register(cfg, cfg.ConfigName);
            configInstances[t] = cfg;
        }

        // 2. Discover all ITabRegistration concrete types
        var registrationTypes = types
            .Where(t => !t.IsAbstract && !t.IsInterface
                        && typeof(IPageRegistration).IsAssignableFrom(t))
            .ToList();

        // 3. Build config-type → page-kinds mapping (for tab refresh after Options save)
        foreach (var configType in configInstances.Keys)
        {
            var pageKinds = new List<string>();
            foreach (var regType in registrationTypes)
            {
                var ctor = BestConstructor(regType);
                if (ctor.GetParameters().Any(p => p.ParameterType == configType))
                {
                    var args   = ResolveArgs(ctor, configInstances);
                    var tempReg = (IPageRegistration)ctor.Invoke(args);
                    pageKinds.Add(tempReg.PageKind);
                }
            }
            _configToPageKinds[configType] = pageKinds;
        }

        // 4. Instantiate all ITabRegistration types with injected configs and IShellServices
        foreach (var regType in registrationTypes)
        {
            var ctor = BestConstructor(regType);
            var args = ResolveArgs(ctor, configInstances);
            var reg  = (IPageRegistration)ctor.Invoke(args);
            _registrations[reg.PageKind] = reg;
        }

        // 5. Collect handler types + instantiate handlers and actions.
        foreach (var t in types.Where(t => !t.IsAbstract && !t.IsInterface))
        {
            if (typeof(IKeyboardHandler).IsAssignableFrom(t)) _keyboardHandlerTypes.Add(t);
            if (typeof(IDropTarget).IsAssignableFrom(t))      _dropTargetTypes.Add(t);
            if (typeof(IQueryHandler).IsAssignableFrom(t))
                TryInstantiate(t, configInstances, _queryHandlers);
            if (typeof(IFolderViewlet).IsAssignableFrom(t))
                TryInstantiate(t, configInstances, _folderViewlets);
            if (typeof(IRibbonPinHandler).IsAssignableFrom(t))
                TryInstantiate(t, configInstances, _ribbonPinHandlers);
        }

        DiscoverActions(asm, configInstances);
    }

    /// <summary>
    /// Scans <paramref name="asm"/> for <see cref="IFileAction"/>, <see cref="IFolderAction"/>,
    /// and <see cref="IFileCreateAction"/> implementations, recording their types and
    /// instantiating each one once via <see cref="TryInstantiate"/>. Types whose ctor
    /// args can't be resolved are silently skipped — that excludes wrappers like
    /// <c>FolderActionAdapter</c> (needs an <c>IFolderAction</c>) and runtime types like
    /// <c>ShellVerbAction</c> (needs strings), both of which are constructed elsewhere.
    /// </summary>
    private void DiscoverActions(Assembly asm, Dictionary<Type, IFeatureConfig> configs)
    {
        foreach (var t in asm.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface))
        {
            bool isFile   = typeof(IFileAction).IsAssignableFrom(t);
            bool isFolder = typeof(IFolderAction).IsAssignableFrom(t);
            bool isCreate = typeof(IFileCreateAction).IsAssignableFrom(t);
            if (!isFile && !isFolder && !isCreate) continue;

            var instance = TryInstantiate(t, configs);
            if (instance is null) continue;

            if (isFile)   { _fileActionTypes.Add(t);       _fileActions.Add((IFileAction)instance); }
            if (isFolder) { _folderActionTypes.Add(t);     _folderActions.Add((IFolderAction)instance); }
            if (isCreate) { _fileCreateActionTypes.Add(t); _fileCreateActions.Add((IFileCreateAction)instance); }
        }
    }

    private void TryInstantiate<T>(Type t, Dictionary<Type, IFeatureConfig> configs, List<T> sink)
    {
        if (TryInstantiate(t, configs) is T instance) sink.Add(instance);
    }

    /// <summary>
    /// Tries each public constructor longest-first; uses the first whose parameters
    /// can ALL be resolved from registered singleton services, configs, or
    /// <see cref="ShellServices"/>. Returns null when no constructor is fully
    /// satisfiable — matches the old FileActionManager.TryCreate semantics so
    /// types whose deps aren't available are skipped rather than created in a
    /// broken null state.
    /// </summary>
    private object? TryInstantiate(Type t, Dictionary<Type, IFeatureConfig> configs)
    {
        foreach (var ctor in t.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var args = TryResolveArgs(ctor, configs);
            if (args is null) continue;
            try { return ctor.Invoke(args); }
            catch { /* ctor itself threw — try a shorter overload */ }
        }
        return null;
    }

    private object?[]? TryResolveArgs(ConstructorInfo ctor, Dictionary<Type, IFeatureConfig> configs)
    {
        var parms = ctor.GetParameters();
        var args  = new object?[parms.Length];
        for (int i = 0; i < parms.Length; i++)
        {
            var pt = parms[i].ParameterType;
            if (typeof(IShellServices).IsAssignableFrom(pt))
            {
                if (ShellServices is null) return null;
                args[i] = ShellServices;
            }
            else if (_singletonServices.TryGetValue(pt, out var svc)) args[i] = svc;
            else if (configs.TryGetValue(pt, out var cfg))            args[i] = cfg;
            else if (parms[i].IsOptional)                             args[i] = Type.Missing;
            else                                                      return null;
        }
        return args;
    }

    private static ConstructorInfo BestConstructor(Type t)
        => t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

    private object?[] ResolveArgs(ConstructorInfo ctor, Dictionary<Type, IFeatureConfig> configs)
        => ctor.GetParameters()
               .Select(p =>
                   typeof(IShellServices).IsAssignableFrom(p.ParameterType)
                       ? (object?)ShellServices
                       : _singletonServices.TryGetValue(p.ParameterType, out var svc)
                           ? svc
                           : (object?)configs.GetValueOrDefault(p.ParameterType))
               .ToArray();

    public IReadOnlyList<string> GetPageKindsForConfig(Type configType)
        => _configToPageKinds.GetValueOrDefault(configType, []);

    // ── Tab creation ──────────────────────────────────────────────────────

    public bool IsRegistered(string pageKind) => _registrations.ContainsKey(pageKind);

    public Page? CreateTab(string pageKind, Dictionary<string, string>? pageParams = null)
    {
        if (!_registrations.TryGetValue(pageKind, out var reg)) return null;
        var tab = reg.CreatePage(pageParams);
        if (tab is not null)
        {
            tab.PageKind   = pageKind;
            tab.PageParams = pageParams;
        }
        return tab;
    }

    // ── Folder viewlets ───────────────────────────────────────────────────

    private readonly List<IFolderViewlet> _folderViewlets = [];
    public IReadOnlyList<IFolderViewlet> FolderViewlets => _folderViewlets.AsReadOnly();

    // ── Query handlers ────────────────────────────────────────────────────

    private readonly List<IQueryHandler> _queryHandlers = [];

    public void RegisterQueryHandler(IQueryHandler handler) => _queryHandlers.Add(handler);

    public IReadOnlyList<IQueryHandler> QueryHandlers => _queryHandlers.AsReadOnly();

    // ── Singleton services ────────────────────────────────────────────────

    private readonly Dictionary<Type, object> _singletonServices = new();

    public void RegisterSingletonService(Type interfaceType, object instance)
        => _singletonServices[interfaceType] = instance;
}
