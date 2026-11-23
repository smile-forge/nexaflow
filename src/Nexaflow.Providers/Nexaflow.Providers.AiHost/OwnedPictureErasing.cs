using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>An erased picture, filled in <see cref="Regions"/> regions, its pixels interleaved 8-bit RGBA. Dispose it once read:
/// its pixels may be a section the AI host shares.</summary>
public sealed record ErasedPicture(int Regions, SharedBytes Pixels) : IDisposable
{
    public void Dispose() => Pixels.Dispose();
}

/// <summary>
/// Erasing from pictures in Nexaflow's AI host: what a mask covers filled from what is round it by LaMa's big-lama
/// (<see cref="HostedToolKind.PictureErasing"/>).
/// </summary>
public sealed class OwnedPictureErasing : OwnedToolFront
{
    public OwnedPictureErasing(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup, MachineCards hardware)
        : this(new OwnedModelRuntime(activities, assets, host, setup, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == HostedToolKind.PictureErasing))
    {
    }

    internal OwnedPictureErasing(OwnedModelRuntime runtime, HostedTool? tool) : base(runtime, tool)
    {
    }

    /// <summary>
    /// Erases from the picture <paramref name="pixels"/> — interleaved 8-bit RGBA with nothing else — what <paramref name="mask"/>, a
    /// byte a pixel, covers (<see cref="ErasePicture"/>), reporting each region done of all. Never downloads: the tool is fetched when
    /// a person asks for it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool is not on this machine, there is no room to run it, or the host
    /// could not.</exception>
    public async Task<ErasedPicture> EraseAsync(ReadOnlyMemory<byte> pixels, ReadOnlyMemory<byte> mask, int width, int height,
                                                IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        var madeWay = new List<IDisposable>();   // the models that make way for this picture, back once it is done
        ErasedPicture? erased = null;
        try
        {
            var parts = await PlacedAsync(ErasingParts.All, ct);
            using var picture = await Owned.Host.ShareAsync(pixels, ct);
            using var covered = await Owned.Host.ShareAsync(mask, ct);
            await foreach (var reply in Owned.Host.SendAsync(id => new ErasePicture(id, parts, picture, covered, width, height, RequestPriority.Job),
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
                        erased = new ErasedPicture(done.Regions, done.Pixels);
                        break;
                }
            }
            return erased ?? throw new InvalidOperationException("The AI host finished without the erased picture.");
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            erased?.Dispose();
            throw new InvalidOperationException($"The picture could not be erased: {ex.Message}", ex);
        }
        catch
        {
            erased?.Dispose();
            throw;
        }
        finally
        {
            foreach (var back in madeWay) back.Dispose();
        }
    }
}
