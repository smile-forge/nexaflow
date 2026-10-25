using Nexaflow.Features.Common.Viewlets;
using Nexaflow.IO.Common;
using System.IO;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Filters the <see cref="IFolderViewlet"/> instances owned by
/// <see cref="FileSystemFeatureRegistry"/> against the current folder path.
/// </summary>
public static class FolderViewletRegistry
{
    public static IReadOnlyList<IFolderViewlet> GetMatchingViewlets(
        string folderPath, FileSystemFeatureRegistry registry, bool isDrive = false)
    {
        // Inside an archive there is no folder — only entries in a container — so a viewlet that shells
        // out has nothing to work in. Checked up front rather than left to the structural probe below:
        // that probe only fails by accident (enumerating a path that isn't a directory throws), and a
        // viewlet declaring no criteria at all would otherwise sail past it.
        //
        // The archive file itself is covered by the folder check: its backing is Real — it IS a real file, which is
        // the right answer for an action operating ON the archive — but browsing it means looking at its entries,
        // and those are no more a folder than the ones a level deeper. A folder on a location has none on this disk.
        var vfs = registry.Vfs;
        string? realFolder = vfs.ResolveOnDisk(folderPath);
        bool fullyBacked = realFolder is not null && IsFolderHere(realFolder);

        var result = new List<IFolderViewlet>();
        foreach (var viewlet in registry.FolderViewlets)
        {
            if (!fullyBacked && viewlet.RequiresFullyBackedPath) continue;
            if (isDrive && !viewlet.AppliesToDrives) continue;
            if (!FolderNameMatches(viewlet, folderPath)) continue;
            if (!ContentsMatch(viewlet, realFolder)) continue;
            result.Add(viewlet);
        }
        return result;
    }

    private static bool FolderNameMatches(IFolderViewlet viewlet, string folderPath)
    {
        var glob = viewlet.FolderNameGlob;
        if (glob is "*") return true;
        var name = Path.GetFileName(folderPath);
        return GlobMatch(name, glob);
    }

    /// <summary>Whether <paramref name="realFolder"/> is a folder on this disk — not the archive file a browse into
    /// one starts at.</summary>
    [LocalDiskOnly("a folder the file system resolved to this disk")]
    private static bool IsFolderHere(string realFolder) => Directory.Exists(realFolder);

    /// <summary>Whether the folder holds what the viewlet asks for, probed where the files actually are — or a
    /// mounted repo folder would match nothing and the Git and .NET viewlets would silently never appear there. A
    /// folder with nothing on this disk (<paramref name="folderPath"/> null: an archive, a location) holds nothing a
    /// contents rule can find.</summary>
    [LocalDiskOnly("a folder the file system resolved to this disk")]
    private static bool ContentsMatch(IFolderViewlet viewlet, string? folderPath)
    {
        var fileGlobs   = viewlet.ContainsFileGlobs;
        var folderGlobs = viewlet.ContainsFolderGlobs;
        if (fileGlobs is null && folderGlobs is null) return true;
        if (folderPath is null) return false;

        try
        {
            if (fileGlobs is not null)
            {
                bool found = false;
                foreach (var pattern in fileGlobs)
                    if (Directory.EnumerateFiles(folderPath, pattern, SearchOption.TopDirectoryOnly).Any())
                    { found = true; break; }
                if (!found) return false;
            }
            if (folderGlobs is not null)
            {
                bool found = false;
                foreach (var pattern in folderGlobs)
                    if (Directory.EnumerateDirectories(folderPath, pattern, SearchOption.TopDirectoryOnly).Any())
                    { found = true; break; }
                if (!found) return false;
            }
        }
        catch { return false; }

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
