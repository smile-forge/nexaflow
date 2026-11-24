using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;


namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Draws a picture by the method its model draws with (<see cref="IDrawingMethod"/>) and sends it as its pixels. The method
/// takes the GPU a step at a time at the request's priority, so a reply or speech gets in between, and each denoising
/// step is reported as it finishes. A part is held only while it is needed, so the host needs room for the largest of
/// them, not all — and the picture borrows that room: residents nothing is using make way on its parts' GPUs, and load
/// again after it.
/// </summary>
internal sealed class DrawHandler(ModelRegistry models, RequestModels requests, IReadOnlyList<IDrawingMethod> methods) : HostRequestHandler<Draw>
{
    public const string Drawing = "drawing";

    protected override async Task HandleAsync(Draw request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        var (entry, sampler) = requests.Drawer(request.Model);
        var method = methods.FirstOrDefault(m => m.Method == sampler.Method)
                      ?? throw new HostRequestException(HostErrorKind.Invalid, $"This host does not draw the {sampler.Method} way.");
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new HostRequestException(HostErrorKind.Invalid, "Drawing needs a prompt.");
        if (request.NegativePrompt is not null && !method.TakesNegativePrompts)
            throw new HostRequestException(HostErrorKind.Invalid, $"A model that draws the {sampler.Method} way is told nothing to keep out.");
        if (request.Lora is not null && !method.TakesLoras)
            throw new HostRequestException(HostErrorKind.Invalid, $"A model that draws the {sampler.Method} way takes no LoRA.");
        if (request.Sources.Count > method.MaxSources)
            throw new HostRequestException(HostErrorKind.Invalid, $"A model that draws the {sampler.Method} way works from at most {method.MaxSources} picture(s).");
        if (request.Mask is not null && !method.Masks)
            throw new HostRequestException(HostErrorKind.Invalid, $"A model that draws the {sampler.Method} way takes no mask.");

        if (request.Sources.Count > 0)
        {
            // An edit is drawn at the size its model reads the first picture at, so it names no size of its own.
            if ((request.Width, request.Height) != (0, 0))
                throw new HostRequestException(HostErrorKind.Invalid, "An edit is drawn at the size its model reads its first picture at; it names no size (0x0).");
            request = EditFitting.Fitted(request, method.ReadSize);
        }
        else
        {
            if (request.Mask is not null)
                throw new HostRequestException(HostErrorKind.Invalid, "A mask lies over a picture to work from.");
            if (!method.Draws(request.Width, request.Height))
                throw new HostRequestException(HostErrorKind.Invalid, $"{request.Width}x{request.Height} is not a size this model draws at.");
        }

        Task Stage(string stage) => reply(new Progress(request.RequestId, stage, 0, 0));
        var model = await requests.DrawingAsync(entry, request.Lora, Stage, ct);

        using var room = models.BorrowRoom();
        var rgb = await method.DrawAsync(request, model, Stage,
                                         (done, total) => reply(new Progress(request.RequestId, Drawing, done, total)), room, ct);
        await reply(new Drawn(request.RequestId, request.Width, request.Height, rgb));
    }
}
