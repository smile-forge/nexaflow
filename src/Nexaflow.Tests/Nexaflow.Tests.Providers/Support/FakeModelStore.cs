using Nexaflow.Providers.Common;
using Nexaflow.AiHost.Provisioning;

namespace Nexaflow.Tests.Providers.Support;

/// <summary>The host's model store in memory: files are here once fetched; a store set up with <c>present</c> has them already.</summary>
internal sealed class FakeModelStore(params IEnumerable<ModelAsset> present) : IModelAssetStore
{
    public readonly HashSet<ModelAsset> Here = [.. present];
    public readonly List<IReadOnlyList<ModelAsset>> Fetched = [];

    public event EventHandler<ModelAsset>? Changed;

    public bool Has(ModelAsset asset) => Here.Contains(asset);

    public Task EnsureAllAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
    {
        Fetched.Add(assets);
        foreach (var asset in assets) { Here.Add(asset); Changed?.Invoke(this, asset); }
        return Task.CompletedTask;
    }

    public ModelRoom RoomFor(IEnumerable<ModelAsset> assets) => new(FetchBytes(assets), null, @"C:\");

    public long FetchBytes(IEnumerable<ModelAsset> assets) => assets.Where(a => !Here.Contains(a)).DistinctBy(a => a.RelativePath).Sum(a => a.Size);

    public Task RemoveAsync(ModelAsset asset, CancellationToken ct = default)
    {
        Here.Remove(asset);
        Changed?.Invoke(this, asset);
        return Task.CompletedTask;
    }
}
