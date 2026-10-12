using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers.Support;

/// <summary>The asset store in memory: files are here once fetched, under <see cref="Root"/>; a store set up with
/// <c>present</c> has them already.</summary>
internal sealed class FakeModelStore(params IEnumerable<ModelAsset> present) : IModelAssetStore
{
    public const string Root = @"C:\ai\";

    public readonly HashSet<ModelAsset> Here = [.. present];
    public readonly List<IReadOnlyList<ModelAsset>> Fetched = [];

    public event EventHandler<ModelAsset>? Changed;

    public string? Locate(ModelAsset asset) => Here.Contains(asset) ? Root + asset.RelativePath.Replace('/', '\\') : null;

    public Task<string> EnsureAsync(ModelAsset asset, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
        => Task.FromResult(EnsureAllAsync([asset], progress, ct).Result[0]);

    public Task<IReadOnlyList<string>> EnsureAllAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
    {
        Fetched.Add(assets);
        foreach (var asset in assets) { Here.Add(asset); Changed?.Invoke(this, asset); }
        return Task.FromResult<IReadOnlyList<string>>([.. assets.Select(a => Locate(a)!)]);
    }
}
