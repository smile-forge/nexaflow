using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed.FileActions;

/// <summary>Extracts an archive into a new sibling folder named after it (zip-slip guarded by the VFS).</summary>
public sealed class UnzipHereAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/archive";
    public string ExperienceId => "/archive";
    public string ExperienceDescription => "Compressed archive";

    public string DisplayName => "Unzip here";
    public string Icon => "📂";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => true;
    public bool RequiresRefresh => true;
    public bool CanPerformAction => true;
    public bool OpensViewer => false;

    public bool PerformAction(string filePath)
    {
        try
        {
            VirtualFileSystem.Instance.ExtractAll(filePath, UniqueSiblingDir(filePath));
            return true;
        }
        catch (Exception ex)
        {
            shell.ShowError($"Could not extract '{Path.GetFileName(filePath)}': {ex.Message}");
            return false;
        }
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        bool any = false;
        foreach (var p in filePaths) any |= PerformAction(p);
        return any;
    }

    private static string UniqueSiblingDir(string archivePath)
    {
        var dir      = Path.GetDirectoryName(archivePath) ?? ".";
        var baseName = StripArchiveExtension(Path.GetFileName(archivePath));
        var dest     = Path.Combine(dir, baseName);
        for (int n = 1; Directory.Exists(dest) || File.Exists(dest); n++)
            dest = Path.Combine(dir, $"{baseName} ({n})");
        return dest;
    }

    /// <summary>Strips the archive extension, including compound forms (<c>.tar.gz</c> → name).</summary>
    private static string StripArchiveExtension(string fileName)
    {
        foreach (var compound in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst" })
            if (fileName.EndsWith(compound, StringComparison.OrdinalIgnoreCase))
                return fileName[..^compound.Length];
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
