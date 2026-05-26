using Nexaflow.Features.Common.Viewlets;
using System.IO;

namespace Nexaflow.Core.Services;

/// <summary>
/// Filters the <see cref="IFolderViewlet"/> instances owned by
/// <see cref="FileSystemFeatureRegistry"/> against the current folder path.
/// </summary>
public static class FolderViewletRegistry
{
    public static IReadOnlyList<IFolderViewlet> GetMatchingViewlets(
        string folderPath, FileSystemFeatureRegistry registry, bool isDrive = false)
    {
        var result = new List<IFolderViewlet>();
        foreach (var viewlet in registry.FolderViewlets)
        {
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
