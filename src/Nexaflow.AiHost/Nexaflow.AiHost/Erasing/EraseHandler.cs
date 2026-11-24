using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;


namespace Nexaflow.AiHost.Erasing;

/// <summary>
/// Erases what a mask covers from a picture as LaMa's predict fills one, a region at a time (<see cref="ErasingRegions"/>,
/// <see cref="ErasingHandover"/>): the inpainter held while the picture's regions go through it, in the room the picture
/// borrowed, each region taking the GPU at the request's priority and reported.
/// </summary>
internal sealed class EraseHandler(HeldModels models, GpuScheduler gpu) : HostRequestHandler<ErasePicture>
{
    protected override async Task HandleAsync(ErasePicture request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        Validate(request);
        // Filled in place in the bytes the reply carries back: the picture is copied once, and only the mask's pixels change.
        var erased = RequestSections.Create(request.Pixels.Length);
        request.Pixels.Span.CopyTo(erased.Span);
        var mask = request.Mask.Memory;

        var regions = ErasingRegions.Of(mask.Span, request.Width, request.Height);
        if (regions.Count > 0)
        {
            var lane = gpu.Open(request.Priority);
            using var room = models.BorrowRoom();
            using var lease = await models.AcquireAsync(models.ToolPart(request.Tool, ErasingParts.Inpainter), s => reply(new Progress(request.RequestId, s, 0, 0)), ct, room);
            var inpainter = lease.As<PictureInpainter>();
            for (int i = 0; i < regions.Count; i++)
            {
                var prepared = ErasingHandover.Prepare(erased.Span, request.Width, mask.Span, regions[i]);
                float[] inpainted;
                using (await lane.NextAsync(lease.Model.Spec.Device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    inpainted = inpainter.Inpaint(prepared.Rgb, prepared.Mask, prepared.Width, prepared.Height, lastCall: i == regions.Count - 1);
                }
                ErasingHandover.LayBack(erased.Span, request.Width, mask.Span, regions[i], inpainted, prepared);
                await reply(new Progress(request.RequestId, ErasingStages.Filling, i + 1, regions.Count));
            }
        }
        await reply(new PictureErased(request.RequestId, regions.Count, erased));
    }

    private static void Validate(ErasePicture request)
    {
        if (request.Width <= 0 || request.Height <= 0)
            throw new HostRequestException(HostErrorKind.Invalid, "A picture to erase from has a size.");
        if (request.Pixels.Length != (long)request.Width * request.Height * 4)
            throw new HostRequestException(HostErrorKind.Invalid, $"A {request.Width}x{request.Height} picture is not {request.Pixels.Length} bytes of RGBA.");
        if (request.Mask.Length != (long)request.Width * request.Height)
            throw new HostRequestException(HostErrorKind.Invalid, $"A {request.Width}x{request.Height} mask is not {request.Mask.Length} bytes.");
    }
}
