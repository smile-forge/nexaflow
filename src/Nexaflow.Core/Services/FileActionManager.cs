using Nexaflow.Core.FileActions;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Core.Services;

/// <summary>
/// Filtering / matching layer over the action instances owned by <see cref="FeatureManager"/>.
/// Purely stateless — discovery and construction live in <see cref="FeatureManager"/>; this
/// class only decides which actions apply to a given file/folder selection.
/// </summary>
public sealed class FileActionManager
{
    private static IReadOnlyList<IFileAction>   File   => FeatureManager.Instance.FileActions;
    private static IReadOnlyList<IFolderAction> Folder => FeatureManager.Instance.FolderActions;

    /// <summary>All discovered create-file actions.</summary>
    public IReadOnlyList<IFileCreateAction> CreateActions => FeatureManager.Instance.FileCreateActions;

    /// <summary>
    /// All experience IDs advertised by the registered file actions.
    /// Passed to <see cref="FileMapManager.RegisterKnownExperiences"/> after construction.
    /// </summary>
    public IReadOnlyList<string> AllExperiences
        => File.Select(a => a.ExperienceId)
               .Where(id => !string.IsNullOrEmpty(id))
               .Distinct()
               .ToList();

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
        var file   = new bool[FileActionManager.File.Count];
        var folder = new bool[FileActionManager.Folder.Count];
        for (int i = 0; i < FileActionManager.File.Count; i++)
            file[i] = FileActionManager.File[i].CanPerformAction;
        for (int i = 0; i < FileActionManager.Folder.Count; i++)
            folder[i] = FileActionManager.Folder[i].CanPerformAction;
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
        for (int i = 0; i < File.Count; i++)
        {
            if (!canPerform[i]) continue;
            if (FileMatches(File[i], selected, anyDrives, multipleFiles))
                filtered.Add(File[i]);
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
        for (int i = 0; i < Folder.Count; i++)
        {
            if (!canPerform[i]) continue;
            var action = Folder[i];

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
            if (!experiences.Contains(action.ExperienceId, System.StringComparer.OrdinalIgnoreCase))
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
            return name.EndsWith(pattern[1..], System.StringComparison.OrdinalIgnoreCase);
        return string.Equals(name, pattern, System.StringComparison.OrdinalIgnoreCase);
    }
}
