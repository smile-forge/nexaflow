using System.Collections.Concurrent;
using System.IO.Compression;

namespace Nexaflow.AiHost.Provisioning;

/// <summary>
/// The files local models need, under one root the app names — a local, not a roaming, folder: weights are gigabytes.
/// <para>
/// A file is handed out only once verified: its size and SHA-256 match the catalog, recorded beside it in a
/// <c>.verified</c> file so a large model is not hashed again at every start. A missing file is first looked for in
/// the <see cref="_adoptFrom"/> folders where earlier versions kept files — a folder this store once held, such as an
/// earlier CUDA pack's, or the one voice models were kept in — and moved into place if it is the same file: taken on
/// the mark the store left beside it there, hashed where it has none. Only what is still missing then is weighed
/// against the drive and downloaded, resuming any download an earlier attempt left.
/// </para>
/// </summary>
public sealed class ModelAssetStore : IModelAssetStore
{
    /// <summary>Model downloads run for minutes; cancellation, not a timeout, is what ends one.</summary>
    private static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string                _root;
    private readonly IReadOnlyList<string> _adoptFrom;
    private readonly HttpClient            _http;
    private readonly bool                  _downloadsAllowed;
    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, long?> _freeOn;

    /// <summary>The room each fetch under way still needs, by what it fetches — a file, or an archive with the entries kept
    /// from it — so fetches running side by side are weighed together, and a file two of them want counts once.</summary>
    private readonly Dictionary<string, Claim> _claims = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The files whose verified mark has been read, by path, with the size and write time they had then: a file is
    /// verified once per version, and every <see cref="Locate"/> after that costs one look at its attributes — so one deleted
    /// or replaced behind the store's back is still never located.</summary>
    private readonly ConcurrentDictionary<string, (long Length, DateTime Written)> _verified = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="root">The folder every file is kept under.</param>
    /// <param name="adoptFrom">Where earlier versions kept model files: folders holding them under their bare file names, or earlier
    /// stores holding them as this one does.</param>
    /// <param name="downloadsAllowed">False keeps the store to what is already on disk or can be adopted.</param>
    /// <param name="freeOn">What the drive a path is on has left for this user, null when it will not say; the drive's own
    /// reading unless a test stands in for it.</param>
    public ModelAssetStore(string root, IReadOnlyList<string> adoptFrom, HttpClient? http = null, bool downloadsAllowed = true,
                           Func<string, long?>? freeOn = null)
    {
        _root             = root;
        _adoptFrom        = adoptFrom;
        _http             = http ?? SharedHttp;
        _downloadsAllowed = downloadsAllowed;
        _freeOn           = freeOn ?? FreeOn;
    }

    /// <summary>What the drive <paramref name="path"/> is on has left for this user — the quota-aware figure, not the disk's
    /// own; null for a share, which has no drive to ask, or a drive that will not say.</summary>
    private static long? FreeOn(string path)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(path)) is { Length: > 0 } root && !root.StartsWith(@"\\", StringComparison.Ordinal)
                       ? new DriveInfo(root).AvailableFreeSpace
                       : null;
        }
        catch (Exception e) when (e is ArgumentException or DriveNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The asset's path when it is in the store, complete and verified; null otherwise. The store's own: an app asks
    /// <see cref="Has"/>, and never learns a path.</summary>
    public string? Locate(ModelAsset asset)
    {
        var path = PathOf(asset);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != asset.Size) return null;
        var version = (file.Length, file.LastWriteTimeUtc);
        if (_verified.TryGetValue(path, out var seen) && seen == version) return path;
        if (!IsVerified(path, asset)) return null;
        _verified[path] = version;
        return path;
    }

    public bool Has(ModelAsset asset) => Locate(asset) is not null;

    public Task EnsureAsync(ModelAsset asset, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
        => EnsureAllAsync([asset], progress, ct);

    /// <summary>
    /// Makes every one of <paramref name="assets"/> here — adopted, resumed or downloaded, then verified. It runs on the thread
    /// pool whoever asks: a download is minutes of reading, writing and hashing in small pieces, and asked from the UI thread every
    /// piece would otherwise continue there. <paramref name="progress"/> is told on the thread the work is on; whoever shows it
    /// moves it to the UI.
    /// </summary>
    public Task EnsureAllAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => FetchMissingAsync(assets, progress, ct), ct);

    /// <summary>
    /// What fetching <paramref name="assets"/> would take on the store's drive at its fullest, beside every fetch under way: each
    /// file not here, less what an interrupted download of it kept, and each archive whole beside the entries unpacked from it —
    /// it is staged in <c>.downloads</c> until they are out. Every fetch is refused with this when it does not fit.
    /// </summary>
    public ModelRoom RoomFor(IEnumerable<ModelAsset> assets)
    {
        var claims = Claims(Missing(assets));
        lock (_claims) return Weigh(claims);
    }

    /// <summary>What fetching <paramref name="assets"/> downloads, in bytes: each file not here, less those an earlier version's
    /// folder holds verified — they are moved in.</summary>
    public long FetchBytes(IEnumerable<ModelAsset> assets) => Missing(assets).Sum(a => a.Size);

    /// <summary>The assets not here, each once — less those an earlier version's folder holds verified, which are moved in
    /// rather than fetched.</summary>
    private List<ModelAsset> Missing(IEnumerable<ModelAsset> assets)
        => [.. assets.Where(a => Locate(a) is null && !EarlierCopies(a).Any(old => IsVerified(old, a)))
                     .DistinctBy(a => a.RelativePath, StringComparer.Ordinal)];

    /// <summary>What fetching <paramref name="missing"/> claims of the drive (<see cref="StoreClaims"/>), each claim counting what an
    /// interrupted download of it already wrote.</summary>
    private List<(string Key, Claim Claim)> Claims(IReadOnlyList<ModelAsset> missing)
        => [.. StoreClaims.Of(missing, a => PartialLength(PathOf(a)), a => PartialLength(ArchivePath(a)))
                          .Select(c => (c.Key, new Claim(c.Bytes, c.Written)))];

    /// <summary>The room <paramref name="claims"/> take beside every fetch under way — a claim one of them holds already counted
    /// once. Called holding <see cref="_claims"/>.</summary>
    private ModelRoom Weigh(IReadOnlyList<(string Key, Claim Claim)> claims)
    {
        long bytes = StoreClaims.Weigh([.. claims.Select(c => new StoreClaims.Claim(c.Key, c.Claim.Left, 0))], UnderWay());
        var (free, drive) = Drive();
        return new ModelRoom(bytes, free, drive);
    }

    /// <summary>What each fetch under way still needs, by claim. Called holding <see cref="_claims"/>.</summary>
    private Dictionary<string, long> UnderWay() => _claims.ToDictionary(c => c.Key, c => c.Value.Left, StringComparer.OrdinalIgnoreCase);

    /// <summary>What the store's drive has left, and the drive as it is named to a person.</summary>
    private (long? Free, string Drive) Drive()
    {
        var staging = Path.Combine(_root, ".downloads");
        return (_freeOn(staging), Path.GetPathRoot(Path.GetFullPath(staging)) ?? staging);
    }

    /// <summary>
    /// The store as it stands for <paramref name="assets"/>: for each, whether it is here verified, what an interrupted download of it
    /// kept, and whether an earlier version's copy of it would be moved in rather than fetched; what each interrupted archive download
    /// kept; what the fetches under way still need; and the drive's room. What an app weighs and shows the store by, without a path.
    /// </summary>
    public StoreReading Read(IEnumerable<ModelAsset> assets)
    {
        var files = assets.DistinctBy(a => a.RelativePath, StringComparer.Ordinal)
                          .Select(a => new FileReading(a.RelativePath, Locate(a) is not null, a.Archive is null ? PartialLength(PathOf(a)) : 0,
                                                       Locate(a) is null && EarlierCopies(a).Any(old => IsVerified(old, a))))
                          .ToList();
        var archives = assets.Where(a => a.Archive is not null).Select(a => a.Archive!).DistinctBy(a => a.ArchiveSha256, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(a => a.ArchiveSha256, a => PartialLength(ArchivePath(a)), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, long> underWay;
        lock (_claims) underWay = UnderWay();
        var (free, drive) = Drive();
        return new StoreReading(files, archives, underWay, free, drive);
    }

    /// <summary>Holds <paramref name="claims"/> against the drive's room for a fetch — sharing any another fetch holds — or refuses
    /// the fetch when they do not fit beside the rest.</summary>
    /// <exception cref="ModelRoomException">They do not fit.</exception>
    private Dictionary<string, Claim> Take(IReadOnlyList<(string Key, Claim Claim)> claims)
    {
        lock (_claims)
        {
            var room = Weigh(claims);
            if (!room.Fits) throw new ModelRoomException(room);
            var held = new Dictionary<string, Claim>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, claim) in claims)
            {
                if (!_claims.TryGetValue(key, out var holding)) _claims[key] = holding = claim;
                holding.Holders++;
                held[key] = holding;
            }
            return held;
        }
    }

    private void Release(Dictionary<string, Claim> held)
    {
        lock (_claims)
            foreach (var (key, claim) in held)
                if (--claim.Holders == 0) _claims.Remove(key);
    }

    private string ArchivePath(ArchiveEntry archive) => Path.Combine(_root, ".downloads", archive.ArchiveSha256 + ".zip");

    /// <summary>What an interrupted download to <paramref name="path"/> kept, which the next one resumes from.</summary>
    private static long PartialLength(string path) => new FileInfo(path + ".partial") is { Exists: true } partial ? partial.Length : 0;

    /// <summary>Room one fetch holds on the drive: what it will have written there at its fullest, and how much of that is written
    /// already — the drive's free space has paid for that part.</summary>
    private sealed class Claim(long bytes, long written)
    {
        private long _written = written;

        /// <summary>The fetches holding it; it is let go when the last ends.</summary>
        public int Holders;

        public long Left => Math.Max(0, bytes - Volatile.Read(ref _written));

        /// <summary>Takes how much of it is written, as its download reports.</summary>
        public void Wrote(long received) => Volatile.Write(ref _written, received);
    }

    /// <summary>Moves into place each of <paramref name="assets"/> not here that an earlier version's folder holds — before
    /// anything is weighed or fetched, so a set that shares most of its files with an earlier one downloads only the rest.</summary>
    /// <summary>Moves into the store every one of <paramref name="assets"/> it lacks that an earlier folder holds as the same file, and
    /// fetches nothing: what is not there is fetched when it is first wanted.</summary>
    public async Task AdoptEarlierAsync(IReadOnlyList<ModelAsset> assets, CancellationToken ct)
    {
        foreach (var asset in assets.DistinctBy(a => a.RelativePath, StringComparer.Ordinal))
        {
            if (Locate(asset) is not null) continue;
            var path = PathOf(asset);
            var gate = LockFor(path);
            await gate.WaitAsync(ct);
            try
            {
                if (!IsVerified(path, asset) && await AdoptAsync(asset, path, ct))
                    await MarkVerifiedAsync(path, asset, ct);
            }
            finally { gate.Release(); }
        }
    }

    /// <summary>Fetches what is missing of <paramref name="assets"/> once the drive is known to have room for it, holding that
    /// room against other fetches until it ends.</summary>
    /// <exception cref="ModelRoomException">The drive has too little room left; nothing was fetched.</exception>
    private async Task FetchMissingAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress, CancellationToken ct)
    {
        await AdoptEarlierAsync(assets, ct);
        var missing = Missing(assets);
        var loose   = missing.Where(a => a.Archive is null).ToList();
        var packed  = missing.Where(a => a.Archive is not null).GroupBy(a => a.Archive!.ArchiveSha256, StringComparer.OrdinalIgnoreCase).ToList();

        var held = Take(Claims(missing));
        try
        {
            var tally = new ProgressTally(loose.Sum(a => a.Size) + packed.Sum(g => g.First().Archive!.ArchiveSize), progress);
            foreach (var asset in loose)
                await EnsureLooseAsync(asset, tally.Next(asset.Size, held[StoreClaims.KeyOf(asset)]), ct);
            foreach (var archive in packed)
                await EnsurePackedAsync([.. archive], tally.Next(archive.First().Archive!.ArchiveSize, held[StoreClaims.KeyOf(archive.First().Archive!)]), ct);
        }
        finally { Release(held); }

        if (assets.FirstOrDefault(a => Locate(a) is null) is { } absent) throw new InvalidDataException($"{absent.RelativePath} did not arrive.");
    }

    /// <summary>A file published on its own: found, or downloaded — an earlier version's copy was moved in before.</summary>
    private async Task EnsureLooseAsync(ModelAsset asset, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var path = PathOf(asset);
        var gate = LockFor(path);
        await gate.WaitAsync(ct);
        try
        {
            if (IsVerified(path, asset)) return;

            RefuseWhenDownloadsAreOff(path);
            await VerifiedDownload.DownloadAsync(_http, asset.Source, path, asset.Size, asset.Sha256, progress, ct);
            await MarkVerifiedAsync(path, asset, ct);
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Files packed in one archive: the archive is downloaded (resuming, verified whole) into <c>.downloads</c>, each wanted
    /// entry is extracted and checked against what the catalog says it is, and the archive is deleted — its other
    /// entries are not wanted, and it would cost its size twice.
    /// </summary>
    private async Task EnsurePackedAsync(IReadOnlyList<ModelAsset> entries, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var archive     = entries[0].Archive!;
        var archivePath = ArchivePath(archive);
        var gate = LockFor(archivePath);
        await gate.WaitAsync(ct);
        try
        {
            var wanted = entries.Where(e => !IsVerified(PathOf(e), e)).ToList();
            if (wanted.Count == 0) return;

            RefuseWhenDownloadsAreOff(archivePath);
            await VerifiedDownload.DownloadAsync(_http, entries[0].Source, archivePath, archive.ArchiveSize, archive.ArchiveSha256, progress, ct);

            using (var zip = ZipFile.OpenRead(archivePath))
            {
                foreach (var asset in wanted)
                {
                    var entry = zip.GetEntry(asset.Archive!.Entry)
                        ?? throw new InvalidDataException($"{Path.GetFileName(archivePath)} has no entry {asset.Archive.Entry}.");
                    var path    = PathOf(asset);
                    var partial = path + ".partial";
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    entry.ExtractToFile(partial, overwrite: true);

                    if (new FileInfo(partial).Length != asset.Size
                        || !string.Equals(await FileHash.Sha256Async(partial, ct), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(partial);
                        throw new InvalidDataException($"{asset.Archive.Entry} in {Path.GetFileName(archivePath)} is not the file the catalog describes.");
                    }

                    File.Move(partial, path, overwrite: true);
                    await MarkVerifiedAsync(path, asset, ct);
                }
            }
            File.Delete(archivePath);
        }
        finally { gate.Release(); }
    }

    private void RefuseWhenDownloadsAreOff(string path)
    {
        if (!_downloadsAllowed)
            throw new ModelDownloadsOffException(Path.GetFileName(path));
    }

    private async Task MarkVerifiedAsync(string path, ModelAsset asset, CancellationToken ct)
    {
        await File.WriteAllTextAsync(VerifiedMark(path), asset.Sha256, ct);
        Changed?.Invoke(this, asset);
    }

    /// <summary>One progress bar over several downloads: each reports its own bytes, counted after those before it, and what it has
    /// written to the room its fetch holds. Each report is passed on as it is made, in order — the caller's own
    /// <see cref="Progress{T}"/> moves it to the caller's thread; a second hop here would let reports overtake each other, and the
    /// last arrive after the fetch had ended.</summary>
    private sealed class ProgressTally(long total, IProgress<AssetProgress>? progress)
    {
        private long _before;

        public IProgress<DownloadProgress> Next(long size, Claim claim)
        {
            long before = _before;
            _before += size;
            return new Forward(p =>
            {
                claim.Wrote(p.Received);
                progress?.Report(new AssetProgress(before + p.Received, total));
            });
        }

        private sealed class Forward(Action<DownloadProgress> report) : IProgress<DownloadProgress>
        {
            public void Report(DownloadProgress value) => report(value);
        }
    }

    public event EventHandler<ModelAsset>? Changed;

    /// <summary>Deletes an asset — the file, its verified mark, and any partial download — to free its space.</summary>
    public async Task RemoveAsync(ModelAsset asset, CancellationToken ct = default)
    {
        var path = PathOf(asset);
        var gate = LockFor(path);
        await gate.WaitAsync(ct);
        try
        {
            _verified.TryRemove(path, out _);
            foreach (var file in new[] { path, VerifiedMark(path), path + ".partial" })
                if (File.Exists(file)) File.Delete(file);
        }
        finally { gate.Release(); }
        Changed?.Invoke(this, asset);
    }

    /// <summary>Moves a file an earlier version kept into place, when it is the same file: one this store verified there is
    /// taken on its mark, any other is hashed first. Its mark goes with it.</summary>
    private async Task<bool> AdoptAsync(ModelAsset asset, string path, CancellationToken ct)
    {
        foreach (var old in EarlierCopies(asset))
        {
            if (!IsVerified(old, asset)
                && !string.Equals(await FileHash.Sha256Async(old, ct), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Move(old, path, overwrite: true);
            File.Delete(VerifiedMark(old));
            return true;
        }
        return false;
    }

    /// <summary>The files of <paramref name="asset"/>'s size where earlier versions kept it: under its bare name in a folder of them,
    /// or at its own path in an earlier store.</summary>
    private IEnumerable<string> EarlierCopies(ModelAsset asset)
    {
        var name = Path.GetFileName(asset.RelativePath);
        foreach (var dir in _adoptFrom)
            foreach (var old in new[] { Path.Combine(dir, name), StorePath.Resolve(dir, asset.RelativePath) }.Distinct(StringComparer.OrdinalIgnoreCase))
                if (File.Exists(old) && new FileInfo(old).Length == asset.Size) yield return old;
    }

    private string PathOf(ModelAsset asset)
        => Path.Combine(_root, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string VerifiedMark(string path) => path + ".verified";

    private static bool IsVerified(string path, ModelAsset asset)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != asset.Size) return false;
        try { return string.Equals(File.ReadAllText(VerifiedMark(path)).Trim(), asset.Sha256, StringComparison.OrdinalIgnoreCase); }
        catch (IOException) { return false; }
    }

    private SemaphoreSlim LockFor(string path)
    {
        lock (_locks)
            return _locks.TryGetValue(path, out var gate) ? gate : _locks[path] = new SemaphoreSlim(1, 1);
    }
}
