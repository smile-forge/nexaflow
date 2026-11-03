using System.Runtime.InteropServices;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.SceneDepth;

/// <summary>
/// Estimates a picture's scene depth as Depth Anything V2's <c>infer_image</c> does, up to the disparity at its working size:
/// the picture prepared on the CPU (<see cref="SceneDepthHandover"/>), then the estimator held in the room the picture
/// borrowed and run once, taking the GPU at the request's priority — or the CPU where its spec places it. Bringing the
/// disparity to the picture's size is the caller's, at whatever size it needs it.
/// </summary>
internal sealed class SceneDepthHandler(ModelRegistry models, GpuScheduler gpu) : HostRequestHandler<EstimateSceneDepth>
{
    protected override async Task HandleAsync(EstimateSceneDepth request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        Validate(request);
        var rgba = await File.ReadAllBytesAsync(request.Pixels, ct);
        if (rgba.Length != request.Width * request.Height * 4)
            throw new HostRequestException(HostErrorKind.Invalid, $"{request.Pixels} holds {rgba.Length} bytes, not {request.Width}x{request.Height} RGBA.");
        var input = await Task.Run(() => SceneDepthHandover.Prepare(rgba, request.Width, request.Height), ct);

        var lane = gpu.Open(request.Priority);
        using var room = models.BorrowRoom(ids => reply(new MadeWay(request.RequestId, ids)));
        var spec = request.Parts[SceneDepthParts.Estimator];
        using var lease = await models.AcquireAsync(spec, resident: false, s => reply(new Progress(request.RequestId, s, 0, 0)), ct, room);
        float[] disparity;
        using (await lane.NextAsync(spec.Device, ct))
        {
            ct.ThrowIfCancellationRequested();
            disparity = lease.As<SceneDepthNetwork>().Estimate(input);
        }
        await File.WriteAllBytesAsync(request.Into, MemoryMarshal.AsBytes(disparity.AsSpan()).ToArray(), ct);
        await reply(new SceneDepthEstimated(request.RequestId, input.Width, input.Height));
    }

    private static void Validate(EstimateSceneDepth request)
    {
        if (request.Width <= 0 || request.Height <= 0)
            throw new HostRequestException(HostErrorKind.Invalid, "A picture to estimate the depth of has a size.");
        if (string.IsNullOrWhiteSpace(request.Into))
            throw new HostRequestException(HostErrorKind.Invalid, "A depth estimate needs a file to write the disparity to.");
        if (!File.Exists(request.Pixels))
            throw new HostRequestException(HostErrorKind.NotFound, $"The picture to estimate the depth of is not at {request.Pixels}.");
        foreach (var role in SceneDepthParts.All)
            if (!request.Parts.ContainsKey(role))
                throw new HostRequestException(HostErrorKind.Invalid, $"Scene depth needs its {role}.");
    }
}
