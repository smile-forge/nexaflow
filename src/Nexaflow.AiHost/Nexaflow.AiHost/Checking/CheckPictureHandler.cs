using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;


namespace Nexaflow.AiHost.Checking;

/// <summary>
/// Checks how fit a picture is for a general audience: the picture's pixels, through the image safety
/// classifier on the CPU (<see cref="PictureSafetyClassifier"/>). Nothing here waits on the GPU.
/// </summary>
internal sealed class CheckPictureHandler(HeldModels models) : HostRequestHandler<CheckPicture>
{
    protected override async Task HandleAsync(CheckPicture request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        if (request.Width <= 0 || request.Height <= 0 || request.Rgb.Length != request.Width * request.Height * 3)
            throw new HostRequestException(HostErrorKind.Invalid, $"A {request.Width}x{request.Height} picture is not {request.Rgb.Length} bytes of RGB.");

        using var lease = await models.AcquireAsync(models.ToolPart(request.Tool, CheckingParts.SafetyClassifier), stage => reply(new Progress(request.RequestId, stage, 0, 0)), ct);
        ct.ThrowIfCancellationRequested();
        var (gore, sexual, neither) = lease.As<PictureSafetyClassifier>().Check(request.Rgb.Span, request.Width, request.Height);
        await reply(new PictureChecked(request.RequestId, gore, sexual, neither));
    }
}
