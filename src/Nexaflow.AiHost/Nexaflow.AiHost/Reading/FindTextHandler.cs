using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Reading;

/// <summary>
/// Finds where a picture holds writing: the picture prepared as PaddleOCR prepares it, through PP-OCRv5's detector on
/// the CPU, and its map read into regions (<see cref="TextFinding"/>). Nothing here waits on the GPU.
/// </summary>
internal sealed class FindTextHandler(ModelRegistry models) : HostRequestHandler<FindTextInPicture>
{
    protected override async Task HandleAsync(FindTextInPicture request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        if (request.Width <= 0 || request.Height <= 0)
            throw new HostRequestException(HostErrorKind.Invalid, "A picture to find text in has a size.");
        if (request.Pixels.Length != (long)request.Width * request.Height * 4)
            throw new HostRequestException(HostErrorKind.Invalid, $"A {request.Width}x{request.Height} picture is not {request.Pixels.Length} bytes of RGBA.");

        var (input, width, height) = TextFinding.Prepare(request.Pixels.Span, request.Width, request.Height);
        using var lease = await models.AcquireAsync(request.Finder, resident: false, stage => reply(new Progress(request.RequestId, stage, 0, 0)), ct);
        ct.ThrowIfCancellationRequested();
        var map = lease.As<TextFinder>().Find(input, width, height);
        await reply(new TextFoundInPicture(request.RequestId, TextFinding.Regions(map, width, height)));
    }
}
