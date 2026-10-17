using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Erasing;

/// <summary>
/// Erases what a mask covers from a picture as LaMa's predict fills one, a region at a time (<see cref="InpaintRegions"/>,
/// <see cref="ErasingHandover"/>): the inpainter held while the picture's regions go through it, in the room the picture
/// borrowed, each region taking the GPU at the request's priority and reported.
/// </summary>
internal sealed class EraseHandler(ModelRegistry models, GpuScheduler gpu) : HostRequestHandler<ErasePicture>
{
    protected override async Task HandleAsync(ErasePicture request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        Validate(request);
        var rgba = await File.ReadAllBytesAsync(request.Pixels, ct);
        if (rgba.Length != request.Width * request.Height * 4)
            throw new HostRequestException(HostErrorKind.Invalid, $"{request.Pixels} holds {rgba.Length} bytes, not {request.Width}x{request.Height} RGBA.");
        var mask = await File.ReadAllBytesAsync(request.Mask, ct);
        if (mask.Length != request.Width * request.Height)
            throw new HostRequestException(HostErrorKind.Invalid, $"{request.Mask} holds {mask.Length} bytes, not a {request.Width}x{request.Height} mask.");

        var regions = InpaintRegions.Of(mask, request.Width, request.Height);
        if (regions.Count > 0)
        {
            var lane = gpu.Open(request.Priority);
            using var room = models.BorrowRoom(ids => reply(new MadeWay(request.RequestId, ids)));
            var spec = request.Parts[ErasingParts.Inpainter];
            using var lease = await models.AcquireAsync(spec, resident: false, s => reply(new Progress(request.RequestId, s, 0, 0)), ct, room);
            var inpainter = lease.As<PictureInpainter>();
            for (int i = 0; i < regions.Count; i++)
            {
                var prepared = ErasingHandover.Prepare(rgba, request.Width, mask, regions[i]);
                float[] inpainted;
                using (await lane.NextAsync(spec.Device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    inpainted = inpainter.Inpaint(prepared.Rgb, prepared.Mask, prepared.Width, prepared.Height, lastCall: i == regions.Count - 1);
                }
                ErasingHandover.LayBack(rgba, request.Width, mask, regions[i], inpainted, prepared);
                await reply(new Progress(request.RequestId, ErasingStages.Filling, i + 1, regions.Count));
            }
        }
        await File.WriteAllBytesAsync(request.Into, rgba, ct);
        await reply(new PictureErased(request.RequestId, regions.Count));
    }

    private static void Validate(ErasePicture request)
    {
        if (request.Width <= 0 || request.Height <= 0)
            throw new HostRequestException(HostErrorKind.Invalid, "A picture to erase from has a size.");
        if (string.IsNullOrWhiteSpace(request.Into))
            throw new HostRequestException(HostErrorKind.Invalid, "An erasure needs a file to write the picture to.");
        if (!File.Exists(request.Pixels))
            throw new HostRequestException(HostErrorKind.NotFound, $"The picture to erase from is not at {request.Pixels}.");
        if (!File.Exists(request.Mask))
            throw new HostRequestException(HostErrorKind.NotFound, $"The mask of what to erase is not at {request.Mask}.");
        foreach (var role in ErasingParts.All)
            if (!request.Parts.ContainsKey(role))
                throw new HostRequestException(HostErrorKind.Invalid, $"Erasing needs its {role}.");
    }
}
