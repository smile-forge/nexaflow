using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Checking;

/// <summary>
/// Checks how fit a picture is for a general audience: the picture read whatever its format, through the image safety
/// classifier on the CPU (<see cref="PictureSafetyClassifier"/>). Nothing here waits on the GPU.
/// </summary>
internal sealed class CheckPictureHandler(ModelRegistry models) : HostRequestHandler<CheckPicture>
{
    protected override async Task HandleAsync(CheckPicture request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        RgbPicture picture;
        try { picture = PictureDecoder.DecodeRgb(request.Picture); }
        catch (InvalidDataException ex) { throw new HostRequestException(HostErrorKind.Invalid, $"The picture to check could not be read: {ex.Message}"); }

        using var lease = await models.AcquireAsync(request.Classifier, resident: false, stage => reply(new Progress(request.RequestId, stage, 0, 0)), ct);
        ct.ThrowIfCancellationRequested();
        var (gore, sexual, neither) = lease.As<PictureSafetyClassifier>().Check(picture);
        await reply(new PictureChecked(request.RequestId, gore, sexual, neither));
    }
}
