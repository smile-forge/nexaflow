using Nexaflow.AiHost.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// The picture tools as features and the assistant use them (<see cref="IPictureTools"/>): Real-ESRGAN and GFPGAN in the
/// AI host (<see cref="OwnedPictureTools"/>).
/// </summary>
public sealed class PictureTools : LocalToolModels, IPictureTools
{
    /// <summary>How much of a restoration's progress the upscaler is, where faces are remade after it.</summary>
    private const double UpscalingShare = 0.85;

    private readonly OwnedPictureTools _tools;

    public PictureTools(OwnedPictureTools tools, IModelAssetStore store, IBackgroundActivityManager? activities) : base(tools, store, activities)
        => _tools = tools;

    public override string Name => Str.Get("Shell.Tools.Picture.Name");

    public override string About => Str.Get("Shell.Tools.Picture.About");

    public bool Restores(int width, int height, int scale) => RestorePicture.Restores(width, height, scale);

    public async Task<PictureRestoration> RestoreAsync(PictureRestoreRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        if (!Restores(request.Width, request.Height, request.Scale))
            throw new InvalidOperationException($"A {request.Width}x{request.Height} picture is too large to restore at {request.Scale}x.");
        double upscaling = request.Faces ? UpscalingShare : 1;
        var steps = progress is null ? null : new Inline<RestoringStep>(step => progress.Report(step.Stage == RestoringStages.Faces
            ? upscaling + (1 - upscaling) * step.Done / step.Total
            : upscaling * step.Done / step.Total));
        var restored = await _tools.RestoreAsync(request.Pixels, request.Width, request.Height, request.Scale, request.Faces, request.Into, steps, ct);
        return new PictureRestoration(restored.Width, restored.Height, restored.FacesRestored);
    }
}
