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
/// the manager scans that assembly for <see cref="ITabRegistration"/> and
/// <see cref="IFeatureConfig"/> implementations, wires configs as constructor dependencies,
/// and registers everything automatically.
/// </summary>
public sealed class FeatureManager
{
    public static FeatureManager Instance { get; } = new();

    private readonly Dictionary<string, ITabRegistration> _registrations
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<Type, IReadOnlyList<string>> _configToPageKinds = new();

    private readonly List<Type> _fileActionTypes       = [];
    private readonly List<Type> _folderActionTypes     = [];
    private readonly List<Type> _fileCreateActionTypes = [];
    private readonly List<Type> _keyboardHandlerTypes  = [];
    private readonly List<Type> _dropTargetTypes       = [];

    public IReadOnlyList<Type> FileActionTypes       => _fileActionTypes;
    public IReadOnlyList<Type> FolderActionTypes     => _folderActionTypes;
    public IReadOnlyList<Type> FileCreateActionTypes => _fileCreateActionTypes;
    public IReadOnlyList<Type> KeyboardHandlerTypes  => _keyboardHandlerTypes;
    public IReadOnlyList<Type> DropTargetTypes       => _dropTargetTypes;

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
                        && typeof(ITabRegistration).IsAssignableFrom(t))
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
                    var tempReg = (ITabRegistration)ctor.Invoke(args);
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
            var reg  = (ITabRegistration)ctor.Invoke(args);
            _registrations[reg.PageKind] = reg;
        }

        // 5. Collect action/handler types; instantiate IQueryHandler types globally
        foreach (var t in types.Where(t => !t.IsAbstract && !t.IsInterface))
        {
            if (typeof(IFileAction).IsAssignableFrom(t))        _fileActionTypes.Add(t);
            if (typeof(IFolderAction).IsAssignableFrom(t))      _folderActionTypes.Add(t);
            if (typeof(IFileCreateAction).IsAssignableFrom(t))  _fileCreateActionTypes.Add(t);
            if (typeof(IKeyboardHandler).IsAssignableFrom(t))   _keyboardHandlerTypes.Add(t);
            if (typeof(IDropTarget).IsAssignableFrom(t))        _dropTargetTypes.Add(t);
            if (typeof(IQueryHandler).IsAssignableFrom(t))
            {
                try
                {
                    var ctor = BestConstructor(t);
                    var args = ResolveArgs(ctor, configInstances);
                    _queryHandlers.Add((IQueryHandler)ctor.Invoke(args));
                }
                catch { /* skip — requires unresolvable constructor args */ }
            }
            if (typeof(IFolderViewlet).IsAssignableFrom(t))
            {
                try
                {
                    var ctor = BestConstructor(t);
                    var args = ResolveArgs(ctor, configInstances);
                    _folderViewlets.Add((IFolderViewlet)ctor.Invoke(args));
                }
                catch { /* skip — requires unresolvable constructor args */ }
            }
        }
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
        var tab = reg.CreateTab(pageParams);
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
