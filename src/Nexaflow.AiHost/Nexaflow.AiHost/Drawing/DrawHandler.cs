using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Draws a picture by the method its model draws with (<see cref="IDrawingMethod"/>) and writes it as a PNG. The method
/// takes the GPU a step at a time at the request's priority, so a reply or speech gets in between, and each denoising
/// step is reported as it finishes. A part is held only while it is needed, so the host needs room for the largest of
/// them, not all — and the picture borrows that room: residents nothing is using make way on its parts' GPUs, and load
/// again after it.
/// </summary>
internal sealed class DrawHandler(ModelRegistry models, IReadOnlyList<IDrawingMethod> methods) : HostRequestHandler<Draw>
{
    public const string Drawing = "drawing";

    protected override async Task HandleAsync(Draw request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        var method = methods.FirstOrDefault(m => m.Method == request.Model.Method)
                     ?? throw new HostRequestException(HostErrorKind.Invalid, $"This host does not draw the {request.Model.Method} way.");
        if (!method.Draws(request.Width, request.Height))
            throw new HostRequestException(HostErrorKind.Invalid, $"{request.Width}x{request.Height} is not a size this model draws at.");
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new HostRequestException(HostErrorKind.Invalid, "Drawing needs a prompt.");

        using var room = models.BorrowRoom(ids => reply(new MadeWay(request.RequestId, ids)));
        var rgb = await method.DrawAsync(request, stage => reply(new Progress(request.RequestId, stage, 0, 0)),
                                         (done, total) => reply(new Progress(request.RequestId, Drawing, done, total)), room, ct);
        await reply(new Drawn(request.RequestId, PngEncoder.EncodeRgb(rgb, request.Width, request.Height)));
    }
}
