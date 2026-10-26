using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Visuals.Common.Localization;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Handles path-navigation queries in a FileSystem tab.
/// Accepts: absolute paths (when root is This PC or same drive), a location's address (<c>sftp://adam@nas/home</c>) or a
/// path the file system names (<c>::docs\…</c>), paths relative to the current folder or the root, and
/// environment-variable paths (%AppData%, etc.). Rejects anything containing wildcards — those belong to the search
/// handlers. Scoring asks nothing of the disk: it runs on every keystroke, and on a location every question is a round
/// trip. Whether the folder is there is asked once, when the query runs.
/// Auto-discovered by FeatureManager because it lives in a Nexaflow.Features.* assembly.
/// </summary>
public sealed class FileSystemQueryHandler : IQueryHandler
{
    public string Symbol => ">";

    public string Description =>
        "Navigates the file browser to a directory. " +
        "Use when the user types a full path (C:\\data), a relative path, or an environment variable path (%AppData%).";

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not FileSystemViewModel fs) return 0f;

        var trimmed = input.Trim();
        if (trimmed.Length == 0) return 0f;

        // An explicit ">" is the user forcing navigation: claim it without gating on wildcards, drive match
        // or a listing — those tests exist to yield to the search handlers on *un-prefixed* input, and
        // ProcessAsync reports a bad path far better than handing the text to the agent.
        if (prefixed) return 1f;

        if (trimmed.Contains('*') || trimmed.Contains('?')) return 0f;

        // A location's address, or a path only the file system names, cannot be anything else.
        if (IsFileSystemPath(trimmed) || fs.Vfs.TryParseDisplayPath(trimmed) is not null) return 0.9f;

        // Environment variable path — expand and treat as absolute
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        if (expanded != trimmed && Path.IsPathRooted(expanded))
            return IsAcceptableDrive(expanded, fs) ? 0.85f : 0f;

        if (Path.IsPathRooted(trimmed))
            return IsAcceptableDrive(trimmed, fs) ? 0.9f : 0f;

        // Relative path — accept only when its first step is a folder the browser already holds: one listed in the open
        // folder, or under the root in the tree, or a step up or across ("..", "."). What was listed is the signal, not
        // a no-spaces heuristic (which both rejected real folders like "Program Files" and falsely claimed any single
        // word), and not the disk.
        return LeadsSomewhereListed(trimmed, fs) ? 0.6f : 0f;
    }

    public async Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not FileSystemViewModel fs)
            return Str.Get("WindowsFileSystem.Navigate.NoContext");

        var trimmed  = input.Trim();
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);

        var target = fs.Vfs.TryParseDisplayPath(trimmed)
                     ?? (Path.IsPathRooted(expanded) || IsFileSystemPath(expanded) ? expanded : await ResolveRelativeAsync(trimmed, fs));
        if (target is not null && await IsFolderAsync(fs, target))
        {
            await fs.BrowseToAsync(target);
            return null;
        }

        return Str.Format("WindowsFileSystem.Navigate.PathNotFoundFormat", target ?? trimmed);
    }

    /// <summary>A path in the file system's own namespace — a mount or a connected location (<c>::{id}\…</c>).</summary>
    private static bool IsFileSystemPath(string path) => path.StartsWith("::", StringComparison.Ordinal);

    private static async Task<bool> IsFolderAsync(FileSystemViewModel fs, string path)
    {
        try { return await fs.Vfs.IsDirectoryAsync(path); }
        catch (IOException) { return false; }
    }

    // Resolves a relative path against the current folder, then the root, returning the first that is a folder — or
    // null when not rooted in a folder, or neither candidate is one.
    private static async Task<string?> ResolveRelativeAsync(string relative, FileSystemViewModel fs)
    {
        if (fs.IsThisPcMode || string.IsNullOrEmpty(fs.RootPath)) return null;

        string?[] candidates =
        [
            string.IsNullOrEmpty(fs.CurrentPath) ? null : Combine(fs.CurrentPath, relative),
            Combine(fs.RootPath, relative),
        ];
        foreach (var candidate in candidates)
            if (candidate is not null && await IsFolderAsync(fs, candidate))
                return candidate;
        return null;
    }

    // Whether the relative path's first step is somewhere the browser has already listed — see CanProcess.
    private static bool LeadsSomewhereListed(string relative, FileSystemViewModel fs)
    {
        if (fs.IsThisPcMode || string.IsNullOrEmpty(fs.RootPath)) return false;

        var first = relative.Split(['\\', '/'], 2)[0];
        if (first is "." or "..") return true;

        if (fs.Entries.Any(e => e.IsDirectory && string.Equals(e.Name, first, StringComparison.OrdinalIgnoreCase)))
            return true;
        return fs.TreeRoots.FirstOrDefault() is { } root
               && string.Equals(root.FullPath, fs.RootPath, StringComparison.OrdinalIgnoreCase)
               && root.Children.Any(c => string.Equals(c.Name, first, StringComparison.OrdinalIgnoreCase));
    }

    // Combine + normalise, returning null instead of throwing on invalid input. A Windows path normalises as Windows
    // does; one in the file system's own namespace by its segments, never climbing above its root.
    private static string? Combine(string basePath, string relative)
    {
        try
        {
            if (!IsFileSystemPath(basePath)) return Path.GetFullPath(Path.Combine(basePath, relative));

            var segments = basePath.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
            foreach (var step in relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (step == ".") continue;
                if (step == "..")
                {
                    if (segments.Count > 1) segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                segments.Add(step);
            }
            return string.Join('\\', segments);
        }
        catch { return null; }
    }

    // Absolute path is acceptable when:
    //   - in This PC mode (can navigate to any drive)
    //   - root is empty (navigated out of This PC, no fixed root)
    //   - the path's drive matches the current root's drive
    private static bool IsAcceptableDrive(string path, FileSystemViewModel fs)
    {
        if (fs.IsThisPcMode) return true;
        if (string.IsNullOrEmpty(fs.RootPath)) return true;

        var pathDrive = Path.GetPathRoot(path);
        var rootDrive = Path.GetPathRoot(fs.RootPath);
        return string.Equals(pathDrive, rootDrive, StringComparison.OrdinalIgnoreCase);
    }
}
