using System.IO;
using System.Net.Http;
using Nexaflow.IO.Common.Downloads;
using Nexaflow.Providers.Common;
using System.IO.Compression;
using Nexaflow.IO.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// The files local models need, under one root: <c>%LOCALAPPDATA%\Smile\nexaflow\ai\</c> — not the roaming profile,
/// which would copy gigabytes of weights to every machine a user signs in to. <c>NEXAFLOW_AI_DIR</c> moves it (tests,
/// a second drive).
/// <para>
/// A file is handed out only once verified: its size and SHA-256 match the catalog, recorded beside it in a
/// <c>.verified</c> file so a large model is not hashed again at every start. A missing file is first looked for in
/// the <see cref="_adoptFrom"/> folders where earlier versions kept models, and moved into place if it is the same
/// file; otherwise it is downloaded, resuming any download an earlier attempt left.
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
    private readonly Func<string, long, SaveRoom> _weigh;

    /// <summary>The room each fetch under way still needs, by what it fetches — a file, or an archive with the entries kept
    /// from it — so fetches running side by side are weighed together, and a file two of them want counts once.</summary>
    private readonly Dictionary<string, Claim> _claims = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="adoptFrom">Folders where earlier versions kept model files under their bare file names.</param>
    /// <param name="downloadsAllowed">False keeps the store to what is already on disk or can be adopted.</param>
    /// <param name="adoptFrom">Folders where earlier versions kept model files under their bare file names.</param>
    /// <param name="downloadsAllowed">False keeps the store to what is already on disk or can be adopted.</param>
    /// <param name="weigh">What writing a number of bytes under a path comes to on its drive; <see cref="SaveRoom.For"/> unless a
    /// test stands in for the drive.</param>
    public ModelAssetStore(string root, IReadOnlyList<string> adoptFrom, HttpClient? http = null, bool downloadsAllowed = true,
                           Func<string, long, SaveRoom>? weigh = null)
    {
        _root             = root;
        _adoptFrom        = adoptFrom;
        _http             = http ?? SharedHttp;
        _downloadsAllowed = downloadsAllowed;
        _weigh            = weigh ?? ((path, bytes) => SaveRoom.For(path, bytes));
    }

    /// <summary><c>NEXAFLOW_AI_DIR</c>, or <c>%LOCALAPPDATA%\Smile\nexaflow\ai</c>.</summary>
    public static string DefaultRoot()
        => Environment.GetEnvironmentVariable("NEXAFLOW_AI_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow", "ai");

    public string? Locate(ModelAsset asset)
    {
        var path = PathOf(asset);
        return IsVerified(path, asset) ? path : null;
    }

    public async Task<string> EnsureAsync(ModelAsset asset, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
        => (await EnsureAllAsync([asset], progress, ct))[0];

    /// <summary>
    /// Makes every one of <paramref name="assets"/> here — adopted, resumed or downloaded, then verified — and returns their
    /// paths. It runs on the thread pool whoever asks: a download is minutes of reading, writing and hashing in small pieces,
    /// and asked from the UI thread every piece would otherwise continue there. <paramref name="progress"/> is told on the
    /// thread the work is on; whoever shows it moves it to the UI.
    /// </summary>
    public Task<IReadOnlyList<string>> EnsureAllAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress = null,
                                                      CancellationToken ct = default)
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

    /// <summary>The assets not here, each once.</summary>
    private List<ModelAsset> Missing(IEnumerable<ModelAsset> assets)
        => [.. assets.Where(a => Locate(a) is null).DistinctBy(a => a.RelativePath, StringComparer.Ordinal)];

    /// <summary>What fetching <paramref name="missing"/> claims of the drive: one claim for each file published on its own, and one
    /// for each archive with the entries kept from it, each counting what an interrupted download of it already wrote.</summary>
    private List<(string Key, Claim Claim)> Claims(IReadOnlyList<ModelAsset> missing)
    {
        var claims = missing.Where(a => a.Archive is null)
                            .Select(a => (LooseKey(a), new Claim(a.Size, PartialLength(PathOf(a)))))
                            .ToList();
        foreach (var entries in missing.Where(a => a.Archive is not null).GroupBy(a => a.Archive!.ArchiveSha256, StringComparer.OrdinalIgnoreCase))
        {
            var archive = entries.First().Archive!;
            claims.Add((ArchiveKey(entries.Key), new Claim(archive.ArchiveSize + entries.Sum(e => e.Size), PartialLength(ArchivePath(archive)))));
        }
        return claims;
    }

    /// <summary>The room <paramref name="claims"/> take beside every fetch under way — a claim one of them holds already counted
    /// once. Called holding <see cref="_claims"/>.</summary>
    private ModelRoom Weigh(IReadOnlyList<(string Key, Claim Claim)> claims)
    {
        long bytes = _claims.Values.Sum(c => c.Left) + claims.Where(c => !_claims.ContainsKey(c.Key)).Sum(c => c.Claim.Left);
        return ModelRoom.Of(_weigh(Path.Combine(_root, ".downloads"), bytes));
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

    private static string LooseKey(ModelAsset asset) => "file:" + asset.RelativePath;

    private static string ArchiveKey(string archiveSha256) => "zip:" + archiveSha256;

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

    /// <summary>Fetches what is missing of <paramref name="assets"/> once the drive is known to have room for it, holding that
    /// room against other fetches until it ends.</summary>
    /// <exception cref="ModelRoomException">The drive has too little room left; nothing was fetched.</exception>
    private async Task<IReadOnlyList<string>> FetchMissingAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress, CancellationToken ct)
    {
        var missing = Missing(assets);
        var loose   = missing.Where(a => a.Archive is null).ToList();
        var packed  = missing.Where(a => a.Archive is not null).GroupBy(a => a.Archive!.ArchiveSha256, StringComparer.OrdinalIgnoreCase).ToList();

        var held = Take(Claims(missing));
        try
        {
            var tally = new ProgressTally(loose.Sum(a => a.Size) + packed.Sum(g => g.First().Archive!.ArchiveSize), progress);
            foreach (var asset in loose)
                await EnsureLooseAsync(asset, tally.Next(asset.Size, held[LooseKey(asset)]), ct);
            foreach (var archive in packed)
                await EnsurePackedAsync([.. archive], tally.Next(archive.First().Archive!.ArchiveSize, held[ArchiveKey(archive.Key)]), ct);
        }
        finally { Release(held); }

        return [.. assets.Select(a => Locate(a) ?? throw new InvalidDataException($"{a.RelativePath} did not arrive."))];
    }

    /// <summary>A file published on its own: found, adopted from an earlier version's folder, or downloaded.</summary>
    private async Task EnsureLooseAsync(ModelAsset asset, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var path = PathOf(asset);
        var gate = LockFor(path);
        await gate.WaitAsync(ct);
        try
        {
            if (IsVerified(path, asset)) return;

            if (!await AdoptAsync(asset, path, ct))
            {
                RefuseWhenDownloadsAreOff(path);
                await VerifiedDownload.DownloadAsync(_http, asset.Source, path, asset.Size, asset.Sha256, progress, ct);
            }

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
                        || !string.Equals(await Nexaflow.IO.Common.Hashing.Sha256FileAsync(partial, ct), asset.Sha256, StringComparison.OrdinalIgnoreCase))
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
            throw new InvalidOperationException($"{Path.GetFileName(path)} is not on this machine, and downloads are off in this run.");
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
            foreach (var file in new[] { path, VerifiedMark(path), path + ".partial" })
                if (File.Exists(file)) File.Delete(file);
        }
        finally { gate.Release(); }
        Changed?.Invoke(this, asset);
    }

    /// <summary>Moves a file an earlier version downloaded into place, when it is the same file.</summary>
    private async Task<bool> AdoptAsync(ModelAsset asset, string path, CancellationToken ct)
    {
        var name = Path.GetFileName(asset.RelativePath);
        foreach (var dir in _adoptFrom)
        {
            var old = Path.Combine(dir, name);
            if (!File.Exists(old) || new FileInfo(old).Length != asset.Size) continue;
            if (!string.Equals(await Nexaflow.IO.Common.Hashing.Sha256FileAsync(old, ct), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Move(old, path, overwrite: true);
            return true;
        }
        return false;
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
