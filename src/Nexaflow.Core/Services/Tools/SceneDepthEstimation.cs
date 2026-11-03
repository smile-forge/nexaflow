using Nexaflow.Features.Common;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// Scene depth as features and the assistant use it (<see cref="ISceneDepth"/>): Depth Anything V2 Small in the AI host
/// (<see cref="OwnedSceneDepth"/>).
/// </summary>
public sealed class SceneDepthEstimation : LocalToolModels, ISceneDepth
{
    private readonly OwnedSceneDepth _depth;

    public SceneDepthEstimation(OwnedSceneDepth depth, IModelAssetStore store, IBackgroundActivityManager? activities) : base(depth, store, activities)
        => _depth = depth;

    public override string Name => Str.Get("Shell.Tools.SceneDepth.Name");

    public override string About => Str.Get("Shell.Tools.SceneDepth.About");

    public async Task<SceneDepthEstimate> EstimateAsync(SceneDepthRequest request, CancellationToken ct)
    {
        if (request.Width <= 0 || request.Height <= 0)
            throw new InvalidOperationException("A picture to estimate the depth of has a size.");
        var estimated = await _depth.EstimateAsync(request.Pixels, request.Width, request.Height, request.Into, ct);
        return new SceneDepthEstimate(estimated.Width, estimated.Height);
    }
}
