using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Video;

namespace Nexaflow.AiHost.Filming;

/// <summary>
/// One way of making a clip: a family of video model — the parts it has (<see cref="FilmingParts"/>) and the sampler that
/// drives them. Each takes the GPU a call at a time at the request's priority, and holds a part only while it needs it,
/// in the room the clip borrowed. A new family of video model is a new filming method.
/// </summary>
internal interface IFilmingMethod
{
    /// <summary>Whether it films the way <paramref name="method"/> names.</summary>
    bool Films(FilmingMethod method);

    /// <summary>Whether it makes clips of this size and length.</summary>
    bool Films(int width, int height, int frames);

    /// <summary>Makes <paramref name="request"/>'s clip, handing its frames over as they are decoded.</summary>
    Task FilmAsync(Film request, FilmingCallbacks report, ModelRegistry.Room room, CancellationToken ct);
}

/// <summary>What a filming method tells as it goes.</summary>
/// <param name="Stage">A part loading, by what it is doing.</param>
/// <param name="Stepped">Each denoising step as it finishes: how many are done, of how many.</param>
/// <param name="Decoded">Each latent frame decoded: how many are done, of how many.</param>
/// <param name="Frames">The frames a latent frame decoded to, in order: interleaved 8-bit RGB, one after another.</param>
internal sealed record FilmingCallbacks(Func<string, Task> Stage, Func<int, int, Task> Stepped, Func<int, int, Task> Decoded, Func<byte[], Task> Frames);

/// <summary>
/// Makes a clip by the method its model films with (<see cref="IFilmingMethod"/>) and writes it as an H.264 MP4 as its
/// frames are decoded, so the clip is never held whole as pictures. The method takes the GPU a call at a time at the
/// request's priority, so a reply or speech gets in between, and each denoising step and decoded latent frame is
/// reported. Like a picture, a clip borrows the room its largest part needs: residents nothing is using make way on its
/// parts' GPUs, and load again after it.
/// </summary>
internal sealed class FilmHandler(ModelRegistry models, IReadOnlyList<IFilmingMethod> methods) : HostRequestHandler<Film>
{


    protected override async Task HandleAsync(Film request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        var method = methods.FirstOrDefault(m => m.Films(request.Model.Method))
                     ?? throw new HostRequestException(HostErrorKind.Invalid, $"This host does not film the {request.Model.Method} way.");
        if (!method.Films(request.Width, request.Height, request.Frames))
            throw new HostRequestException(HostErrorKind.Invalid, $"{request.Frames} frames at {request.Width}x{request.Height} is not a clip this model makes.");
        if (request.FramesPerSecond <= 0)
            throw new HostRequestException(HostErrorKind.Invalid, "A clip needs a frame rate.");
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new HostRequestException(HostErrorKind.Invalid, "Filming needs a prompt.");
        if (request.FirstFrame is { } first && first.Length != request.Width * request.Height * 3)
            throw new HostRequestException(HostErrorKind.Invalid, $"The first frame is not {request.Width}x{request.Height} RGB.");

        int frameBytes = request.Width * request.Height * 3;
        using var room = models.BorrowRoom(ids => reply(new MadeWay(request.RequestId, ids)));
        using var mp4 = new MemoryStream();
        using (var encoder = new Mp4Encoder(mp4, request.Width, request.Height, request.FramesPerSecond))
        {
            var report = new FilmingCallbacks(
                stage => reply(new Progress(request.RequestId, stage, 0, 0)),
                (done, total) => reply(new Progress(request.RequestId, FilmingStages.Filming, done, total)),
                (done, total) => reply(new Progress(request.RequestId, FilmingStages.Decoding, done, total)),
                rgb =>
                {
                    for (int at = 0; at < rgb.Length; at += frameBytes) encoder.AddFrame(rgb.AsSpan(at, frameBytes));
                    return Task.CompletedTask;
                });
            await method.FilmAsync(request, report, room, ct);
            encoder.Finish();
        }
        await reply(new Filmed(request.RequestId, mp4.ToArray()));
    }
}
