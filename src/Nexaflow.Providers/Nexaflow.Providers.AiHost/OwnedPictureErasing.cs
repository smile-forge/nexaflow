using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>An erased picture, in the file it was asked into, filled in <see cref="Regions"/> regions.</summary>
public sealed record ErasedPicture(int Regions);

/// <summary>
/// Erasing from pictures in Nexaflow's AI host: what a mask covers filled from what is round it by LaMa's big-lama
/// (<see cref="OwnedToolKind.PictureErasing"/>).
/// </summary>
public sealed class OwnedPictureErasing : OwnedToolFront
{
    public OwnedPictureErasing(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup, ComputeHardwareReport hardware)
        : this(new OwnedModelRuntime(activities, assets, host, setup, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == OwnedToolKind.PictureErasing))
    {
    }

    internal OwnedPictureErasing(OwnedModelRuntime runtime, OwnedTool? tool) : base(runtime, tool)
    {
    }

    /// <summary>
    /// Erases from the picture in <paramref name="pixels"/> — interleaved 8-bit RGBA with nothing else — what the mask in
    /// <paramref name="mask"/> covers, into <paramref name="into"/> (<see cref="ErasePicture"/>), reporting each region done
    /// of all. Never downloads: the tool is fetched when a person asks for it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool is not on this machine, there is no room to run it, or the host
    /// could not.</exception>
    public async Task<ErasedPicture> EraseAsync(string pixels, string mask, int width, int height, string into, IProgress<(int Done, int Total)>? progress,
                                                CancellationToken ct)
    {
        var madeWay = new List<IDisposable>();   // the models that make way for this picture, back once it is done
        try
        {
            var parts = await PlacedAsync(ErasingParts.All, ct);
            ErasedPicture? erased = null;
            await foreach (var reply in Owned.Host.SendAsync(id => new ErasePicture(id, parts, pixels, mask, width, height, into, RequestPriority.Job),
                                                             OwnedModelRuntime.StallLimit, ct))
            {
                switch (reply)
                {
                    case Progress { Stage: ErasingStages.Filling, Total: > 0 } step:
                        progress?.Report(((int)step.Done, (int)step.Total));
                        break;
                    case MadeWay made:
                        madeWay.Add(Owned.MakingWay(made.Models));
                        break;
                    case PictureErased done:
                        erased = new ErasedPicture(done.Regions);
                        break;
                }
            }
            return erased ?? throw new InvalidOperationException("The AI host finished without the erased picture.");
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            throw new InvalidOperationException($"The picture could not be erased: {ex.Message}", ex);
        }
        finally
        {
            foreach (var back in madeWay) back.Dispose();
        }
    }
}
