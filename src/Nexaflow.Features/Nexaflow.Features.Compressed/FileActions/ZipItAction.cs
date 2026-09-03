using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed.FileActions;

/// <summary>Compresses the selection into a sibling archive of the configured default format — a folder,
/// a file, or a whole multi-selection of either in one archive. (A format-pick overlay is offered once
/// more than one create-capable format is installed.)</summary>
public sealed class ZipItAction(IShellServices shell, CompressedConfig config) : IFileAction, IFolderAction, ICacheable
{
    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => true;
    public string Icon => "📦";
    public string DisplayName => "Zip It";

    public bool RequiresRefresh => true;
    public bool CanPerformAction => true;

    // ── IFileAction ──────────────────────────────────────────────────────────
    // Zipping is tied to no file type, so it claims the root experience the way Copy/Delete do — that is
    // what puts it on a selection of files, one or many, as well as on folders.
    public static string? StaticExperienceId => "/";
    public string ExperienceId => "/";
    public string ExperienceDescription => "All files";
    public bool OpensViewer => false;
    // The bytes are read off disk: a file materialised out of an archive is one lone temp copy, with none
    // of the neighbours the selection names.
    public bool RequiresFullyBackedPath => true;

    // ── IFolderAction ────────────────────────────────────────────────────────
    public bool AppliesToRoot => true;
    public bool AppliesToDrives => false;
    public bool AppliesInsideArchive => false;   // nothing real to compress from a virtual path

    /// <summary>Compresses one item into an archive named after it: a folder as its contents (no wrapper
    /// directory), a file on its own with its extension replaced.</summary>
    public bool PerformAction(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent  = Path.GetDirectoryName(trimmed);
        var name    = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
        {
            shell.ShowError("Cannot zip a drive root.");
            return false;
        }

        // A file drops its own extension ("report.docx" → "report.zip"); a folder keeps its whole name,
        // which may itself contain a dot.
        bool isFolder = Directory.Exists(trimmed);
        var  baseName = isFolder ? name : Path.GetFileNameWithoutExtension(name);

        return Compress(parent, baseName, $"'{name}'", dest =>
        {
            if (isFolder) VirtualFileSystem.Instance.CreateArchive(dest, trimmed);
            else          VirtualFileSystem.Instance.CreateArchive(dest, new[] { trimmed });
        });
    }

    /// <summary>Compresses the whole selection — files, folders or a mix — into ONE archive beside it,
    /// named after the folder the items sit in. A selection of one is zipped under its own name instead.</summary>
    public bool PerformAction(IEnumerable<string> paths)
    {
        var items = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
        if (items.Count == 0) return false;
        if (items.Count == 1) return PerformAction(items[0]);

        var parent = Path.GetDirectoryName(
            items[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent))
        {
            shell.ShowError("Cannot zip a drive root.");
            return false;
        }

        // A selection has no name of its own, so it borrows the folder it came from; straight off a drive
        // root there is no such name to borrow.
        var folderName = Path.GetFileName(
            parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var baseName = string.IsNullOrEmpty(folderName) ? "Archive" : folderName;

        return Compress(parent, baseName, $"{items.Count} items",
                        dest => VirtualFileSystem.Instance.CreateArchive(dest, items));
    }

    /// <summary>Writes an archive of the configured format at a free name under <paramref name="parent"/>,
    /// reporting any refusal or failure against <paramref name="subject"/>.</summary>
    private bool Compress(string parent, string baseName, string subject, Action<string> write)
    {
        try
        {
            var ext  = NormalizeFormat(config.DefaultFormat);
            var dest = UniqueArchivePath(parent, baseName, ext);

            if (!VirtualFileSystem.Instance.CanCreate(Path.GetFileName(dest)))
            {
                shell.ShowError($"No installed provider can create a '{config.DefaultFormat}' archive.");
                return false;
            }

            write(dest);
            return true;
        }
        catch (Exception ex)
        {
            shell.ShowError($"Could not zip {subject}: {ex.Message}");
            return false;
        }
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
