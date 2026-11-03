using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>A picture's disparity, in the file it was asked into, at the size the estimator read the picture.</summary>
public sealed record EstimatedSceneDepth(int Width, int Height);

/// <summary>
/// Scene depth in Nexaflow's AI host: how far each part of a picture lies from the camera, from the one picture, by Depth
/// Anything V2 Small (<see cref="OwnedToolKind.SceneDepth"/>) — on the GPU where it has room, else the CPU.
/// </summary>
public sealed class OwnedSceneDepth : OwnedToolFront
{
    public OwnedSceneDepth(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup, ComputeHardwareReport hardware)
        : this(new OwnedModelRuntime(activities, assets, host, setup, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == OwnedToolKind.SceneDepth))
    {
    }

    internal OwnedSceneDepth(OwnedModelRuntime runtime, OwnedTool? tool) : base(runtime, tool)
    {
    }

    /// <summary>
    /// Estimates the scene depth of the picture in <paramref name="pixels"/> — interleaved 8-bit RGBA with nothing else — into
    /// <paramref name="into"/> (<see cref="EstimateSceneDepth"/>). Never downloads: the tool is fetched when a person asks for it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool is not on this machine, there is no room to run it, or the host
    /// could not.</exception>
    public async Task<EstimatedSceneDepth> EstimateAsync(string pixels, int width, int height, string into, CancellationToken ct)
    {
        var madeWay = new List<IDisposable>();   // the models that make way for this picture, back once it is done
        try
        {
            var parts = await PlacedAsync(SceneDepthParts.All, ct);
            EstimatedSceneDepth? estimated = null;
            await foreach (var reply in Owned.Host.SendAsync(id => new EstimateSceneDepth(id, parts, pixels, width, height, into, RequestPriority.Job),
                                                             OwnedModelRuntime.StallLimit, ct))
            {
                switch (reply)
                {
                    case MadeWay made:
                        madeWay.Add(Owned.MakingWay(made.Models));
                        break;
                    case SceneDepthEstimated done:
                        estimated = new EstimatedSceneDepth(done.Width, done.Height);
                        break;
                }
            }
            return estimated ?? throw new InvalidOperationException("The AI host finished without the picture's depth.");
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            throw new InvalidOperationException($"The picture's depth could not be estimated: {ex.Message}", ex);
        }
        finally
        {
            foreach (var back in madeWay) back.Dispose();
        }
    }
}
