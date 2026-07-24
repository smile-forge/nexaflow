using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Filtering / matching layer over the action instances owned by
/// <see cref="FileSystemFeatureRegistry"/>. Purely stateless — discovery and
/// construction live in the registry; this class only decides which actions
/// apply to a given file/folder selection.
/// </summary>
public sealed class FileActionManager
{
    private readonly FileSystemFeatureRegistry _registry;

    public FileActionManager(FileSystemFeatureRegistry registry) => _registry = registry;

    private IReadOnlyList<IFileAction>   File   => _registry.FileActions;
    private IReadOnlyList<IFolderAction> Folder => _registry.FolderActions;

    /// <summary>
    /// All create-file actions offered by the "New" picker, aggregated live from every source:
    /// code-defined (discovered, e.g. Folder / Text File), HKCR ShellNew entries (when registry
    /// mapping is on), and user templates. Read each time the picker opens so an Options Save is
    /// reflected without rebuilding the tab.
    /// </summary>
    public IReadOnlyList<IFileCreateAction> GetCreateActions()
    {
        var list = new List<IFileCreateAction>(_registry.FileCreateActions);
        list.AddRange(ShellNewRegistry.Instance.BuildCreateActions());
        list.AddRange(TemplatedCreateRegistry.Instance.BuildCreateActions());
        return list;
    }

    /// <summary>
    /// All experience IDs advertised by the registered file actions.
    /// Passed to <see cref="FileMapManager.RegisterKnownExperiences"/> after construction.
    /// </summary>
    public IReadOnlyList<string> AllExperiences => _registry.AllExperiences;

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
        var file   = File;
        var folder = Folder;
        var fileArr   = new bool[file.Count];
        var folderArr = new bool[folder.Count];
        for (int i = 0; i < file.Count;   i++) fileArr[i]   = file[i].CanPerformAction;
        for (int i = 0; i < folder.Count; i++) folderArr[i] = folder[i].CanPerformAction;
        return (fileArr, folderArr);
    }

    /// <summary>
    /// Step 2 — safe to call on any thread.
    /// Applies file-action filtering rules using the pre-computed <paramref name="canPerform"/> array.
    /// </summary>
    /// <param name="includeOptional">
    /// When true (default — the action strip), experiences claimed only via
    /// <see cref="FileActions.CriteriaType.OptionalExtension"/> are offered. Pass false for the
    /// right-click menu so a secondary capability (e.g. "As Archive" on a <c>*.docx</c>) is hidden there.
    /// </param>
    public IReadOnlyList<IFileAction> FilterActions(
        IReadOnlyList<FileSystemEntry> selected,
        bool[]                         canPerform,
        bool                           includeOptional = true)
    {
        if (selected.Count == 0) return [];

        var file = File;
        bool anyDrives     = selected.Any(e => e.IsDrive);
        bool multipleFiles = selected.Count(e => !e.IsDirectory) > 1 || selected.Count > 1;

        var filtered = new List<IFileAction>();
        for (int i = 0; i < file.Count; i++)
        {
            if (!canPerform[i]) continue;
            if (FileMatches(file[i], selected, anyDrives, multipleFiles, includeOptional))
                filtered.Add(file[i]);
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
        bool[]                         canPerform,
        string?                        currentFolderPath = null)
    {
        var folder = Folder;
        bool emptySelection = selected.Count == 0;
        bool anyDrives      = selected.Any(e => e.IsDrive);
        bool multipleItems  = selected.Count > 1;

        var filtered = new List<IFileAction>();
        for (int i = 0; i < folder.Count; i++)
        {
            if (!canPerform[i]) continue;
            var action = folder[i];

            if (emptySelection && !action.AppliesToRoot) continue;
            if (!emptySelection && anyDrives && !action.AppliesToDrives) continue;
            if (multipleItems && !action.SupportsMultipleFiles) continue;

            if (!emptySelection)
            {
                if (!FolderNameMatches(action, selected)) continue;
                if (!ContentsMatch(action, selected)) continue;
                // Per-path gate (e.g. Unmount only on image-backed drives). Default is true, so unaffected
                // actions cost only one virtual call per selected item.
                if (!selected.All(e => action.AppliesToFolder(e.FullPath))) continue;
            }
            else if (IsConstrained(action))
            {
                // Root / current-folder case: honour the action's name & content constraints against
                // the open folder so a restricted action (e.g. the image Slideshow/Album, gated to
                // folders that are ≥30% images) only appears when that folder actually qualifies.
                // Without a folder path the constraint can't be verified, so the action is hidden.
                if (string.IsNullOrEmpty(currentFolderPath) || !FolderMatchesPath(action, currentFolderPath))
                    continue;
            }

            filtered.Add(new FolderActionAdapter(action));
        }
        return filtered;
    }

    private static bool IsConstrained(IFolderAction action) =>
        action.FolderNameGlob is not "*"
        || action.ContainsFileGlobs is not null
        || action.ContainsFolderGlobs is not null;

    private static bool FileMatches(
        IFileAction                    action,
        IReadOnlyList<FileSystemEntry> selected,
        bool                           anyDrives,
        bool                           multipleFiles,
        bool                           includeOptional)
    {
        if (multipleFiles && !action.SupportsMultipleFiles) return false;
        if (anyDrives) return false;  // drives use folder actions (IFolderAction.AppliesToDrives)

        // Lookup via FileMapManager reverse index
        var files = selected.Where(e => !e.IsDirectory).ToList();
        if (files.Count == 0) return false;

        foreach (var entry in files)
        {
            var experiences = FileMapManager.Instance.GetExperiencesForFile(new FileInfo(entry.FullPath), includeOptional);
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
        foreach (var entry in selected.Where(e => e.IsDirectory))
            if (!FolderContentsMatch(action, entry.FullPath)) return false;
        return true;
    }

    /// <summary>Name + contents check against a single folder path — the open-folder equivalent of
    /// <see cref="FolderNameMatches"/> + <see cref="ContentsMatch"/>.</summary>
    private static bool FolderMatchesPath(IFolderAction action, string folderPath)
    {
        var glob = action.FolderNameGlob;
        if (glob is not "*")
        {
            var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar,
                                                            Path.AltDirectorySeparatorChar));
            if (!GlobMatch(name, glob)) return false;
        }
        return FolderContentsMatch(action, folderPath);
    }

    private static bool FolderContentsMatch(IFolderAction action, string folderPath)
    {
        var fileGlobs   = action.ContainsFileGlobs;
        var folderGlobs = action.ContainsFolderGlobs;
        if (fileGlobs is null && folderGlobs is null) return true;

        int pct = System.Math.Clamp(action.MinimumFileGlobMatchPercentage, 0, 100);

        try
        {
            var (files, dirs) = TopLevelNames(folderPath);

            if (fileGlobs is not null && !FileGlobThresholdMet(files, fileGlobs, pct))
                return false;

            if (folderGlobs is not null && !dirs.Any(d => MatchesAnyGlob(d, folderGlobs)))
                return false;
        }
        catch { return false; }
        return true;
    }

    /// <summary>Top-level file and directory <b>names</b> of a folder — from the real filesystem, or through the
    /// VFS when the path is virtual (inside a mounted disk image or archive), so folder actions such as
    /// "As DICOM" are offered there too, not just on real folders.</summary>
    private static (IReadOnlyList<string> Files, IReadOnlyList<string> Dirs) TopLevelNames(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            var files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                                 .Select(Path.GetFileName).OfType<string>().ToList();
            var dirs = Directory.GetDirectories(folderPath, "*", SearchOption.TopDirectoryOnly)
                                .Select(p => Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                                .OfType<string>().Where(n => n.Length > 0).ToList();
            return (files, dirs);
        }

        var entries = VirtualFileSystem.Instance.EnumerateEntries(folderPath);
        return (entries.Where(e => !e.IsDirectory).Select(e => e.Name).ToList(),
                entries.Where(e => e.IsDirectory).Select(e => e.Name).ToList());
    }

    /// <summary>
    /// True when the folder's top-level files clear the glob threshold: at least one match when
    /// <paramref name="pct"/> is 0, otherwise at least <paramref name="pct"/>% of all files. The
    /// percentage path scans the file list once, returning early the moment the required count is
    /// reached or the unscanned remainder can no longer reach it.
    /// </summary>
    private static bool FileGlobThresholdMet(IReadOnlyList<string> fileNames, string[] globs, int pct)
    {
        if (pct <= 0)
            return fileNames.Any(n => MatchesAnyGlob(n, globs));

        int total = fileNames.Count;
        if (total == 0) return false;

        int required = (int)System.Math.Ceiling(total * pct / 100.0);
        int matched  = 0;
        for (int i = 0; i < total; i++)
        {
            if (MatchesAnyGlob(fileNames[i], globs))
            {
                if (++matched >= required) return true;                  // threshold reached
            }
            else if (matched + (total - 1 - i) < required)
            {
                return false;                                            // can no longer be reached
            }
        }
        return matched >= required;
    }

    private static bool MatchesAnyGlob(string name, string[] globs)
    {
        foreach (var pattern in globs)
            if (GlobMatch(name, pattern)) return true;
        return false;
    }

    private static bool GlobMatch(string name, string pattern)
    {
        if (pattern is "*" or "*.*") return true;
        if (pattern.StartsWith("*."))
            return name.EndsWith(pattern[1..], System.StringComparison.OrdinalIgnoreCase);
        return string.Equals(name, pattern, System.StringComparison.OrdinalIgnoreCase);
    }
}
