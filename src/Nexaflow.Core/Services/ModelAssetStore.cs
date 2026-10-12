using System.IO;
using System.Net.Http;
using Nexaflow.IO.Common.Downloads;
using Nexaflow.Providers.Common;

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
    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="adoptFrom">Folders where earlier versions kept model files under their bare file names.</param>
    public ModelAssetStore(string root, IReadOnlyList<string> adoptFrom, HttpClient? http = null)
    {
        _root      = root;
        _adoptFrom = adoptFrom;
        _http      = http ?? SharedHttp;
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
    {
        var path = PathOf(asset);
        var gate = LockFor(path);
        await gate.WaitAsync(ct);
        try
        {
            if (IsVerified(path, asset)) return path;

            if (!await AdoptAsync(asset, path, ct))
                await VerifiedDownload.DownloadAsync(_http, asset.Source, path, asset.Size, asset.Sha256,
                    progress is null ? null : new Progress<DownloadProgress>(p => progress.Report(new AssetProgress(p.Received, p.Total))),
                    ct);

            await File.WriteAllTextAsync(VerifiedMark(path), asset.Sha256, ct);
            return path;
        }
        finally { gate.Release(); }
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
