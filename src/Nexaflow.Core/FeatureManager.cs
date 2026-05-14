using Nexaflow.Features.Common;
using System.Collections.Generic;
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
    private readonly List<Type> _fileCreateActionTypes = [];
    private readonly List<Type> _keyboardHandlerTypes  = [];
    private readonly List<Type> _dropTargetTypes       = [];

    /// <summary>
    /// Types implementing <see cref="IFileAction"/> discovered from all registered
    /// feature assemblies. Consumed by <see cref="Services.FileActionManager"/> at
    /// construction time to instantiate cross-assembly file actions.
    /// </summary>
    public IReadOnlyList<Type> FileActionTypes       => _fileActionTypes;

    /// <summary>
    /// Types implementing <see cref="IFileCreateAction"/> discovered from all registered
    /// feature assemblies.
    /// </summary>
    public IReadOnlyList<Type> FileCreateActionTypes => _fileCreateActionTypes;

    /// <summary>
    /// Types implementing <see cref="IKeyboardHandler"/> discovered from all registered
    /// feature assemblies.
    /// </summary>
    public IReadOnlyList<Type> KeyboardHandlerTypes => _keyboardHandlerTypes;

    /// <summary>
    /// Types implementing <see cref="IDropTarget"/> discovered from all registered
    /// feature assemblies.
    /// </summary>
    public IReadOnlyList<Type> DropTargetTypes => _dropTargetTypes;

    // ── Registration ──────────────────────────────────────────────────────

    /// <summary>
    /// Scans the assembly of <paramref name="featureType"/> for all concrete
    /// <see cref="ITabRegistration"/> and <see cref="IFeatureConfig"/> types.
    /// Creates one <see cref="IFeatureConfig"/> instance per discovered type, registers it
    /// with <see cref="ConfigManager"/>, then instantiates each <see cref="ITabRegistration"/>
    /// by injecting the matching config into its constructor (DI-style).
    /// </summary>
    public void Register(Type featureType)
    {
        var asm = featureType.Assembly;

        // 1. Discover and instantiate all IFeatureConfig types in the assembly
        var configInstances = new Dictionary<Type, IFeatureConfig>();
        foreach (var t in asm.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface
                        && typeof(IFeatureConfig).IsAssignableFrom(t)))
        {
            var cfg = (IFeatureConfig)Activator.CreateInstance(t)!;
            ConfigManager.Instance.Register(cfg, cfg.ConfigName);
            configInstances[t] = cfg;
        }

        // 2. Discover all ITabRegistration concrete types
        var registrationTypes = asm.GetTypes()
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
                    // Instantiate temporarily to read PageKind
                    var args = ResolveArgs(ctor, configInstances, _tabOpener);
                    var tempReg = (ITabRegistration)ctor.Invoke(args);
                    pageKinds.Add(tempReg.PageKind);
                }
            }
            _configToPageKinds[configType] = pageKinds;
        }

        // 4. Instantiate all ITabRegistration types with injected configs and ITabOpener
        foreach (var regType in registrationTypes)
        {
            var ctor = BestConstructor(regType);
            var args = ResolveArgs(ctor, configInstances, _tabOpener);
            var reg  = (ITabRegistration)ctor.Invoke(args);
            _registrations[reg.PageKind] = reg;
        }

        // 5. Collect IFileAction / IFileCreateAction types for FileActionManager to instantiate.
        //    Also instantiate any IQueryHandler types found in the assembly and register globally.
        foreach (var t in asm.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface))
        {
            if (typeof(IFileAction).IsAssignableFrom(t))        _fileActionTypes.Add(t);
            if (typeof(IFileCreateAction).IsAssignableFrom(t))  _fileCreateActionTypes.Add(t);
            if (typeof(IKeyboardHandler).IsAssignableFrom(t))   _keyboardHandlerTypes.Add(t);
            if (typeof(IDropTarget).IsAssignableFrom(t))         _dropTargetTypes.Add(t);
            if (typeof(IQueryHandler).IsAssignableFrom(t))      _queryHandlers.Add((IQueryHandler)Activator.CreateInstance(t)!);
        }
    }

    private static ConstructorInfo BestConstructor(Type t)
        => t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

    private static object?[] ResolveArgs(
        ConstructorInfo ctor,
        Dictionary<Type, IFeatureConfig> configs,
        ITabOpener tabOpener)
        => ctor.GetParameters()
               .Select(p =>
                   typeof(ITabOpener).IsAssignableFrom(p.ParameterType)
                       ? (object?)tabOpener
                       : (object?)configs.GetValueOrDefault(p.ParameterType))
               .ToArray();

    // ITabOpener implementation injected into feature registrations
    private readonly ITabOpener _tabOpener;

    private FeatureManager()
    {
        _tabOpener = new FeatureTabOpener(this);
    }

    private sealed class FeatureTabOpener(FeatureManager manager) : ITabOpener
    {
        public void OpenTab(string pageKind, Dictionary<string, string>? pageParams = null)
            => manager.RequestTab(pageKind, pageParams);
    }

    /// <summary>
    /// Returns the page kinds associated with a config type, used by the Options panel
    /// to determine which tabs to close and reopen after a config change.
    /// </summary>
    public IReadOnlyList<string> GetPageKindsForConfig(Type configType)
        => _configToPageKinds.GetValueOrDefault(configType, []);

    // ── Tab creation ──────────────────────────────────────────────────────

    public bool IsRegistered(string pageKind) => _registrations.ContainsKey(pageKind);

    /// <summary>
    /// Creates a <see cref="TabEntry"/> for <paramref name="pageKind"/>,
    /// or returns <c>null</c> if no registration exists for that kind.
    /// Stamps <see cref="TabEntry.PageKind"/> and <see cref="TabEntry.PageParams"/>
    /// on the returned entry so the shell can refresh tabs after config changes.
    /// </summary>
    public TabEntry? CreateTab(string pageKind, Dictionary<string, string>? pageParams = null)
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

    // ── Query handlers ────────────────────────────────────────────────────

    private readonly List<IQueryHandler> _queryHandlers = [];

    /// <summary>
    /// Registers a global <see cref="IQueryHandler"/> that will be scored on every shell input.
    /// </summary>
    public void RegisterQueryHandler(IQueryHandler handler) => _queryHandlers.Add(handler);

    /// <summary>All globally registered query handlers.</summary>
    public IReadOnlyList<IQueryHandler> QueryHandlers => _queryHandlers.AsReadOnly();

    // ── Cross-feature tab requests ────────────────────────────────────────

    /// <summary>
    /// Raised when a feature wants the shell to open a tab for another page kind.
    /// The shell subscribes and calls <see cref="CreateTab"/> + OpenTab.
    /// </summary>
    public event Action<string, Dictionary<string, string>?>? TabOpenRequested;

    /// <summary>
    /// Called by feature code to ask the shell to open or focus a tab without
    /// holding a direct reference to the shell.
    /// </summary>
    public void RequestTab(string pageKind, Dictionary<string, string>? pageParams = null)
        => TabOpenRequested?.Invoke(pageKind, pageParams);
}
