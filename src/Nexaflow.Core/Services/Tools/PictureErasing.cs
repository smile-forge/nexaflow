using Nexaflow.Features.Common;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// Erasing from pictures as features and the assistant use it (<see cref="IPictureErasing"/>): LaMa in the AI host
/// (<see cref="OwnedPictureErasing"/>).
/// </summary>
public sealed class PictureErasing : LocalToolModels, IPictureErasing
{
    private readonly OwnedPictureErasing _erasing;

    public PictureErasing(OwnedPictureErasing erasing, IModelAssetStore store, IBackgroundActivityManager? activities) : base(erasing, store, activities)
        => _erasing = erasing;

    public override string Name => Str.Get("Shell.Tools.Erasing.Name");

    public override string About => Str.Get("Shell.Tools.Erasing.About");

    public async Task<PictureErasure> EraseAsync(PictureEraseRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        if (request.Width <= 0 || request.Height <= 0)
            throw new InvalidOperationException("A picture to erase from has a size.");
        var regions = progress is null ? null : new Inline<(int Done, int Total)>(step => progress.Report((double)step.Done / step.Total));
        var erased = await _erasing.EraseAsync(request.Pixels, request.Mask, request.Width, request.Height, request.Into, regions, ct);
        return new PictureErasure(erased.Regions);
    }
}
