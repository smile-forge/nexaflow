using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// The AI host's model store as an app sees it: the host alone keeps the store, reads its files and fetches them, whichever app
/// asks; the app knows what is here from what the host last said of the store (<see cref="RefreshAsync"/>, then each change it
/// tells of), and fetches and removes files by asking it — never by a path. What a fetch would take is weighed here the same way
/// the store weighs it (<see cref="StoreClaims"/>). Read it once before anything asks what is here.
/// </summary>
public sealed class AiHostModelStore : IModelAssetStore
{
    /// <summary>How long a fetch may go without a byte before it is given up on: a download's reports come many times a second.</summary>
    private static readonly TimeSpan FetchStall = TimeSpan.FromMinutes(5);

    private readonly IAiHost _host;
    private readonly IReadOnlyDictionary<string, ModelAsset> _catalog;
    private volatile ModelStoreState _state = new([], new Dictionary<string, long>(), new Dictionary<string, long>(), null, "", false);
    private int _read;
    private volatile IReadOnlyDictionary<string, ModelFileState> _files = new Dictionary<string, ModelFileState>();

    public AiHostModelStore(IAiHost host)
    {
        _host = host;
        _catalog = HostCatalog.Models.SelectMany(m => m.Files.Concat(m.Loras.SelectMany(l => l.Files)))
                                     .Concat(HostCatalog.Tools.SelectMany(t => t.Files))
                                     .Concat(CudaRuntimePack.Files)
                                     .DistinctBy(f => f.RelativePath, StringComparer.Ordinal)
                                     .ToDictionary(f => f.RelativePath, StringComparer.Ordinal);
        host.ModelFilesChanged += (_, paths) => _ = OnChangedAsync(paths);
    }

    public event EventHandler<ModelAsset>? Changed;

    /// <summary>Whether the host's store holds an earlier CUDA pack: this machine had the pack, so an app may fetch the current one in
    /// its place without asking again.</summary>
    public bool HadEarlierRuntimePack => _state.EarlierRuntimePack;

    /// <summary>Reads the store from the host — starting one, where none runs. The first read tells of every file here, as each
    /// arrives later is told of: what asked what is here before then heard nothing was.</summary>
    /// <exception cref="AiHostException">The host could not be reached.</exception>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await foreach (var reply in _host.SendAsync(id => new DescribeModelStore(id), FetchStall, ct).ConfigureAwait(false))
            if (reply is ModelStoreDescribed described) Take(described.State);
        if (Interlocked.Exchange(ref _read, 1) == 0)
            foreach (var file in _files.Values.Where(f => f.Here))
                if (_catalog.TryGetValue(file.Path, out var asset)) Changed?.Invoke(this, asset);
    }

    public bool Has(ModelAsset asset) => _files.TryGetValue(asset.RelativePath, out var file) && file.Here;

    public long FetchBytes(IEnumerable<ModelAsset> assets) => Missing(assets).Where(a => !Adoptable(a)).Sum(a => a.Size);

    public ModelRoom RoomFor(IEnumerable<ModelAsset> assets)
    {
        var state = _state;
        var missing = Missing(assets).Where(a => !Adoptable(a)).ToList();
        var claims = StoreClaims.Of(missing, a => _files.TryGetValue(a.RelativePath, out var f) ? f.Partial : 0,
                                    archive => state.ArchivePartials.TryGetValue(archive.ArchiveSha256, out var kept) ? kept : 0);
        return new ModelRoom(StoreClaims.Weigh(claims, state.UnderWay), state.Free, state.Drive);
    }

    /// <exception cref="ModelRoomException">The store's drive has too little room left; nothing was fetched.</exception>
    /// <exception cref="AiHostException">The host refused or failed the fetch, or could not be reached.</exception>
    public async Task EnsureAllAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
    {
        var paths = assets.Select(a => a.RelativePath).Distinct(StringComparer.Ordinal).ToList();
        ModelRoom? short_ = null;
        try
        {
            await foreach (var reply in _host.SendAsync(id => new FetchModelFiles(id, paths), FetchStall, ct).ConfigureAwait(false))
            {
                if (reply is Progress { Stage: FetchModelFiles.Fetching } step) progress?.Report(new AssetProgress(step.Done, step.Total));
                else if (reply is NoRoomForModelFiles room) short_ = new ModelRoom(room.Bytes, room.Free, room.Drive);
            }
        }
        catch (AiHostException) when (short_ is { } room)
        {
            throw new ModelRoomException(room);
        }
        finally
        {
            await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(ModelAsset asset, CancellationToken ct = default)
    {
        await foreach (var _ in _host.SendAsync(id => new RemoveModelFiles(id, [asset.RelativePath]), FetchStall, ct).ConfigureAwait(false)) { }
        await RefreshAsync(ct).ConfigureAwait(false);
    }

    private IEnumerable<ModelAsset> Missing(IEnumerable<ModelAsset> assets)
        => assets.Where(a => !Has(a)).DistinctBy(a => a.RelativePath, StringComparer.Ordinal);

    private bool Adoptable(ModelAsset asset) => _files.TryGetValue(asset.RelativePath, out var file) && file.Adoptable;

    private void Take(ModelStoreState state)
    {
        _files = state.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        _state = state;
    }

    /// <summary>The host says files changed: the store is read again, then each is told of — what runs a model it belongs to can run it
    /// now, or no longer.</summary>
    private async Task OnChangedAsync(IReadOnlyList<string> paths)
    {
        try { await RefreshAsync().ConfigureAwait(false); }
        catch (AiHostException) { return; }   // the host has gone: the next one is asked when something is
        foreach (var path in paths)
            if (_catalog.TryGetValue(path, out var asset)) Changed?.Invoke(this, asset);
    }
}
