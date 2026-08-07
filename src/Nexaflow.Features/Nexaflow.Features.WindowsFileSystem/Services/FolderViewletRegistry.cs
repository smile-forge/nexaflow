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
        // IsContainer covers the archive file itself. Its backing is Real — it IS a real file, which is
        // the right answer for an action operating ON the archive — but browsing it means looking at its
        // entries, and those are no more a folder than the ones a level deeper.
        var vfs = VirtualFileSystem.Instance;
        bool fullyBacked = vfs.GetBacking(folderPath) != VirtualBacking.Materialized
                           && !vfs.IsContainer(folderPath);

        var result = new List<IFolderViewlet>();
        foreach (var viewlet in registry.FolderViewlets)
        {
            if (!fullyBacked && viewlet.RequiresFullyBackedPath) continue;
            if (isDrive && !viewlet.AppliesToDrives) continue;
            if (!FolderNameMatches(viewlet, folderPath)) continue;
            if (!ContentsMatch(viewlet, folderPath)) continue;
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

    private static bool ContentsMatch(IFolderViewlet viewlet, string folderPath)
    {
        var fileGlobs   = viewlet.ContainsFileGlobs;
        var folderGlobs = viewlet.ContainsFolderGlobs;
        if (fileGlobs is null && folderGlobs is null) return true;

        // Probe where the files actually are, or a mounted repo folder would match nothing and the Git
        // and .NET viewlets would silently never appear there.
        folderPath = VirtualFileSystem.Instance.TryResolveReal(folderPath) ?? folderPath;

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
