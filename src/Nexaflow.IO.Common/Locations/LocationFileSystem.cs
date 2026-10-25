using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// One workspace runtime's file system: the shared <see cref="VirtualFileSystem"/> for real, archive and mounted
/// paths, plus the remote locations this runtime has connected. A <c>::{connectionId}\…</c> path routes to its
/// connection's session; every other path passes to the shared file system unchanged.
/// <para>
/// Sessions are per runtime, so this — not the process-wide instance — is what a feature is given. The sync IO
/// members serve local paths only and throw <see cref="LocationRequiresAsyncException"/> on a remote one; the async
/// members serve both. A remote file is not browsable as an archive: archive work runs over local bytes, so the
/// archive members refuse a remote path with <see cref="NotSupportedException"/> and
/// <see cref="IsContainerAsync"/> answers false for one. Thread-safe.
/// </para>
/// </summary>
public sealed class LocationFileSystem : IVirtualFileSystem
{
    private readonly IVirtualFileSystem _shared;

    // Swapped wholesale, as the shared mount table is: every call reads it and connections change rarely.
    private LocationMount[] _locations = [];

    public LocationFileSystem(IVirtualFileSystem shared) => _shared = shared;

    // ── Locations ───────────────────────────────────────────────────────────

    /// <summary>Every connected location.</summary>
    public IReadOnlyList<LocationMount> Locations => Volatile.Read(ref _locations);

    /// <summary>Raised when a location is connected or let go. May fire on any thread.</summary>
    public event Action? LocationsChanged;

    /// <summary>Routes <c>::{id}</c> to the location, replacing one with the same id. Idempotent. Throws
    /// <see cref="ArgumentException"/> if the id is not path-segment-safe or already names a shared mount.</summary>
    public void RegisterLocation(LocationMount location)
    {
        if (!LocationPath.IsSegmentSafeId(location.Id))
            throw new ArgumentException(
                $"Location id '{location.Id}' must be non-empty and free of \\ / : — it forms a single path segment.",
                nameof(location));
        if (_shared.Mounts.Any(m => IdEquals(m.Id, location.Id)))
            throw new ArgumentException($"Location id '{location.Id}' already names a mount.", nameof(location));

        while (true)
        {
            var current = Volatile.Read(ref _locations);
            if (current.Contains(location)) return;
            var next = current.Where(l => !IdEquals(l.Id, location.Id)).Append(location).ToArray();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _locations, next, current), current)) break;
        }
        LocationsChanged?.Invoke();
    }

    /// <summary>Stops routing <c>::{id}</c>, if it was. The session itself belongs to its source.</summary>
    public void UnregisterLocation(string id)
    {
        while (true)
        {
            var current = Volatile.Read(ref _locations);
            if (!current.Any(l => IdEquals(l.Id, id))) return;
            var next = current.Where(l => !IdEquals(l.Id, id)).ToArray();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _locations, next, current), current)) break;
        }
        LocationsChanged?.Invoke();
    }

    private static bool IdEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the path lies on a connected location: its mount, and what follows <c>::{id}\</c>.</summary>
    private bool TryRoute(string path, [NotNullWhen(true)] out LocationMount? location, out string remainder)
    {
        location  = null;
        remainder = string.Empty;
        if (string.IsNullOrEmpty(path) || !path.StartsWith(VirtualMount.Prefix, StringComparison.Ordinal)) return false;

        var body = path.AsSpan(VirtualMount.Prefix.Length);
        int sep  = body.IndexOfAny('\\', '/');
        var id   = sep < 0 ? body : body[..sep];
        foreach (var l in Volatile.Read(ref _locations))
        {
            if (!id.Equals(l.Id, StringComparison.OrdinalIgnoreCase)) continue;
            location  = l;
            remainder = sep < 0 ? string.Empty : body[(sep + 1)..].Trim(['\\', '/']).ToString();
            return true;
        }
        return false;
    }

    /// <summary>The path, for a sync member — which may not wait on the network, so a remote path is refused.</summary>
    private string Local(string path, [CallerMemberName] string member = "")
        => TryRoute(path, out _, out _) ? throw new LocationRequiresAsyncException(path, member) : path;

    /// <summary>The path, for archive work — which runs over local bytes, so a remote path is refused.</summary>
    private string ArchiveLocal(string path, [CallerMemberName] string member = "")
        => TryRoute(path, out _, out _)
            ? throw new NotSupportedException($"{member} works on local bytes; '{path}' is on a remote location.")
            : path;

    private static string VirtualPathOf(LocationMount location, string providerPath)
    {
        var remainder = LocationPath.ToRemainder(providerPath);
        return remainder.Length == 0 ? location.Root : location.Root + "\\" + remainder;
    }

    /// <summary>A provider's entry as the VFS lists it: its name as a path segment.</summary>
    private static LocationEntry AsSegment(LocationEntry entry)
    {
        var segment = LocationPath.EncodeSegment(entry.Name);
        return ReferenceEquals(segment, entry.Name) ? entry : entry with { Name = segment };
    }

    // ── Sync: local paths only ──────────────────────────────────────────────

    public bool Exists(string path) => _shared.Exists(Local(path));

    public bool IsDirectory(string path) => _shared.IsDirectory(Local(path));

    public bool IsContainer(string path) => _shared.IsContainer(Local(path));

    public bool IsContainerName(string fileName) => _shared.IsContainerName(fileName);

    public long GetLength(string path) => _shared.GetLength(Local(path));

    public VirtualEntry? GetEntryInfo(string path) => _shared.GetEntryInfo(Local(path));

    public IReadOnlyList<VirtualEntry> EnumerateEntries(string path) => _shared.EnumerateEntries(Local(path));

    public Stream OpenRead(string path) => _shared.OpenRead(Local(path));

    public Stream OpenRead(string path, int bufferSize, bool useAsync) => _shared.OpenRead(Local(path), bufferSize, useAsync);

    public Stream OpenReadWrite(string path) => _shared.OpenReadWrite(Local(path));

    public byte[] ReadAllBytes(string path) => _shared.ReadAllBytes(Local(path));

    public string ReadAllText(string path, Encoding? encoding = null) => _shared.ReadAllText(Local(path), encoding);

    public void WriteAllBytes(string path, byte[] bytes) => _shared.WriteAllBytes(Local(path), bytes);

    public void WriteAllText(string path, string contents, Encoding? encoding = null)
        => _shared.WriteAllText(Local(path), contents, encoding);

    public void Replace(string path, byte[] newContent) => _shared.Replace(Local(path), newContent);

    public (string RealContainer, string? Inner) SplitOutermostContainer(string path)
        => TryRoute(path, out _, out _) ? (path, null) : _shared.SplitOutermostContainer(path);

    public ArchiveSummary? DescribeArchive(string containerPath) => _shared.DescribeArchive(Local(containerPath));

    public void ExtractAll(string containerPath, string destinationDir,
                           IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        => _shared.ExtractAll(Local(containerPath), Local(destinationDir), progress, ct);

    public void CreateArchive(string archivePath, string sourceDir,
                              IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        => _shared.CreateArchive(Local(archivePath), Local(sourceDir), progress, ct);

    public void CreateArchive(string archivePath, IReadOnlyList<string> sourcePaths,
                              IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        foreach (var source in sourcePaths) Local(source);
        _shared.CreateArchive(Local(archivePath), sourcePaths, progress, ct);
    }

    public bool CanCreate(string archiveFileName) => _shared.CanCreate(archiveFileName);

    public void Repackage(string sourcePath, string targetPath, ArchiveWriteOptions? options = null)
        => _shared.Repackage(Local(sourcePath), Local(targetPath), options);

    public void AddFiles(string containerPath, IReadOnlyList<(string SourcePath, string EntryName)> files)
    {
        foreach (var (source, _) in files) Local(source);
        _shared.AddFiles(Local(containerPath), files);
    }

    public void RegisterHandler(IArchiveHandler handler) => _shared.RegisterHandler(handler);

    // ── Mounts ──────────────────────────────────────────────────────────────

    public IReadOnlyList<VirtualMount> Mounts => _shared.Mounts;

    /// <summary>Adds the mount to the shared table. Throws <see cref="ArgumentException"/> if a location of this
    /// runtime already holds the id: inside a runtime, <c>::{id}</c> names exactly one place.</summary>
    public void RegisterMount(VirtualMount mount)
    {
        if (Volatile.Read(ref _locations).Any(l => IdEquals(l.Id, mount.Id)))
            throw new ArgumentException($"Mount id '{mount.Id}' already names a connected location.", nameof(mount));
        _shared.RegisterMount(mount);
    }

    public void UnregisterMount(string id) => _shared.UnregisterMount(id);

    public event Action? MountsChanged
    {
        add    => _shared.MountsChanged += value;
        remove => _shared.MountsChanged -= value;
    }

    // ── Paths ───────────────────────────────────────────────────────────────

    public VirtualBacking GetBacking(string path)
        => TryRoute(path, out _, out _) ? VirtualBacking.Remote : _shared.GetBacking(path);

    public string? TryResolveReal(string path) => TryRoute(path, out _, out _) ? null : _shared.TryResolveReal(path);

    public string? TryToVirtual(string realPath) => _shared.TryToVirtual(realPath);

    // A ::{id}\… parent is found by its string alone, which the shared rule already does for any id.
    public string? GetParentPath(string path) => _shared.GetParentPath(path);

    public string GetDisplayName(string path)
    {
        if (!TryRoute(path, out var location, out var remainder)) return _shared.GetDisplayName(path);
        if (remainder.Length == 0) return location.Label;
        int sep = remainder.LastIndexOfAny(['\\', '/']);
        return LocationPath.DecodeSegment(sep < 0 ? remainder : remainder[(sep + 1)..]);
    }

    public IReadOnlyList<(string Label, string Path)> GetBreadcrumbs(string path)
    {
        if (!TryRoute(path, out var location, out var remainder)) return _shared.GetBreadcrumbs(path);
        var built  = location.Root;
        var crumbs = new List<(string, string)> { (location.Label, built) };
        foreach (var segment in remainder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            built += "\\" + segment;
            crumbs.Add((LocationPath.DecodeSegment(segment), built));
        }
        return crumbs;
    }

    public string GetDisplayPath(string path)
        => TryRoute(path, out var location, out var remainder)
            ? LocationPath.ToDisplayForm(location.Connection, LocationPath.ToProviderPath(remainder))
            : _shared.GetDisplayPath(path);

    public string? TryParseDisplayPath(string text)
    {
        if (LocationPath.TryParseDisplayForm(text, out var address))
            foreach (var location in Volatile.Read(ref _locations))
                if (LocationPath.Matches(address, location.Connection))
                    return VirtualPathOf(location, address.Path);
        return _shared.TryParseDisplayPath(text);
    }

    // ── Async ───────────────────────────────────────────────────────────────

    public async ValueTask<bool> ExistsAsync(string path, CancellationToken ct = default)
        => TryRoute(path, out var location, out var remainder)
            ? await RemoteEntryAsync(location, remainder, ct).ConfigureAwait(false) is not null
            : await _shared.ExistsAsync(path, ct).ConfigureAwait(false);

    public async ValueTask<bool> IsDirectoryAsync(string path, CancellationToken ct = default)
        => TryRoute(path, out var location, out var remainder)
            ? (await RemoteEntryAsync(location, remainder, ct).ConfigureAwait(false))?.IsDirectory == true
            : await _shared.IsDirectoryAsync(path, ct).ConfigureAwait(false);

    public ValueTask<bool> IsContainerAsync(string path, CancellationToken ct = default)
        => TryRoute(path, out _, out _) ? ValueTask.FromResult(false) : _shared.IsContainerAsync(path, ct);

    public ValueTask<LocationEntry?> GetEntryAsync(string path, CancellationToken ct = default)
        => TryRoute(path, out var location, out var remainder)
            ? RemoteEntryAsync(location, remainder, ct)
            : _shared.GetEntryAsync(path, ct);

    private static async ValueTask<LocationEntry?> RemoteEntryAsync(LocationMount location, string remainder, CancellationToken ct)
    {
        var session = await location.Sessions.GetSessionAsync(ct).ConfigureAwait(false);
        var entry   = await session.GetEntryAsync(LocationPath.ToProviderPath(remainder), ct).ConfigureAwait(false);
        return entry is null ? null : AsSegment(entry);
    }

    public IAsyncEnumerable<LocationEntry> EnumerateAsync(string path, CancellationToken ct = default)
        => TryRoute(path, out var location, out var remainder)
            ? EnumerateRemoteAsync(location, remainder, ct)
            : _shared.EnumerateAsync(path, ct);

    private static async IAsyncEnumerable<LocationEntry> EnumerateRemoteAsync(
        LocationMount location, string remainder, [EnumeratorCancellation] CancellationToken ct)
    {
        var session = await location.Sessions.GetSessionAsync(ct).ConfigureAwait(false);
        await foreach (var entry in session.EnumerateAsync(LocationPath.ToProviderPath(remainder), ct).ConfigureAwait(false))
            yield return AsSegment(entry);
    }

    public async ValueTask<Stream> OpenReadAsync(string path, ReadIntent intent, CancellationToken ct = default)
    {
        if (!TryRoute(path, out var location, out var remainder))
            return await _shared.OpenReadAsync(path, intent, ct).ConfigureAwait(false);
        var session = await location.Sessions.GetSessionAsync(ct).ConfigureAwait(false);
        return await session.OpenReadAsync(LocationPath.ToProviderPath(remainder), intent, ct).ConfigureAwait(false);
    }

    public async ValueTask<ILocationWrite> BeginWriteAsync(string path, WriteMode mode, LocationEntry? expected = null, CancellationToken ct = default)
    {
        if (!TryRoute(path, out var location, out var remainder))
            return await _shared.BeginWriteAsync(path, mode, expected, ct).ConfigureAwait(false);
        var session = await location.Sessions.GetSessionAsync(ct).ConfigureAwait(false);
        return await session.BeginWriteAsync(LocationPath.ToProviderPath(remainder), mode, expected, ct).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        if (!TryRoute(path, out _, out _)) return await _shared.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        await using var stream = await OpenReadAsync(path, ReadIntent.Whole, ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public async ValueTask<string> ReadAllTextAsync(string path, Encoding? encoding = null, CancellationToken ct = default)
    {
        if (!TryRoute(path, out _, out _)) return await _shared.ReadAllTextAsync(path, encoding, ct).ConfigureAwait(false);
        await using var stream = await OpenReadAsync(path, ReadIntent.Whole, ct).ConfigureAwait(false);
        // As File.ReadAllText reads: a byte-order mark wins, UTF-8 otherwise.
        using var reader = new StreamReader(stream, encoding ?? new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    public ValueTask WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
        => TryRoute(path, out _, out _) ? WriteWholeAsync(path, bytes, ct) : _shared.WriteAllBytesAsync(path, bytes, ct);

    public ValueTask WriteAllTextAsync(string path, string contents, Encoding? encoding = null, CancellationToken ct = default)
        => TryRoute(path, out _, out _)
            ? WriteWholeAsync(path, VirtualFileSystem.EncodeText(contents, encoding), ct)
            : _shared.WriteAllTextAsync(path, contents, encoding, ct);

    public ValueTask ReplaceAsync(string path, byte[] newContent, CancellationToken ct = default)
        => TryRoute(path, out _, out _) ? WriteWholeAsync(path, newContent, ct) : _shared.ReplaceAsync(path, newContent, ct);

    /// <summary>A whole-content write to a remote file: atomic where the location renames, one upload where not.</summary>
    private async ValueTask WriteWholeAsync(string path, byte[] bytes, CancellationToken ct)
    {
        await using var write = await BeginWriteAsync(path, WriteMode.Replace, null, ct).ConfigureAwait(false);
        await write.Content.WriteAsync(bytes, ct).ConfigureAwait(false);
        await write.CommitAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask MoveAsync(string from, string to, bool replace, CancellationToken ct = default)
    {
        bool fromRemote = TryRoute(from, out var source, out var sourceRest);
        bool toRemote   = TryRoute(to, out var target, out var targetRest);
        if (!fromRemote && !toRemote)
        {
            await _shared.MoveAsync(from, to, replace, ct).ConfigureAwait(false);
            return;
        }
        if (source is null || target is null || !IdEquals(source.Id, target.Id))
            throw new NotSupportedException(
                $"'{from}' and '{to}' are on different locations; moving between them copies the bytes, which is a transfer, not a rename.");

        var session = await source.Sessions.GetSessionAsync(ct).ConfigureAwait(false);
        await session.MoveAsync(LocationPath.ToProviderPath(sourceRest), LocationPath.ToProviderPath(targetRest), replace, ct)
            .ConfigureAwait(false);
    }

    public async ValueTask CopyWithinAsync(string from, string to, bool replace, CancellationToken ct = default)
    {
        bool fromRemote = TryRoute(from, out var source, out var sourceRest);
        bool toRemote   = TryRoute(to, out var target, out var targetRest);
        if (!fromRemote || !toRemote || !IdEquals(source!.Id, target!.Id)
            || !source.Capabilities.HasFlag(LocationCapabilities.ServerSideCopy))
            throw new NotSupportedException($"'{from}' and '{to}' are not on one location that copies on its server.");

        var session = await source.Sessions.GetSessionAsync(ct).ConfigureAwait(false);
        await session.CopyWithinAsync(LocationPath.ToProviderPath(sourceRest), LocationPath.ToProviderPath(targetRest), replace, ct)
            .ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(string path, bool recursive, CancellationToken ct = default)
    {
        if (!TryRoute(path, out var location, out var remainder))
        {
            await _shared.DeleteAsync(path, recursive, ct).ConfigureAwait(false);
            return;
        }
        var session = await location.Sessions.GetSessionAsync(ct).ConfigureAwait(false);
        await session.DeleteAsync(LocationPath.ToProviderPath(remainder), recursive, ct).ConfigureAwait(false);
    }

    public async ValueTask CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        if (!TryRoute(path, out var location, out var remainder))
        {
            await _shared.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
            return;
        }
        var session = await location.Sessions.GetSessionAsync(ct).ConfigureAwait(false);
        await session.CreateDirectoryAsync(LocationPath.ToProviderPath(remainder), ct).ConfigureAwait(false);
    }

    public async ValueTask<ILocationWatch?> WatchChangesAsync(string path, bool recursive, CancellationToken ct = default)
    {
        if (!TryRoute(path, out var location, out var remainder))
            return await _shared.WatchChangesAsync(path, recursive, ct).ConfigureAwait(false);
        if ((location.Capabilities & LocationCapabilities.Watch) == 0) return null;

        var session = await location.Sessions.GetSessionAsync(ct).ConfigureAwait(false);
        return session.Watch(LocationPath.ToProviderPath(remainder), recursive) is { } watch
            ? new RoutedLocationWatch(watch, providerPath => VirtualPathOf(location, providerPath))
            : null;
    }

    public LocationCapabilities GetCapabilities(string path)
        => TryRoute(path, out var location, out _) ? location.Capabilities : _shared.GetCapabilities(path);

    public ValueTask<ArchiveSummary?> DescribeArchiveAsync(string containerPath, CancellationToken ct = default)
        => TryRoute(containerPath, out _, out _)
            ? ValueTask.FromResult<ArchiveSummary?>(null)   // a remote file is never a container
            : _shared.DescribeArchiveAsync(containerPath, ct);

    public ValueTask<string> MaterializeFileAsync(string path, CancellationToken ct = default)
        => TryRoute(path, out _, out _)
            ? ValueTask.FromException<string>(new NotSupportedException($"'{path}' is on a remote location, which keeps no copy of it on this disk."))
            : _shared.MaterializeFileAsync(path, ct);

    public ValueTask ExtractAllAsync(string containerPath, string destinationDir,
                                     IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        => _shared.ExtractAllAsync(ArchiveLocal(containerPath), ArchiveLocal(destinationDir), progress, ct);

    public ValueTask CreateArchiveAsync(string archivePath, string sourceDir,
                                        IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
        => _shared.CreateArchiveAsync(ArchiveLocal(archivePath), ArchiveLocal(sourceDir), progress, ct);

    public ValueTask CreateArchiveAsync(string archivePath, IReadOnlyList<string> sourcePaths,
                                        IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        foreach (var source in sourcePaths) ArchiveLocal(source);
        return _shared.CreateArchiveAsync(ArchiveLocal(archivePath), sourcePaths, progress, ct);
    }

    public ValueTask RepackageAsync(string sourcePath, string targetPath, ArchiveWriteOptions? options = null, CancellationToken ct = default)
        => _shared.RepackageAsync(ArchiveLocal(sourcePath), ArchiveLocal(targetPath), options, ct);

    public ValueTask AddFilesAsync(string containerPath, IReadOnlyList<(string SourcePath, string EntryName)> files, CancellationToken ct = default)
    {
        foreach (var (source, _) in files) ArchiveLocal(source);
        return _shared.AddFilesAsync(ArchiveLocal(containerPath), files, ct);
    }
}
