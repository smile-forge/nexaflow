using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>How fit a picture is for a general audience, as chances that sum to 1: gore, sexual content, or neither.</summary>
public sealed record PictureVerdict(float Gore, float Sexual, float Neither);

/// <summary>
/// Checking pictures in Nexaflow's AI host: OwenElliott's image safety classifier, M, on the CPU
/// (<see cref="OwnedToolKind.PictureChecking"/>).
/// </summary>
public sealed class OwnedPictureChecking : OwnedToolFront
{
    public OwnedPictureChecking(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup, ComputeHardwareReport hardware)
        : this(new OwnedModelRuntime(activities, assets, host, setup, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == OwnedToolKind.PictureChecking))
    {
    }

    internal OwnedPictureChecking(OwnedModelRuntime runtime, OwnedTool? tool) : base(runtime, tool)
    {
    }

    /// <summary>How fit <paramref name="picture"/> — encoded as PNG, JPEG, WebP or whatever Windows reads — is for a
    /// general audience. Never downloads: the tool is fetched when a person asks for it.</summary>
    /// <exception cref="InvalidOperationException">The tool is not on this machine, or the host could not check it.</exception>
    public async Task<PictureVerdict> CheckAsync(byte[] picture, CancellationToken ct)
    {
        try
        {
            var parts = await PlacedAsync([CheckingParts.SafetyClassifier], ct);
            await foreach (var reply in Owned.Host.SendAsync(id => new CheckPicture(id, parts[CheckingParts.SafetyClassifier], picture),
                                                             OwnedModelRuntime.StallLimit, ct))
                if (reply is PictureChecked checkedOne) return new PictureVerdict(checkedOne.Gore, checkedOne.Sexual, checkedOne.Neither);
            throw new InvalidOperationException("The AI host finished without its verdict on the picture.");
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            throw new InvalidOperationException($"The picture could not be checked: {ex.Message}", ex);
        }
    }
}
