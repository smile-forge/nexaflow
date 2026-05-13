using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Core.Services;

/// <summary>
/// Discovers every <see cref="IFileAction"/> and <see cref="IFileCreateAction"/>
/// implementation — both in the executing (Core) assembly and in any feature assembly
/// registered with <see cref="FeatureManager"/> — and instantiates each one once
/// (singletons) using basic constructor injection.
/// Use <see cref="GetActionsFor"/> to obtain a filtered list appropriate for the
/// current selection context.
/// </summary>
public sealed class FileActionManager
{
    private readonly IReadOnlyList<IFileAction>       _all;
    private readonly IReadOnlyList<IFileCreateAction> _allCreateActions;

    /// <summary>All discovered create-file actions.</summary>
    public IReadOnlyList<IFileCreateAction> CreateActions => _allCreateActions;

    /// <param name="services">
    /// Map of service type → singleton instance that can be injected into action
    /// constructors. Pass an empty dictionary if no extra services are needed.
    /// </param>
    public FileActionManager(IReadOnlyDictionary<Type, object>? services = null)
    {
        var svc = services ?? new Dictionary<Type, object>();
        _all              = Discover(svc);
        _allCreateActions = DiscoverCreate(svc);
    }

    // ── Discovery ────────────────────────────────────────────────────────────

    private static IReadOnlyList<IFileAction> Discover(IReadOnlyDictionary<Type, object> services)
    {
        var result = new List<IFileAction>();

        // Core assembly types
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            TryAddAction(type, services, result);

        // Feature assembly types collected by FeatureManager during Register()
        foreach (var type in FeatureManager.Instance.FileActionTypes)
            TryAddAction(type, services, result);

        return result;
    }

    private static void TryAddAction(Type type, IReadOnlyDictionary<Type, object> services, List<IFileAction> result)
    {
        if (type.IsAbstract || type.IsInterface) return;
        if (!typeof(IFileAction).IsAssignableFrom(type)) return;
        if (TryCreate(type, services) is IFileAction action)
            result.Add(action);
    }

    private static IReadOnlyList<IFileCreateAction> DiscoverCreate(IReadOnlyDictionary<Type, object> services)
    {
        var result = new List<IFileCreateAction>();

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            TryAddCreateAction(type, services, result);

        foreach (var type in FeatureManager.Instance.FileCreateActionTypes)
            TryAddCreateAction(type, services, result);

        return result;
    }

    private static void TryAddCreateAction(Type type, IReadOnlyDictionary<Type, object> services, List<IFileCreateAction> result)
    {
        if (type.IsAbstract || type.IsInterface) return;
        if (!typeof(IFileCreateAction).IsAssignableFrom(type)) return;
        if (TryCreate(type, services) is IFileCreateAction action)
            result.Add(action);
    }

    /// <summary>
    /// Attempts to instantiate <paramref name="type"/> by finding the constructor
    /// whose parameters can all be satisfied from <paramref name="services"/>.
    /// Constructors are tried longest-first so the richest available overload wins.
    /// </summary>
    private static object? TryCreate(Type type, IReadOnlyDictionary<Type, object> services)
    {
        foreach (var ctor in type.GetConstructors()
                                  .OrderByDescending(c => c.GetParameters().Length))
        {
            var parms = ctor.GetParameters();
            var args  = new object?[parms.Length];
            bool ok   = true;

            for (int i = 0; i < parms.Length; i++)
            {
                if (services.TryGetValue(parms[i].ParameterType, out var svc))
                    args[i] = svc;
                else if (parms[i].IsOptional)
                    args[i] = Type.Missing;
                else { ok = false; break; }
            }

            if (ok) return ctor.Invoke(args);
        }
        return null;
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the subset of actions that are applicable to <paramref name="selected"/>.
    /// <list type="bullet">
    ///   <item>Empty selection → folder-applicable actions only.</item>
    ///   <item>Only folders selected → folder-applicable actions, filtered by SupportedFolderNames.</item>
    ///   <item>Multiple files → only actions that advertise SupportsMultipleFiles.</item>
    ///   <item>File type filtering via SupportedFileTypes glob patterns (;-separated).</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<IFileAction> GetActionsFor(IReadOnlyList<FileSystemEntry> selected)
    {
        var canPerform = SnapshotCanPerform();
        return FilterActions(selected, canPerform);
    }

    /// <summary>
    /// Step 1 — must be called on the STA UI thread.
    /// Snapshots <see cref="IFileAction.CanPerformAction"/> for every registered action.
    /// The returned array is indexed to match the internal <c>_all</c> list.
    /// </summary>
    public bool[] SnapshotCanPerform()
    {
        var snapshot = new bool[_all.Count];
        for (int i = 0; i < _all.Count; i++)
            snapshot[i] = _all[i].CanPerformAction;
        return snapshot;
    }

    /// <summary>
    /// Step 2 — safe to call on any thread.
    /// Applies all filtering rules using the pre-computed <paramref name="canPerform"/> snapshot.
    /// </summary>
    public IReadOnlyList<IFileAction> FilterActions(
        IReadOnlyList<FileSystemEntry> selected,
        bool[]                         canPerform)
    {
        if (selected.Count == 0)
        {
            var result = new List<IFileAction>();
            for (int i = 0; i < _all.Count; i++)
                if (canPerform[i] && _all[i].AppliesToFolders && _all[i].AppliesToRoot)
                    result.Add(_all[i]);
            return result;
        }

        bool onlyFolders   = selected.All(e => e.IsDirectory);
        bool anyDrives     = selected.Any(e => e.IsDrive);
        bool multipleFiles = !onlyFolders && (selected.Count(e => !e.IsDirectory) > 1
                                           || selected.Count > 1);

        var filtered = new List<IFileAction>();
        for (int i = 0; i < _all.Count; i++)
            if (canPerform[i] && Matches(_all[i], selected, onlyFolders, anyDrives, multipleFiles))
                filtered.Add(_all[i]);
        return filtered;
    }

    private static bool Matches(
        IFileAction                    action,
        IReadOnlyList<FileSystemEntry> selected,
        bool                           onlyFolders,
        bool                           anyDrives,
        bool                           multipleFiles)
    {
        if (multipleFiles && !action.SupportsMultipleFiles)
            return false;

        if (anyDrives && !action.AppliesToDrives)
            return false;

        if (action.AppliesToRoot && !action.AppliesToFolders)
            return false;
        if (action.AppliesToRoot && !onlyFolders)
            return false;

        if (onlyFolders)
        {
            if (!action.AppliesToFolders) return false;
            if (action.SupportedFolderNames != "*")
                return selected.All(e => GlobMatch(e.Name, action.SupportedFolderNames));
            return true;
        }

        if (action.SupportedFileTypes != "*.*")
        {
            var patterns = action.SupportedFileTypes
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return selected
                .Where(e => !e.IsDirectory)
                .All(e => patterns.Any(p => GlobMatch(e.Name, p)));
        }
        return true;
    }

    private static bool GlobMatch(string name, string pattern)
    {
        if (pattern is "*" or "*.*") return true;
        if (pattern.StartsWith("*."))
            return name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if <paramref name="action"/> is compatible with
    /// <paramref name="contentType"/>.
    /// </summary>
    public static bool ContentTypeMatches(IFileAction action, string contentType)
    {
        var supported = action.SupportedContentTypes;
        if (supported is "*" or "") return true;
        if (string.IsNullOrEmpty(contentType)) return true;

        if (supported.EndsWith("/*"))
        {
            var prefix = supported[..^2];
            return contentType.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(supported, contentType, StringComparison.OrdinalIgnoreCase);
    }
}
