using Nexaflow.Core.FileActions;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Nexaflow.Core.Services;

/// <summary>
/// Discovers every <see cref="IFileAction"/> and <see cref="IFolderAction"/>
/// implementation — both in the executing (Core) assembly and in any feature assembly
/// registered with <see cref="FeatureManager"/> — and instantiates each one once
/// (singletons) using basic constructor injection.
/// File actions are matched against files via <see cref="FileMapManager"/>.
/// Folder actions are matched structurally via glob and containment rules.
/// </summary>
public sealed class FileActionManager
{
    private readonly IReadOnlyList<IFileAction>       _all;
    private readonly IReadOnlyList<IFolderAction>     _allFolderActions;
    private readonly IReadOnlyList<IFileCreateAction> _allCreateActions;
    private readonly FileMapManager                   _fileMapManager;

    /// <summary>All discovered create-file actions.</summary>
    public IReadOnlyList<IFileCreateAction> CreateActions => _allCreateActions;

    /// <summary>
    /// All experience IDs advertised by the registered file actions.
    /// Passed to <see cref="FileMapManager.RegisterKnownExperiences"/> after construction.
    /// </summary>
    public IReadOnlyList<string> AllExperiences
        => _all.Select(a => a.ExperienceId)
               .Where(id => !string.IsNullOrEmpty(id))
               .Distinct()
               .ToList();

    /// <param name="services">
    /// Map of service type → singleton instance that can be injected into action
    /// constructors. Should include <see cref="FileMapManager"/>.
    /// </param>
    public FileActionManager(IReadOnlyDictionary<Type, object>? services = null)
    {
        var svc = services ?? new Dictionary<Type, object>();
        _fileMapManager   = svc.TryGetValue(typeof(FileMapManager), out var fm)
            ? (FileMapManager)fm
            : FileMapManager.Instance;
        _all              = Discover(svc);
        _allFolderActions = DiscoverFolderActions(svc);
        _allCreateActions = DiscoverCreate(svc);
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<IFileAction> Discover(IReadOnlyDictionary<Type, object> services)
    {
        var result = new List<IFileAction>();
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            TryAddAction(type, services, result);
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

    private static IReadOnlyList<IFolderAction> DiscoverFolderActions(IReadOnlyDictionary<Type, object> services)
    {
        var result = new List<IFolderAction>();
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            TryAddFolderAction(type, services, result);
        foreach (var type in FeatureManager.Instance.FolderActionTypes)
            TryAddFolderAction(type, services, result);
        return result;
    }

    private static void TryAddFolderAction(Type type, IReadOnlyDictionary<Type, object> services, List<IFolderAction> result)
    {
        if (type.IsAbstract || type.IsInterface) return;
        if (!typeof(IFolderAction).IsAssignableFrom(type)) return;
        // Dual-interface types (IFileAction + IFolderAction) are already in _all.
        // We only add the IFolderAction interface here — the adapter is created at filter time.
        if (TryCreate(type, services) is IFolderAction action)
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

    // ── Lookup ───────────────────────────────────────────────────────────────

    /// <summary>Finds an action by its concrete type's full or short name.</summary>
    public IFileAction? FindByTypeName(string name)
        => _all.FirstOrDefault(a => a.GetType().FullName == name || a.GetType().Name == name);

    // ── Filtering ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the subset of file actions that are applicable to <paramref name="selected"/>.
    /// </summary>
    public IReadOnlyList<IFileAction> GetActionsFor(IReadOnlyList<FileSystemEntry> selected)
    {
        var canPerform = SnapshotCanPerform();
        return FilterActions(selected, canPerform.File);
    }

    /// <summary>
    /// Step 1 — must be called on the STA UI thread (OLE clipboard access).
    /// Snapshots <see cref="IFileAction.CanPerformAction"/> and
    /// <see cref="IFolderAction.CanPerformAction"/> for every registered action.
    /// </summary>
    public (bool[] File, bool[] Folder) SnapshotCanPerform()
    {
        var file   = new bool[_all.Count];
        var folder = new bool[_allFolderActions.Count];
        for (int i = 0; i < _all.Count; i++)
            file[i] = _all[i].CanPerformAction;
        for (int i = 0; i < _allFolderActions.Count; i++)
            folder[i] = _allFolderActions[i].CanPerformAction;
        return (file, folder);
    }

    /// <summary>
    /// Step 2 — safe to call on any thread.
    /// Applies file-action filtering rules using the pre-computed <paramref name="canPerform"/> array.
    /// </summary>
    public IReadOnlyList<IFileAction> FilterActions(
        IReadOnlyList<FileSystemEntry> selected,
        bool[]                         canPerform)
    {
        if (selected.Count == 0) return [];

        bool anyDrives     = selected.Any(e => e.IsDrive);
        bool multipleFiles = selected.Count(e => !e.IsDirectory) > 1 || selected.Count > 1;

        var filtered = new List<IFileAction>();
        for (int i = 0; i < _all.Count; i++)
        {
            if (!canPerform[i]) continue;
            if (FileMatches(_all[i], selected, anyDrives, multipleFiles))
                filtered.Add(_all[i]);
        }
        return filtered;
    }

    /// <summary>
    /// Filters folder actions for a folder-only selection. Safe to call on any thread.
    /// Returns <see cref="FolderActionAdapter"/> wrappers so callers can treat the
    /// result uniformly as <see cref="IFileAction"/>.
    /// </summary>
    public IReadOnlyList<IFileAction> FilterFolderActions(
        IReadOnlyList<FileSystemEntry> selected,
        bool[]                         canPerform)
    {
        bool emptySelection = selected.Count == 0;
        bool anyDrives      = selected.Any(e => e.IsDrive);
        bool multipleItems  = selected.Count > 1;

        var filtered = new List<IFileAction>();
        for (int i = 0; i < _allFolderActions.Count; i++)
        {
            if (!canPerform[i]) continue;
            var action = _allFolderActions[i];

            if (emptySelection && !action.AppliesToRoot) continue;
            if (!emptySelection && anyDrives && !action.AppliesToDrives) continue;
            if (multipleItems && !action.SupportsMultipleFiles) continue;

            if (!emptySelection && !FolderNameMatches(action, selected)) continue;
            if (!emptySelection && !ContentsMatch(action, selected)) continue;

            filtered.Add(new FolderActionAdapter(action));
        }
        return filtered;
    }

    private static bool FileMatches(
        IFileAction                    action,
        IReadOnlyList<FileSystemEntry> selected,
        bool                           anyDrives,
        bool                           multipleFiles)
    {
        if (multipleFiles && !action.SupportsMultipleFiles) return false;
        if (anyDrives) return false;  // drives use folder actions (IFolderAction.AppliesToDrives)

        // Lookup via FileMapManager reverse index
        var files = selected.Where(e => !e.IsDirectory).ToList();
        if (files.Count == 0) return false;

        foreach (var entry in files)
        {
            var experiences = FileMapManager.Instance.GetExperiencesForFile(new FileInfo(entry.FullPath));
            if (!experiences.Contains(action.ExperienceId, StringComparer.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static bool FolderNameMatches(IFolderAction action, IReadOnlyList<FileSystemEntry> selected)
    {
        var glob = action.FolderNameGlob;
        if (glob is "*") return true;
        return selected.Where(e => e.IsDirectory)
                       .All(e => GlobMatch(e.Name, glob));
    }

    private static bool ContentsMatch(IFolderAction action, IReadOnlyList<FileSystemEntry> selected)
    {
        var fileGlobs   = action.ContainsFileGlobs;
        var folderGlobs = action.ContainsFolderGlobs;
        if (fileGlobs is null && folderGlobs is null) return true;

        foreach (var entry in selected.Where(e => e.IsDirectory))
        {
            try
            {
                if (fileGlobs is not null)
                {
                    bool found = false;
                    foreach (var pattern in fileGlobs)
                        if (Directory.EnumerateFiles(entry.FullPath, pattern, SearchOption.TopDirectoryOnly).Any())
                        { found = true; break; }
                    if (!found) return false;
                }
                if (folderGlobs is not null)
                {
                    bool found = false;
                    foreach (var pattern in folderGlobs)
                        if (Directory.EnumerateDirectories(entry.FullPath, pattern, SearchOption.TopDirectoryOnly).Any())
                        { found = true; break; }
                    if (!found) return false;
                }
            }
            catch { return false; }
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
}
