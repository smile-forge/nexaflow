using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>A restored picture: its size, how many faces were found and remade in it, and its pixels, interleaved 8-bit RGBA.
/// Dispose it once read: its pixels may be a section the AI host shares.</summary>
public sealed record RestoredPicture(int Width, int Height, int FacesRestored, SharedBytes Pixels) : IDisposable
{
    public void Dispose() => Pixels.Dispose();
}

/// <summary>How far a restoration has got: tiles through the upscaler (<see cref="RestoringStages.Upscaling"/>), then
/// faces remade (<see cref="RestoringStages.Faces"/>).</summary>
public sealed record RestoringStep(string Stage, int Done, int Total);

/// <summary>
/// The picture tools Nexaflow runs in its AI host: a picture restored — made twice or four times its size, or kept at its
/// size and cleaned, its faces found and remade — with Real-ESRGAN and GFPGAN (<see cref="HostedToolKind.PictureRestoration"/>).
/// </summary>
public sealed class OwnedPictureTools : OwnedToolFront
{
    public OwnedPictureTools(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostHolds holds, MachineCards hardware)
        : this(new OwnedModelRuntime(activities, assets, host, holds, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == HostedToolKind.PictureRestoration))
    {
    }

    internal OwnedPictureTools(OwnedModelRuntime runtime, HostedTool? tool) : base(runtime, tool)
    {
    }

    /// <summary>
    /// Restores the picture <paramref name="pixels"/> — interleaved 8-bit RGBA with nothing else (<see cref="RestorePicture"/>) — at
    /// <paramref name="scale"/> times its size, faces remade where <paramref name="faces"/>. Never downloads: the tools are fetched
    /// when a person asks for them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tools are not on this machine, there is no room to run them, or the
    /// host could not.</exception>
    public async Task<RestoredPicture> RestoreAsync(ReadOnlyMemory<byte> pixels, int width, int height, int scale, bool faces,
                                                    IProgress<RestoringStep>? progress, CancellationToken ct)
    {
        RestoredPicture? restored = null;
        try
        {
            var tool = ToolId();
            using var picture = await Owned.Host.ShareAsync(pixels, ct);
            await foreach (var reply in Owned.Host.SendAsync(id => new RestorePicture(id, tool, picture, width, height, scale, faces, RequestPriority.Job),
                                                             OwnedModelRuntime.StallLimit, ct))
            {
                switch (reply)
                {
                    case Progress { Total: > 0 } step:
                        progress?.Report(new RestoringStep(step.Stage, (int)step.Done, (int)step.Total));
                        break;

                    case PictureRestored done:
                        restored = new RestoredPicture(done.Width, done.Height, done.FacesRestored, done.Pixels);
                        break;
                }
            }
            return restored ?? throw new InvalidOperationException("The AI host finished without the restored picture.");
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            restored?.Dispose();
            throw new InvalidOperationException($"The picture could not be restored: {ex.Message}", ex);
        }
        catch
        {
            restored?.Dispose();
            throw;
        }
    }
}
