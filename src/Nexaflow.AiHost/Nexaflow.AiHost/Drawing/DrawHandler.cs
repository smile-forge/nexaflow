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
        if (request.NegativePrompt is not null && !method.TakesNegativePrompts)
            throw new HostRequestException(HostErrorKind.Invalid, $"A model that draws the {request.Model.Method} way is told nothing to keep out.");
        if (request.Lora is not null && !method.TakesLoras)
            throw new HostRequestException(HostErrorKind.Invalid, $"A model that draws the {request.Model.Method} way takes no LoRA.");
        if (request.Sources.Count > method.MaxSources)
            throw new HostRequestException(HostErrorKind.Invalid, $"A model that draws the {request.Model.Method} way works from at most {method.MaxSources} picture(s).");
        foreach (var source in request.Sources)
        {
            if (!method.Draws(source.Width, source.Height))
                throw new HostRequestException(HostErrorKind.Invalid, $"A {source.Width}x{source.Height} picture to work from is not a size this model reads.");
            if (source.Rgb.Length != source.Width * source.Height * 3)
                throw new HostRequestException(HostErrorKind.Invalid, $"A {source.Width}x{source.Height} picture is {source.Width * source.Height * 3} bytes of RGB, not {source.Rgb.Length}.");
        }
        if (request.Mask is { } mask)
        {
            if (!method.Masks)
                throw new HostRequestException(HostErrorKind.Invalid, $"A model that draws the {request.Model.Method} way takes no mask.");
            if (request.Sources.Count == 0 || (request.Sources[0].Width, request.Sources[0].Height) != (request.Width, request.Height))
                throw new HostRequestException(HostErrorKind.Invalid, "A mask lies over a first picture to work from the size of the picture drawn.");
            if (mask.Length != request.Width * request.Height)
                throw new HostRequestException(HostErrorKind.Invalid, $"A {request.Width}x{request.Height} mask is {request.Width * request.Height} bytes, not {mask.Length}.");
        }

        using var room = models.BorrowRoom(ids => reply(new MadeWay(request.RequestId, ids)));
        var rgb = await method.DrawAsync(request, stage => reply(new Progress(request.RequestId, stage, 0, 0)),
                                         (done, total) => reply(new Progress(request.RequestId, Drawing, done, total)), room, ct);
        await reply(new Drawn(request.RequestId, PngEncoder.EncodeRgb(rgb, request.Width, request.Height)));
    }
}
