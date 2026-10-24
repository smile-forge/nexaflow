using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.IO.Common;

// The async members, over the same paths the sync ones serve. Reads and writes of real files use overlapped IO;
// metadata calls, which have no async form in .NET, and archive work, which is CPU-bound over local bytes, run on the
// thread pool — so nothing waits on the disk on the caller's thread. Failures with a common meaning surface as
// LocationExceptions (LocationFailures).
public sealed partial class VirtualFileSystem
{
    /// <summary>Rows a listing hands over before the reader takes them: a screenful, small enough to show at once.</summary>
    private const int ListingPage = 256;

    private const LocationCapabilities LocalCapabilities =
        LocationCapabilities.RandomRead | LocationCapabilities.AtomicRename
        | LocationCapabilities.Watch | LocationCapabilities.Symlinks;

    public ValueTask<bool> ExistsAsync(string path, CancellationToken ct = default)
        => OffThread(() => Exists(path), path, ct);

    public ValueTask<bool> IsDirectoryAsync(string path, CancellationToken ct = default)
        => OffThread(() => IsDirectory(path), path, ct);

    public ValueTask<bool> IsContainerAsync(string path, CancellationToken ct = default)
        => OffThread(() => IsContainer(path), path, ct);

    public ValueTask<LocationEntry?> GetEntryAsync(string path, CancellationToken ct = default)
        => OffThread(() => EntryAt(path), path, ct);

    private LocationEntry? EntryAt(string path)
    {
        var real = ToReal(path);
        if (Directory.Exists(real)) return LocationEntry.FromFileSystemInfo(new DirectoryInfo(real));
        if (File.Exists(real))      return LocationEntry.FromFileSystemInfo(new FileInfo(real));
        return GetEntryInfo(real) is { } inArchive ? LocationEntry.FromArchive(inArchive) : null;
    }

    public async IAsyncEnumerable<LocationEntry> EnumerateAsync(string path, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var page = Channel.CreateBounded<LocationEntry>(
            new BoundedChannelOptions(ListingPage) { SingleReader = true, SingleWriter = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var walk = Task.Run(() => ListInto(path, page.Writer, stop.Token), CancellationToken.None);
        try
        {
            await foreach (var entry in page.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return entry;
        }
        finally
        {
            // A reader that stops early must not leave the walk blocked on a full page.
            stop.Cancel();
            await walk.ConfigureAwait(false);
        }
    }

    /// <summary>Walks the folder into the page; never throws — a failure completes the page with it, which is
    /// where the reader sees it.</summary>
    private async Task ListInto(string path, ChannelWriter<LocationEntry> page, CancellationToken ct)
    {
        try
        {
            var real = ToReal(path);
            if (Directory.Exists(real))
            {
                foreach (var info in new DirectoryInfo(real).EnumerateFileSystemInfos())
                    await page.WriteAsync(LocationEntry.FromFileSystemInfo(info), ct).ConfigureAwait(false);
            }
            else
            {
                if (!IsDirectory(real)) throw LocationFailures.NotFound(path);
                foreach (var entry in EnumerateEntries(real))
                    await page.WriteAsync(LocationEntry.FromArchive(entry), ct).ConfigureAwait(false);
            }
            page.Complete();
        }
        catch (System.Exception e)
        {
            page.Complete(LocationFailures.Translate(e, path) ?? e);
        }
    }

    public ValueTask<Stream> OpenReadAsync(string path, ReadIntent intent, CancellationToken ct = default)
        => OffThread<Stream>(() => new FileStream(ReadableFile(path), new FileStreamOptions
        {
            Mode       = FileMode.Open,
            Access     = FileAccess.Read,
            Share      = FileShare.ReadWrite,
            BufferSize = intent == ReadIntent.Random ? 4096 : 81920,
            Options    = FileOptions.Asynchronous
                         | (intent == ReadIntent.Random ? FileOptions.RandomAccess : FileOptions.SequentialScan),
        }), path, ct);

    /// <summary>The real file whose bytes a path reads: itself, or the materialised copy of an archive entry.</summary>
    private string ReadableFile(string path)
    {
        var real = ToReal(path);
        return File.Exists(real) ? real : MaterializeFile(real);
    }

    public ValueTask<ILocationWrite> BeginWriteAsync(string path, WriteMode mode, LocationEntry? expected = null, CancellationToken ct = default)
        => OffThread<ILocationWrite>(() =>
        {
            var real = ToReal(path);
            if (!IsVirtual(real)) return FileLocationWrite.Open(real, path, mode, expected);
            return new BufferedLocationWrite((bytes, token) =>
                OffThread(() => CommitArchiveEntry(real, path, mode, expected, bytes), path, token));
        }, path, ct);

    /// <summary>Rewrites an archive entry in one piece, after the same checks a file write makes.</summary>
    private void CommitArchiveEntry(string real, string path, WriteMode mode, LocationEntry? expected, byte[] bytes)
    {
        var current = EntryAt(real);
        if (mode == WriteMode.CreateNew && current is not null)
            throw LocationFailures.Conflict(path, $"'{path}' already exists.");
        if (expected is not null && (current is null || !current.IsSameVersion(expected)))
            throw LocationFailures.Conflict(path, $"'{path}' changed since it was read.");
        Replace(real, mode == WriteMode.Append && current is not null ? [.. ReadAllBytes(real), .. bytes] : bytes);
    }

    public async ValueTask<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        var file = await OffThread(() => ReadableFile(path), path, ct).ConfigureAwait(false);
        try { return await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false); }
        catch (System.Exception e) when (LocationFailures.Translate(e, path) is { } failure) { throw failure; }
    }

    public async ValueTask<string> ReadAllTextAsync(string path, Encoding? encoding = null, CancellationToken ct = default)
    {
        var file = await OffThread(() => ReadableFile(path), path, ct).ConfigureAwait(false);
        try
        {
            return encoding is null
                ? await File.ReadAllTextAsync(file, ct).ConfigureAwait(false)
                : await File.ReadAllTextAsync(file, encoding, ct).ConfigureAwait(false);
        }
        catch (System.Exception e) when (LocationFailures.Translate(e, path) is { } failure) { throw failure; }
    }

    public async ValueTask WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        var (real, inArchive) = await OffThread(() => { var r = ToReal(path); return (r, IsVirtual(r)); }, path, ct)
            .ConfigureAwait(false);
        if (inArchive)
        {
            await OffThread(() => Replace(real, bytes), path, ct).ConfigureAwait(false);
            return;
        }
        try { await File.WriteAllBytesAsync(real, bytes, ct).ConfigureAwait(false); }
        catch (System.Exception e) when (LocationFailures.Translate(e, path) is { } failure) { throw failure; }
    }

    public ValueTask WriteAllTextAsync(string path, string contents, Encoding? encoding = null, CancellationToken ct = default)
        => WriteAllBytesAsync(path, EncodeText(contents, encoding), ct);

    public async ValueTask ReplaceAsync(string path, byte[] newContent, CancellationToken ct = default)
    {
        await using var write = await BeginWriteAsync(path, WriteMode.Replace, null, ct).ConfigureAwait(false);
        await write.Content.WriteAsync(newContent, ct).ConfigureAwait(false);
        await write.CommitAsync(ct).ConfigureAwait(false);
    }

    public ValueTask MoveAsync(string from, string to, bool replace, CancellationToken ct = default)
        => OffThread(() =>
        {
            var source = OnDisk(from, nameof(MoveAsync));
            var target = OnDisk(to, nameof(MoveAsync));
            if (Directory.Exists(source))
            {
                if (Directory.Exists(target) || File.Exists(target))
                    throw LocationFailures.Conflict(to, $"'{to}' already exists; a folder is never moved over another entry.");
                Directory.Move(source, target);
            }
            else if (File.Exists(source)) File.Move(source, target, replace);
            else throw LocationFailures.NotFound(from);
        }, from, ct);

    public ValueTask DeleteAsync(string path, bool recursive, CancellationToken ct = default)
        => OffThread(() =>
        {
            var real = OnDisk(path, nameof(DeleteAsync));
            if (Directory.Exists(real))  Directory.Delete(real, recursive);
            else if (File.Exists(real))  File.Delete(real);
            else throw LocationFailures.NotFound(path);
        }, path, ct);

    public ValueTask CreateDirectoryAsync(string path, CancellationToken ct = default)
        => OffThread(() => { Directory.CreateDirectory(OnDisk(path, nameof(CreateDirectoryAsync))); }, path, ct);

    /// <summary>The real path behind <paramref name="path"/> for an operation that changes the file system itself,
    /// which an archive entry cannot take part in — archives change through their own members.</summary>
    private string OnDisk(string path, string operation)
    {
        var real = ToReal(path);
        if (IsVirtual(real))
            throw new System.NotSupportedException(
                $"'{path}' is inside an archive; {operation} works on files and folders, and an archive changes through AddFilesAsync or RepackageAsync.");
        return real;
    }

    public ValueTask<ILocationWatch?> WatchChangesAsync(string path, bool recursive, CancellationToken ct = default)
        => OffThread<ILocationWatch?>(() =>
        {
            var real = ToReal(path);
            if (Directory.Exists(real))
                return new DirectoryLocationWatch(real, null, recursive,
                    name => name is null ? path : path.TrimEnd('\\', '/') + "\\" + name);
            if (File.Exists(real) && Path.GetDirectoryName(real) is { Length: > 0 } folder)
                return new DirectoryLocationWatch(folder, Path.GetFileName(real), recursive: false, _ => path);
            return null;   // inside an archive (watch the container instead), or nothing there
        }, path, ct);

    public LocationCapabilities GetCapabilities(string path)
        => GetBacking(path) == VirtualBacking.Materialized ? LocationCapabilities.RandomRead : LocalCapabilities;

    public ValueTask<ArchiveSummary?> DescribeArchiveAsync(string containerPath, CancellationToken ct = default)
        => OffThread(() => DescribeArchive(containerPath), containerPath, ct);

    public ValueTask ExtractAllAsync(string containerPath, string destinationDir,
                                     System.IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        => OffThread(() => ExtractAll(containerPath, destinationDir, progress, ct), containerPath, ct);

    public ValueTask CreateArchiveAsync(string archivePath, string sourceDir,
                                        System.IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        => OffThread(() => CreateArchive(archivePath, sourceDir, progress, ct), archivePath, ct);

    public ValueTask CreateArchiveAsync(string archivePath, IReadOnlyList<string> sourcePaths,
                                        System.IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        => OffThread(() => CreateArchive(archivePath, sourcePaths, progress, ct), archivePath, ct);

    public ValueTask RepackageAsync(string sourcePath, string targetPath, ArchiveWriteOptions? options = null, CancellationToken ct = default)
        => OffThread(() => Repackage(sourcePath, targetPath, options), sourcePath, ct);

    public ValueTask AddFilesAsync(string containerPath, IReadOnlyList<(string SourcePath, string EntryName)> files, CancellationToken ct = default)
        => OffThread(() => AddFiles(containerPath, files), containerPath, ct);

    /// <summary>A local path is its own display form.</summary>
    public string GetDisplayPath(string path) => path;

    /// <summary>The shared file system holds no remote location, so no display form names one of its paths.</summary>
    public string? TryParseDisplayPath(string text) => null;

    /// <summary>Text as <see cref="File.WriteAllText(string, string?, Encoding)"/> writes it: the encoding's
    /// preamble (BOM) first when it has one; UTF-8 without one by default.</summary>
    internal static byte[] EncodeText(string contents, Encoding? encoding)
    {
        var enc      = encoding ?? new UTF8Encoding(false);
        var preamble = enc.GetPreamble();
        var body     = enc.GetBytes(contents);
        return preamble.Length == 0 ? body : [.. preamble, .. body];
    }

    private static async ValueTask<T> OffThread<T>(System.Func<T> work, string path, CancellationToken ct)
    {
        try { return await Task.Run(work, ct).ConfigureAwait(false); }
        catch (System.Exception e) when (LocationFailures.Translate(e, path) is { } failure) { throw failure; }
    }

    private static async ValueTask OffThread(System.Action work, string path, CancellationToken ct)
    {
        try { await Task.Run(work, ct).ConfigureAwait(false); }
        catch (System.Exception e) when (LocationFailures.Translate(e, path) is { } failure) { throw failure; }
    }
}
