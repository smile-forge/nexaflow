using Nexaflow.Core.Services.Generation;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// The picture check Enhanced AI Protection Mode holds every picture made to (<see cref="IPictureCheck"/>):
/// OwenElliott's image safety classifier, M, in the AI host on the CPU (<see cref="OwnedPictureChecking"/>). A picture is
/// fit while the chances it shows gore or sexual content stay under <see cref="UnfitFrom"/> together. The classifier is
/// fetched the first time a picture is checked: asking for a picture while the mode is on is asking for its check.
/// </summary>
public sealed class PictureChecking : LocalToolModels, IPictureCheck
{
    /// <summary>The chance of gore and of sexual content together from which a picture is withheld.</summary>
    public const float UnfitFrom = 0.5f;

    private readonly OwnedPictureChecking _checking;

    public PictureChecking(OwnedPictureChecking checking, IModelAssetStore store, IBackgroundActivityManager? activities) : base(checking, store, activities)
        => _checking = checking;

    public override string Name => Str.Get("Shell.Tools.Checking.Name");

    public override string About => Str.Get("Shell.Tools.Checking.About");

    async Task<bool> IPictureCheck.FitForGeneralAudienceAsync(MediaBytes picture, CancellationToken ct)
    {
        await ReadyAsync(ct);
        return IsFit(await _checking.CheckAsync(picture.Bytes, ct));
    }

    /// <summary>Whether a verdict leaves a picture fit for a general audience.</summary>
    internal static bool IsFit(PictureVerdict verdict) => verdict.Gore + verdict.Sexual < UnfitFrom;
}
