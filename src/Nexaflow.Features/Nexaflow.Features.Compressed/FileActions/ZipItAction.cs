using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed.FileActions;

/// <summary>Compresses a folder into a sibling archive of the configured default format. (A format-pick
/// overlay is offered once more than one create-capable format is installed.)</summary>
public sealed class ZipItAction(IShellServices shell, CompressedConfig config) : IFolderAction, ICacheable
{
    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => true;
    public string Icon => "📦";
    public string DisplayName => "Zip It";

    public bool RequiresRefresh => true;
    public bool CanPerformAction => true;
    public bool AppliesToRoot => true;
    public bool AppliesToDrives => false;

    public bool PerformAction(string folderPath)
    {
        try
        {
            var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent  = Path.GetDirectoryName(trimmed) ?? trimmed;
            var name    = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name)) { shell.ShowError("Cannot zip a drive root."); return false; }

            var ext  = NormalizeFormat(config.DefaultFormat);
            var dest = UniqueArchivePath(parent, name, ext);

            if (!VirtualFileSystem.Instance.CanCreate(Path.GetFileName(dest)))
            {
                shell.ShowError($"No installed provider can create a '{config.DefaultFormat}' archive.");
                return false;
            }

            VirtualFileSystem.Instance.CreateArchive(dest, folderPath);
            return true;
        }
        catch (Exception ex)
        {
            shell.ShowError($"Could not zip '{Path.GetFileName(folderPath)}': {ex.Message}");
            return false;
        }
    }

    public bool PerformAction(IEnumerable<string> folderPaths)
    {
        bool any = false;
        foreach (var p in folderPaths) any |= PerformAction(p);
        return any;
    }

    private static string NormalizeFormat(string format)
    {
        var f = (format ?? "zip").Trim().TrimStart('.').ToLowerInvariant();
        return "." + f;   // "zip" → ".zip", "tar.gz" → ".tar.gz"
    }

    private static string UniqueArchivePath(string parent, string baseName, string ext)
    {
        var dest = Path.Combine(parent, baseName + ext);
        for (int n = 1; File.Exists(dest) || Directory.Exists(dest); n++)
            dest = Path.Combine(parent, $"{baseName} ({n}){ext}");
        return dest;
    }
}
