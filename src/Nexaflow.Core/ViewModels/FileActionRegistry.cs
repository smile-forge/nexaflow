using Nexaflow.Features.WinFileSystem.FileActions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// Discovers every <see cref="IFileAction"/> implementation in the executing assembly
/// and instantiates each one once (singletons) using basic constructor injection.
/// The registry is built once and cached; use <see cref="GetActionsFor"/> to obtain
/// a filtered list appropriate for the current selection context.
/// </summary>
public sealed class FileActionRegistry
{
    private readonly IReadOnlyList<IFileAction> _all;

    /// <param name="services">
    /// Map of service type → singleton instance that can be injected into action
    /// constructors. Pass an empty dictionary if no extra services are needed.
    /// </param>
    public FileActionRegistry(IReadOnlyDictionary<Type, object>? services = null)
    {
        _all = Discover(services ?? new Dictionary<Type, object>());
    }

    // ── Discovery ────────────────────────────────────────────────────────────

    private static IReadOnlyList<IFileAction> Discover(IReadOnlyDictionary<Type, object> services)
    {
        var result = new List<IFileAction>();
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IFileAction).IsAssignableFrom(type)) continue;
            if (TryCreate(type, services) is IFileAction action)
                result.Add(action);
        }
        return result;
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
        // Snapshot CanPerformAction on the calling (STA) thread — these may call
        // OLE clipboard APIs that cannot run on an MTA thread-pool thread.
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

        // Actions that only apply to the root (e.g. Paste) should not appear
        // when individual files are selected — only when nothing is selected or
        // only folders are selected and the action also supports folders.
        if (action.AppliesToRoot && !action.AppliesToFolders)
            return false;   // pure root-only, no individual selection support
        if (action.AppliesToRoot && !onlyFolders)
            return false;   // e.g. Paste: hide when files are selected

        if (onlyFolders)
        {
            if (!action.AppliesToFolders) return false;
            if (action.SupportedFolderNames != "*")
                return selected.All(e => GlobMatch(e.Name, action.SupportedFolderNames));
            return true;
        }

        // At least one file in the selection
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

        // Exact match or wildcard subtype: "text/*" matches "text/plain"
        if (supported.EndsWith("/*"))
        {
            var prefix = supported[..^2];
            return contentType.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(supported, contentType, StringComparison.OrdinalIgnoreCase);
    }
}
