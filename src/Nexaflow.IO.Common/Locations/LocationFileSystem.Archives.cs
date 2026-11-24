using System.Runtime.CompilerServices;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// Archives on a location, which run over local bytes as all archive work does. On a location this machine reaches as a
/// Windows folder (a WSL distro) an archive, and what is inside it, is read through that folder — a native path is for
/// readers. Every change to one — an entry written, files added, the archive made or remade — is made on a local copy and put
/// in place through the location, and what is extracted into a location is written through it entry by entry, so the location
/// keeps its exact names and a replaced file its permissions. An archive made of a location's files names each as the
/// location does, though Windows shows a name it cannot hold mapped. An entry is not removed, moved or made a folder inside an
/// archive, here as on this PC. An archive on any other location is refused until the content cache lands.
/// </summary>
public sealed partial class LocationFileSystem
{
    /// <summary>An archive on a location, or a path inside one: the archive's location path, where Windows reaches it, and
    /// the path within it — empty for the archive itself.</summary>
    private readonly record struct LocationArchive(string Container, string Native, string Inner)
    {
        /// <summary>Where it is read: the archive's Windows path, and the entry within it.</summary>
        public string ReadPath => Inner.Length == 0 ? Native : Native + "\\" + Inner;

        /// <summary>A path inside the archive, not the archive itself.</summary>
        public bool IsEntry => Inner.Length > 0;

        /// <summary>The same path inside a local copy of the archive.</summary>
        public string Within(string copy) => Inner.Length == 0 ? copy : copy + "\\" + Inner;
    }

    /// <summary>
    /// The archive <paramref name="path"/> is, or is inside, on a location Windows reaches as a folder — named by the location's
    /// path or by that folder's; null for any other path. Only a name an archive handler claims is looked at on the disk, so a
    /// plain path costs nothing.
    /// </summary>
    [LocalDiskOnly("whether a name the archive handlers claim is a file, asked of the Windows folder showing the location")]
    private LocationArchive? ArchiveOf(string path)
    {
        var located = OnLocation(path);
        if (located is null || !TryRoute(located, out var location, out var remainder) || remainder.Length == 0) return null;

        var segments = remainder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            if (!_shared.IsContainerName(LocationPath.DecodeSegment(segments[i]))) continue;
            var archive = string.Join('\\', segments[..(i + 1)]);
            if (NativePathOf(location, archive) is not { } native || !File.Exists(native)) continue;
            var inner = string.Join('\\', segments[(i + 1)..].Select(LocationPath.DecodeSegment));
            return new LocationArchive(location.Root + "\\" + archive, native, inner);
        }
        return null;
    }

    /// <summary>Where archive work reads <paramref name="path"/>: on a location, the Windows folder showing it; anywhere else
    /// the path itself. A location Windows does not reach has no bytes on this disk to read.</summary>
    private string ArchiveSource(string path, [CallerMemberName] string member = "")
    {
        if (ArchiveOf(path) is { } archive) return archive.ReadPath;
        if (OnLocation(path) is not { } located) return path;
        return TryRoute(located, out var location, out var remainder) && NativePathOf(location, remainder) is { } native
            ? native
            : throw new NotSupportedException($"{member} works on local bytes; '{path}' is on a location this machine keeps no copy of.");
    }

    /// <summary>A folder of its own for one piece of archive work over local bytes, removed with everything in it once done.</summary>
    private sealed class ArchiveStaging : IAsyncDisposable
    {
        public string Folder { get; } = Directory.CreateDirectory(
            Path.Combine(CacheLocation.SessionFolder("location-archives"), Guid.NewGuid().ToString("N"))).FullName;

        /// <summary>A path in the folder for a file named <paramref name="fileName"/>, with what Windows cannot hold in a name made safe.</summary>
        public string Holding(string fileName) => Path.Combine(Folder, string.Concat(fileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(Folder, recursive: true); }
            catch (Exception gone) when (gone is IOException or UnauthorizedAccessException) { /* the session's folder is let go of later */ }
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A local copy of an archive on a location, read where Windows reaches it.</summary>
    private static async ValueTask<string> CopyOutAsync(LocationArchive archive, ArchiveStaging staging, CancellationToken ct)
    {
        var copy = staging.Holding(Path.GetFileName(archive.Native));
        await using var source = new FileStream(archive.Native, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                                                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var target = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await source.CopyToAsync(target, ct).ConfigureAwait(false);
        return copy;
    }

    /// <summary>A file on this disk made <paramref name="target"/>'s content through the location, whole — a file it replaces
    /// keeps its permissions, and one changed since <paramref name="expected"/> was read is not overwritten.</summary>
    private async ValueTask PutInPlaceAsync(string local, string target, LocationEntry? expected, CancellationToken ct)
    {
        await using var write = await BeginWriteAsync(target, WriteMode.Replace, expected, ct: ct).ConfigureAwait(false);
        await using (var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
            await source.CopyToAsync(write.Content, ct).ConfigureAwait(false);
        await write.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A change to an archive on a location, made over a local copy and put back through the location.</summary>
    private async ValueTask RewriteArchiveAsync(LocationArchive archive, Func<string, ValueTask> change, CancellationToken ct)
    {
        var expected = await GetEntryAsync(archive.Container, ct).ConfigureAwait(false);
        await using var staging = new ArchiveStaging();
        var copy = await CopyOutAsync(archive, staging, ct).ConfigureAwait(false);
        await change(copy).ConfigureAwait(false);
        await PutInPlaceAsync(copy, archive.Container, expected, ct).ConfigureAwait(false);
    }

    /// <summary>An entry of an archive on a location, written into a local copy that is put back through the location when
    /// the write commits.</summary>
    private async ValueTask<ILocationWrite> BeginArchiveEntryWriteAsync(LocationArchive archive, WriteMode mode, LocationEntry? expected,
                                                                       CancellationToken ct)
    {
        var container = await GetEntryAsync(archive.Container, ct).ConfigureAwait(false);
        var staging   = new ArchiveStaging();
        try
        {
            var copy  = await CopyOutAsync(archive, staging, ct).ConfigureAwait(false);
            var inner = await _shared.BeginWriteAsync(archive.Within(copy), mode, expected, ct: ct).ConfigureAwait(false);
            return new ArchiveEntryWrite(inner, token => PutInPlaceAsync(copy, archive.Container, container, token), staging);
        }
        catch
        {
            await staging.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class ArchiveEntryWrite(ILocationWrite inner, Func<CancellationToken, ValueTask> putBack, ArchiveStaging staging) : ILocationWrite
    {
        public Stream Content => inner.Content;

        public async ValueTask CommitAsync(CancellationToken ct)
        {
            await inner.CommitAsync(ct).ConfigureAwait(false);
            await putBack(ct).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            await staging.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Where an archive's entry lands in <paramref name="destination"/>, a folder of a location: under the entry's own names,
    /// the folders among them made as first needed (<paramref name="made"/> keeps those already made). An entry with a name
    /// the location cannot take — a "." or ".." climbing out among them — is left out.
    /// </summary>
    private async ValueTask<ILocationWrite?> PlaceEntryAsync(string destination, string entryName, HashSet<string> made, CancellationToken ct)
    {
        var names = entryName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0 || !names.All(name => CanName(destination, name))) return null;

        var folder = destination;
        foreach (var name in names[..^1])
        {
            folder = ChildPath(folder, name);
            if (made.Add(folder)) await CreateDirectoryAsync(folder, ct).ConfigureAwait(false);
        }
        return await BeginWriteAsync(ChildPath(folder, names[^1]), WriteMode.Replace, null, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The entries archiving <paramref name="path"/> makes — every file beneath a folder, or the file itself — read where
    /// Windows reaches it and named as its own file system names it: a location Windows reaches as a folder shows a name that
    /// folder cannot hold mapped (<see cref="ILocationNativePaths"/>), and the archive is given the location's name. A folder's
    /// files go behind its own name when <paramref name="underOwnName"/>, else at the archive's root. A file on a location
    /// Windows does not reach has no bytes here to read.
    /// </summary>
    [LocalDiskOnly("the files beneath a folder, walked where Windows reaches them")]
    private List<ArchiveWriteEntry> SourceEntries(string path, bool underOwnName)
    {
        string native, name;
        Func<string, string> named;
        if (OnLocation(path) is { } located && ArchiveOf(path) is null)
        {
            if (!TryRoute(located, out var location, out var remainder) || location.NativePaths is not { } names
                || NativePathOf(location, remainder) is not { } found)
                throw new NotSupportedException($"Archiving works on local bytes; '{path}' is on a location this machine keeps no copy of.");
            (native, name, named) = (found, LocationPath.DecodeSegment(Path.GetFileName(remainder)), names.FromNativeName);
        }
        else
            (native, name, named) = (_shared.TryResolveReal(path) ?? path, Path.GetFileName(path.TrimEnd('\\', '/')), static same => same);

        if (File.Exists(native)) return [NativeEntry(native, name)];
        if (!Directory.Exists(native)) throw new FileNotFoundException($"Nothing to compress at '{path}'.", path);
        var prefix = underOwnName && name.Length > 0 ? name + "/" : "";
        return [.. Directory.EnumerateFiles(native, "*", SearchOption.AllDirectories)
                            .Select(file => NativeEntry(file, prefix + string.Join('/', Path.GetRelativePath(native, file).Split('\\').Select(named))))];
    }

    /// <summary>One file as an entry, its bytes read where Windows reaches it.</summary>
    [LocalDiskOnly("the file's size and time, read where Windows reaches it")]
    private static ArchiveWriteEntry NativeEntry(string native, string entryPath)
    {
        var info = new FileInfo(native);
        return new()
        {
            Path        = entryPath,
            Modified    = info.LastWriteTime,
            Length      = info.Exists ? info.Length : 0,
            OpenContent = () => new FileStream(native, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
        };
    }

    /// <summary>An archive of <paramref name="entries"/> made at <paramref name="archivePath"/> — of nothing, when a folder held
    /// no file, as an empty folder's archive is.</summary>
    private async ValueTask CreateArchiveFromAsync(string archivePath, List<ArchiveWriteEntry> entries,
                                                   IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (entries.Count > 0)
        {
            await CreateArchiveAsync(archivePath, entries, progress, ct).ConfigureAwait(false);
            return;
        }
        await using var nothing = new ArchiveStaging();
        await CreateArchiveAsync(archivePath, nothing.Folder, progress, ct).ConfigureAwait(false);
    }

    /// <summary>A change inside an archive that only adding files or remaking it can make, refused as the shared file system
    /// refuses it for an archive on this PC.</summary>
    private static NotSupportedException ChangedInsideArchive(string path, string operation)
        => new($"'{path}' is inside an archive; {operation} works on files and folders, and an archive changes through AddFilesAsync or RepackageAsync.");

    /// <summary>An archive made on this disk by <paramref name="make"/>, then put at <paramref name="target"/> on a location.</summary>
    private async ValueTask MakeOnLocationAsync(string target, Func<string, ValueTask> make, CancellationToken ct)
    {
        await using var staging = new ArchiveStaging();
        var copy = staging.Holding(LocationPath.DecodeSegment(Path.GetFileName(target)));
        await make(copy).ConfigureAwait(false);
        await PutInPlaceAsync(copy, target, null, ct).ConfigureAwait(false);
    }

    // ── The archive members ──────────────────────────────────────────────────

    public ValueTask<ArchiveSummary?> DescribeArchiveAsync(string containerPath, CancellationToken ct = default)
        => ArchiveOf(containerPath) is { } archive ? _shared.DescribeArchiveAsync(archive.ReadPath, ct)
         : TryRoute(containerPath, out _, out _)  ? ValueTask.FromResult<ArchiveSummary?>(null)   // a remote file keeps no bytes here
         : _shared.DescribeArchiveAsync(containerPath, ct);

    public async ValueTask ExtractAllAsync(string containerPath, string destinationDir,
                                           IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        var source = ArchiveSource(containerPath);
        if (OnLocation(destinationDir) is not { } destination)
        {
            await _shared.ExtractAllAsync(source, destinationDir, progress, ct).ConfigureAwait(false);
            return;
        }

        // Written through the location entry by entry, so each lands under the archive's own name — one a Windows folder
        // could not hold among them.
        await CreateDirectoryAsync(destination, ct).ConfigureAwait(false);
        var made = new HashSet<string>(StringComparer.Ordinal) { destination };
        await _shared.ExtractAllAsync(source, (name, token) => PlaceEntryAsync(destination, name, made, token), progress, ct).ConfigureAwait(false);
    }

    public ValueTask ExtractAllAsync(string containerPath, ArchiveEntryPlacement place,
                                     IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        => _shared.ExtractAllAsync(ArchiveSource(containerPath), place, progress, ct);

    public async ValueTask CreateArchiveAsync(string archivePath, string sourceDir,
                                              IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        if (IsOnLocation(sourceDir) && ArchiveOf(sourceDir) is null)
        {
            var entries = await Task.Run(() => SourceEntries(sourceDir, underOwnName: false), ct).ConfigureAwait(false);
            await CreateArchiveFromAsync(archivePath, entries, progress, ct).ConfigureAwait(false);
            return;
        }

        var source = ArchiveSource(sourceDir);
        if (OnLocation(archivePath) is { } target)
            await MakeOnLocationAsync(target, copy => _shared.CreateArchiveAsync(copy, source, progress, ct), ct).ConfigureAwait(false);
        else
            await _shared.CreateArchiveAsync(archivePath, source, progress, ct).ConfigureAwait(false);
    }

    public async ValueTask CreateArchiveAsync(string archivePath, IReadOnlyList<string> sourcePaths,
                                              IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        if (sourcePaths.Any(path => IsOnLocation(path) && ArchiveOf(path) is null))
        {
            var entries = await Task.Run(() => sourcePaths.SelectMany(path => SourceEntries(path, underOwnName: true)).ToList(), ct).ConfigureAwait(false);
            await CreateArchiveFromAsync(archivePath, entries, progress, ct).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<string> sources = [.. sourcePaths.Select(path => ArchiveSource(path))];
        if (OnLocation(archivePath) is { } target)
            await MakeOnLocationAsync(target, copy => _shared.CreateArchiveAsync(copy, sources, progress, ct), ct).ConfigureAwait(false);
        else
            await _shared.CreateArchiveAsync(archivePath, sources, progress, ct).ConfigureAwait(false);
    }

    public async ValueTask CreateArchiveAsync(string archivePath, IReadOnlyList<ArchiveWriteEntry> entries,
                                              IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        if (OnLocation(archivePath) is { } target)
            await MakeOnLocationAsync(target, copy => _shared.CreateArchiveAsync(copy, entries, progress, ct), ct).ConfigureAwait(false);
        else
            await _shared.CreateArchiveAsync(archivePath, entries, progress, ct).ConfigureAwait(false);
    }

    public async ValueTask RepackageAsync(string sourcePath, string targetPath, ArchiveWriteOptions? options = null, CancellationToken ct = default)
    {
        var source = ArchiveSource(sourcePath);
        if (OnLocation(targetPath) is not { } target)
        {
            await _shared.RepackageAsync(source, targetPath, options, ct).ConfigureAwait(false);
            return;
        }

        // Remade over itself, the archive is read in full before anything is put back, and a change made since is not overwritten.
        var expected = await GetEntryAsync(target, ct).ConfigureAwait(false);
        await using var staging = new ArchiveStaging();
        var copy = staging.Holding(LocationPath.DecodeSegment(Path.GetFileName(target)));
        await _shared.RepackageAsync(source, copy, options, ct).ConfigureAwait(false);
        await PutInPlaceAsync(copy, target, expected, ct).ConfigureAwait(false);
    }

    public async ValueTask AddFilesAsync(string containerPath, IReadOnlyList<(string SourcePath, string EntryName)> files, CancellationToken ct = default)
    {
        IReadOnlyList<(string, string)> added = [.. files.Select(file => (ArchiveSource(file.SourcePath), file.EntryName))];
        if (ArchiveOf(containerPath) is { } archive)
            await RewriteArchiveAsync(archive, copy => _shared.AddFilesAsync(archive.Within(copy), added, ct), ct).ConfigureAwait(false);
        else if (OnLocation(containerPath) is not null)
            throw new NotSupportedException($"AddFilesAsync works on local bytes; '{containerPath}' is on a location this machine keeps no copy of.");
        else
            await _shared.AddFilesAsync(containerPath, added, ct).ConfigureAwait(false);
    }
}
