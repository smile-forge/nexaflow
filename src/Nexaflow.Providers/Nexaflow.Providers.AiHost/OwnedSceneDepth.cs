using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>A picture's scene depth: <see cref="Width"/> × <see cref="Height"/> single-precision floats of disparity, row by row
/// (<see cref="SceneDepthEstimated"/>). Dispose it once read: the floats may be a section the AI host shares.</summary>
public sealed record EstimatedSceneDepth(int Width, int Height, SharedBytes Disparity) : IDisposable
{
    public void Dispose() => Disparity.Dispose();
}

/// <summary>
/// Scene depth in Nexaflow's AI host: how far each part of a picture lies from the camera, from the one picture, by Depth
/// Anything V2 Small (<see cref="HostedToolKind.SceneDepth"/>) — on the GPU where it has room, else the CPU.
/// </summary>
public sealed class OwnedSceneDepth : OwnedToolFront
{
    public OwnedSceneDepth(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostHolds holds, MachineCards hardware)
        : this(new OwnedModelRuntime(activities, assets, host, holds, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == HostedToolKind.SceneDepth))
    {
    }

    internal OwnedSceneDepth(OwnedModelRuntime runtime, HostedTool? tool) : base(runtime, tool)
    {
    }

    /// <summary>The scene depth of the picture <paramref name="pixels"/> — interleaved 8-bit RGBA with nothing else. Never
    /// downloads: the tool is fetched when a person asks for it.</summary>
    /// <exception cref="InvalidOperationException">The tool is not on this machine, there is no room to run it, or the host
    /// could not.</exception>
    public async Task<EstimatedSceneDepth> EstimateAsync(ReadOnlyMemory<byte> pixels, int width, int height, CancellationToken ct)
    {
        EstimatedSceneDepth? estimated = null;
        try
        {
            var tool = ToolId();
            using var picture = await Owned.Host.ShareAsync(pixels, ct);
            await foreach (var reply in Owned.Host.SendAsync(id => new EstimateSceneDepth(id, tool, picture, width, height, RequestPriority.Job),
                                                             OwnedModelRuntime.StallLimit, ct))
                if (reply is SceneDepthEstimated done) estimated = new EstimatedSceneDepth(done.Width, done.Height, done.Disparity);
            return estimated ?? throw new InvalidOperationException("The AI host finished without the picture's depth.");
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            estimated?.Dispose();
            throw new InvalidOperationException($"The picture's depth could not be estimated: {ex.Message}", ex);
        }
        catch
        {
            estimated?.Dispose();
            throw;
        }
    }
}
