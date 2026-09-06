using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Compressed.FileActions;

/// <summary>Extracts an archive into a new sibling folder named after it (zip-slip guarded by the VFS).</summary>
public sealed class UnzipHereAction(IShellServices shell, IFileOperationHost? operations = null)
    : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/archive";
    public string ExperienceId => "/archive";
    public string ExperienceDescription => "Compressed archive";

    public string DisplayName => "Unzip here";
    public string Icon => "📂";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => true;
    public bool RequiresRefresh => false;   // the queue refreshes when the operation finishes
    public bool CanPerformAction => true;
    public bool OpensViewer => false;

    public bool PerformAction(string filePath)
    {
        string dest;
        try
        {
            dest = UniqueSiblingDir(filePath);
            // Claim the name now, for the same reason ZipItAction claims its archive name: it was picked
            // synchronously but the extraction is seconds away, so two unzips would otherwise pick the same one.
            if (operations is not null) Directory.CreateDirectory(dest);
        }
        catch (Exception ex)
        {
            shell.ShowError($"Could not extract '{Path.GetFileName(filePath)}': {ex.Message}");
            return false;
        }

        // No host means nobody is showing progress, so there is nothing to be gained by going async.
        if (operations is null) return ExtractNow(filePath, dest);

        _ = operations.Run(
            new FileOperationRequest("Extracting", Path.GetFileName(filePath), Path.GetFileName(dest), dest),
            (progress, ct) => Task.Run(() => Extract(filePath, dest, progress, ct), CancellationToken.None));
        return true;
    }

    /// <summary>
    /// The extraction as the progress row wants it: every outcome described in the result rather than
    /// thrown, so a cancellation can still name the folder it left half-populated and the panel can
    /// offer to clear it up.
    /// </summary>
    private static TransferResult Extract(string archivePath, string dest,
                                          IProgress<TransferProgress> progress, CancellationToken ct)
    {
        try
        {
            VirtualFileSystem.Instance.ExtractAll(archivePath, dest, progress, ct);
            return new TransferResult(true, 0, 0, [], [], [], []);
        }
        catch (OperationCanceledException)
        {
            return new TransferResult(false, 0, 0, [], [], [], [dest]);
        }
        catch (Exception ex)
        {
            return new TransferResult(true, 0, 0,
                [new TransferItemFailure(archivePath, "extract", 0,
                    $"Could not extract '{Path.GetFileName(archivePath)}': {ex.Message}")],
                [], [], [dest]);
        }
    }

    /// <summary>The original blocking path, kept for when there is no progress row to run in.</summary>
    private bool ExtractNow(string filePath, string dest)
    {
        try
        {
            VirtualFileSystem.Instance.ExtractAll(filePath, dest);
            return true;
        }
        catch (Exception ex)
        {
            shell.ShowError($"Could not extract '{Path.GetFileName(filePath)}': {ex.Message}");
            return false;
        }
    }

    /// <summary>One row per archive, so several selected archives are several cancellable operations
    /// rather than one opaque wait — the same shape as dropping three folders.</summary>
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
