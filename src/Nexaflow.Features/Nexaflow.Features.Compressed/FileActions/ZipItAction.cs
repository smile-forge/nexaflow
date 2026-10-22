using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Compressed.FileActions;

/// <summary>Compresses the selection into a sibling archive of the configured default format — a folder,
/// a file, or a whole multi-selection of either in one archive. (A format-pick overlay is offered once
/// more than one create-capable format is installed.)</summary>
public sealed class ZipItAction(IShellServices shell, CompressedConfig config,
                               IFileOperationHost? operations = null)
    : IFileAction, IFolderAction, ICacheable
{
    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => true;
    public string Icon => "📦";
    public string DisplayName => Str.Get("Compressed.Action.ZipIt");

    public bool RequiresRefresh => false;   // the queue refreshes when the operation finishes
    public bool CanPerformAction => true;

    // ── IFileAction ──────────────────────────────────────────────────────────
    // Zipping is tied to no file type, so it claims the root experience the way Copy/Delete do — that is
    // what puts it on a selection of files, one or many, as well as on folders.
    public static string? StaticExperienceId => "/";
    public string ExperienceId => "/";
    public string ExperienceDescription => Str.Get("Compressed.Experience.AllFiles");
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
            shell.ShowError(Str.Get("Compressed.ZipIt.DriveRoot"));
            return false;
        }

        // A file drops its own extension ("report.docx" → "report.zip"); a folder keeps its whole name,
        // which may itself contain a dot.
        bool isFolder = Directory.Exists(trimmed);
        var  baseName = isFolder ? name : Path.GetFileNameWithoutExtension(name);

        return isFolder
            ? Compress(parent, baseName, ZipSubject.Named(name), trimmed, null)
            : Compress(parent, baseName, ZipSubject.Named(name), null, [trimmed]);
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
            shell.ShowError(Str.Get("Compressed.ZipIt.DriveRoot"));
            return false;
        }

        // A selection has no name of its own, so it borrows the folder it came from; straight off a drive
        // root there is no such name to borrow.
        var folderName = Path.GetFileName(
            parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var baseName = string.IsNullOrEmpty(folderName) ? Str.Get("Compressed.ZipIt.DefaultName") : folderName;

        return Compress(parent, baseName, ZipSubject.Selection(items.Count), null, items);
    }

    /// <summary>Writes an archive of the configured format at a free name under <paramref name="parent"/>,
    /// reporting any refusal or failure against <paramref name="subject"/>. Exactly one of
    /// <paramref name="sourceDir"/> and <paramref name="sources"/> is given.</summary>
    private bool Compress(string parent, string baseName, ZipSubject subject,
                          string? sourceDir, IReadOnlyList<string>? sources)
    {
        string dest;
        try
        {
            var ext = NormalizeFormat(config.DefaultFormat);
            dest = UniqueArchivePath(parent, baseName, ext);

            if (!VirtualFileSystem.Instance.CanCreate(Path.GetFileName(dest)))
            {
                shell.ShowError(Str.Format("Compressed.ZipIt.NoProviderFormat", config.DefaultFormat));
                return false;
            }
        }
        catch (Exception ex)
        {
            shell.ShowError(subject.Failure(ex.Message));
            return false;
        }

        // No host means nobody is showing progress, so there is nothing to be gained by going async.
        if (operations is null) return CompressNow(dest, subject, sourceDir, sources);

        // Claim the name before returning. It was picked synchronously but the write is now seconds away,
        // so without this two Zip Its in quick succession would both pick it and the second would replace
        // the first. WriteNewArchive already replaces an existing file, so a placeholder costs nothing.
        try { File.Create(dest).Dispose(); }
        catch (Exception ex)
        {
            shell.ShowError(subject.Failure(ex.Message));
            return false;
        }

        _ = operations.Run(
            new FileOperationRequest(Str.Get("Compressed.Operation.Compressing"), subject.RowLabel, Path.GetFileName(dest), dest),
            (progress, ct) => Task.Run(() => Build(dest, subject, sourceDir, sources, progress, ct),
                                       CancellationToken.None));
        return true;
    }

    /// <summary>
    /// The write as the progress row wants it: every outcome described in the result rather than thrown.
    /// A run that does not finish takes the placeholder with it, so a cancelled zip leaves nothing behind
    /// — the temp is already the VFS's to clear.
    /// </summary>
    private static TransferResult Build(string dest, ZipSubject subject, string? sourceDir,
                                        IReadOnlyList<string>? sources,
                                        IProgress<TransferProgress> progress, CancellationToken ct)
    {
        try
        {
            if (sourceDir is not null) VirtualFileSystem.Instance.CreateArchive(dest, sourceDir, progress, ct);
            else                       VirtualFileSystem.Instance.CreateArchive(dest, sources!, progress, ct);
            return new TransferResult(true, 0, 0, [], [], [], []);
        }
        catch (OperationCanceledException)
        {
            Discard(dest);
            return new TransferResult(false, 0, 0, [], [], [], []);
        }
        catch (Exception ex)
        {
            Discard(dest);
            return new TransferResult(true, 0, 0,
                [new TransferItemFailure(dest, "compress", 0, subject.Failure(ex.Message))],
                [], [], []);
        }
    }

    private static void Discard(string dest)
    {
        try { File.Delete(dest); } catch { /* best effort */ }
    }

    /// <summary>The original blocking path, kept for when there is no progress row to run in.</summary>
    private bool CompressNow(string dest, ZipSubject subject, string? sourceDir, IReadOnlyList<string>? sources)
    {
        try
        {
            if (sourceDir is not null) VirtualFileSystem.Instance.CreateArchive(dest, sourceDir);
            else                       VirtualFileSystem.Instance.CreateArchive(dest, sources!);
            return true;
        }
        catch (Exception ex)
        {
            shell.ShowError(subject.Failure(ex.Message));
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

    /// <summary>What one Zip It is of: a single item by its name, or a selection by its count. The progress
    /// row and every failure sentence name it from here, each as one whole sentence of the language pack.</summary>
    private readonly record struct ZipSubject(string? Name, int Count)
    {
        public static ZipSubject Named(string name) => new(name, 1);
        public static ZipSubject Selection(int count) => new(null, count);

        /// <summary>The progress row's subject: the item's own name, or "3 items".</summary>
        public string RowLabel => Name ?? Str.Format("Compressed.ZipIt.ItemsFormat", Count);

        /// <summary>The sentence a failure to zip this subject is reported in.</summary>
        public string Failure(string reason) => Name is not null
            ? Str.Format("Compressed.ZipIt.FailedNamedFormat", Name, reason)
            : Str.Format("Compressed.ZipIt.FailedItemsFormat", Count, reason);
    }
}
