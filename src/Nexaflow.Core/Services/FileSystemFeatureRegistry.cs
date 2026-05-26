using Nexaflow.Core.FileActions;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Core.Services;

/// <summary>
/// Owns discovery, construction and caching of the file-system feature
/// contracts: <see cref="IFileAction"/>, <see cref="IFolderAction"/>,
/// <see cref="IFileCreateAction"/> and <see cref="IFolderViewlet"/>.
///
/// Deliberately decoupled from <c>WorkContext</c> and <c>FeatureManager</c> so
/// the file can move into <c>Nexaflow.Features.WindowsFileSystem</c> later
/// without dragging Core types along. All cross-assembly reflection is
/// delegated to <see cref="IShellServices.DiscoverImplementations{T}"/>.
/// </summary>
public sealed class FileSystemFeatureRegistry
{
    private static readonly Dictionary<IShellServices, FileSystemFeatureRegistry> _instances = new();
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
                _instances[shell] = r = new FileSystemFeatureRegistry(shell, ai, configs);
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

        _allExperiences = _fileActionTypes
            .Select(t => t.GetProperty("StaticExperienceId",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                ?.GetValue(null) as string)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    // ── Typed accessors ──────────────────────────────────────────────────

    public IReadOnlyList<IFileAction>       FileActions       => Materialize<IFileAction>(_fileActionTypes);
    public IReadOnlyList<IFolderAction>     FolderActions     => Materialize<IFolderAction>(_folderActionTypes);
    public IReadOnlyList<IFileCreateAction> FileCreateActions => Materialize<IFileCreateAction>(_fileCreateActionTypes);
    public IReadOnlyList<IFolderViewlet>    FolderViewlets    => Materialize<IFolderViewlet>(_folderViewletTypes);

    public IReadOnlyList<string> AllExperiences => _allExperiences;

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
