using System.IO;
using System.Net.Http;
using Nexaflow.IO.Common.Downloads;
using Nexaflow.Providers.Common;
using System.IO.Compression;

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

    /// <param name="adoptFrom">Folders where earlier versions kept model files under their bare file names.</param>
    /// <param name="downloadsAllowed">False keeps the store to what is already on disk or can be adopted.</param>
    public ModelAssetStore(string root, IReadOnlyList<string> adoptFrom, HttpClient? http = null, bool downloadsAllowed = true)
    {
        _root             = root;
        _adoptFrom        = adoptFrom;
        _http             = http ?? SharedHttp;
        _downloadsAllowed = downloadsAllowed;
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

    public async Task<IReadOnlyList<string>> EnsureAllAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress = null,
                                                            CancellationToken ct = default)
    {
        var missing = assets.Where(a => Locate(a) is null).Distinct().ToList();
        var loose   = missing.Where(a => a.Archive is null).ToList();
        var packed  = missing.Where(a => a.Archive is not null).GroupBy(a => a.Archive!.ArchiveSha256).ToList();

        var tally = new ProgressTally(loose.Sum(a => a.Size) + packed.Sum(g => g.First().Archive!.ArchiveSize), progress);
        foreach (var asset in loose)
            await EnsureLooseAsync(asset, tally.Next(asset.Size), ct);
        foreach (var archive in packed)
            await EnsurePackedAsync([.. archive], tally.Next(archive.First().Archive!.ArchiveSize), ct);

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
        var archivePath = Path.Combine(_root, ".downloads", archive.ArchiveSha256 + ".zip");
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

    /// <summary>One progress bar over several downloads: each reports its own bytes, counted after those before it.</summary>
    private sealed class ProgressTally(long total, IProgress<AssetProgress>? progress)
    {
        private long _before;

        public IProgress<DownloadProgress>? Next(long size)
        {
            if (progress is null) return null;
            long before = _before;
            _before += size;
            return new Progress<DownloadProgress>(p => progress.Report(new AssetProgress(before + p.Received, total)));
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
