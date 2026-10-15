using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>A restored picture, in the file it was asked into.</summary>
/// <param name="FacesRestored">How many faces were found and remade in it.</param>
public sealed record RestoredPicture(int Width, int Height, int FacesRestored);

/// <summary>How far a restoration has got: tiles through the upscaler (<see cref="RestoringStages.Upscaling"/>), then
/// faces remade (<see cref="RestoringStages.Faces"/>).</summary>
public sealed record RestoringStep(string Stage, int Done, int Total);

/// <summary>
/// The picture tools Nexaflow runs in its AI host: a picture restored — made twice or four times its size, or kept at its
/// size and cleaned, its faces found and remade — with Real-ESRGAN and GFPGAN (<see cref="OwnedToolKind.PictureRestoration"/>).
/// </summary>
public sealed class OwnedPictureTools : OwnedToolFront
{
    public OwnedPictureTools(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup, ComputeHardwareReport hardware)
        : this(new OwnedModelRuntime(activities, assets, host, setup, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == OwnedToolKind.PictureRestoration))
    {
    }

    internal OwnedPictureTools(OwnedModelRuntime runtime, OwnedTool? tool) : base(runtime, tool)
    {
    }

    /// <summary>
    /// Restores the picture in <paramref name="pixels"/> into <paramref name="into"/>, both interleaved 8-bit RGBA with
    /// nothing else (<see cref="RestorePicture"/>), at <paramref name="scale"/> times its size, faces remade where
    /// <paramref name="faces"/>. Never downloads: the tools are fetched when a person asks for them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tools are not on this machine, there is no room to run them, or the
    /// host could not.</exception>
    public async Task<RestoredPicture> RestoreAsync(string pixels, int width, int height, int scale, bool faces, string into,
                                                    IProgress<RestoringStep>? progress, CancellationToken ct)
    {
        var madeWay = new List<IDisposable>();   // the models that make way for this picture, back once it is done
        try
        {
            var parts = await PlacedAsync(RestoringParts.Of(scale, faces), ct);
            RestoredPicture? restored = null;
            await foreach (var reply in Owned.Host.SendAsync(id => new RestorePicture(id, parts, pixels, width, height, scale, faces, into, RequestPriority.Job),
                                                             OwnedModelRuntime.StallLimit, ct))
            {
                switch (reply)
                {
                    case Progress { Total: > 0 } step:
                        progress?.Report(new RestoringStep(step.Stage, (int)step.Done, (int)step.Total));
                        break;
                    case MadeWay made:
                        madeWay.Add(Owned.MakingWay(made.Models));
                        break;
                    case PictureRestored done:
                        restored = new RestoredPicture(done.Width, done.Height, done.FacesRestored);
                        break;
                }
            }
            return restored ?? throw new InvalidOperationException("The AI host finished without the restored picture.");
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            throw new InvalidOperationException($"The picture could not be restored: {ex.Message}", ex);
        }
        finally
        {
            foreach (var back in madeWay) back.Dispose();
        }
    }
}
