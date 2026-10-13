using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Draws a picture with an image model's three parts: the prompt read once, then the sampler's steps through the
/// denoiser, then the latents decoded and written as a PNG. Each of those takes the GPU as one step at the request's
/// priority, so a reply or speech gets in between denoising steps; each denoising step is reported as it finishes. A
/// part is held only while its stage runs, so the host needs room for the largest of them, not all three.
/// </summary>
internal sealed class DrawHandler(ModelRegistry models, GpuScheduler gpu) : HostRequestHandler<Draw>
{
    public const string Drawing = "drawing";

    protected override async Task HandleAsync(Draw request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        if (!ZImageSampler.Draws(request.Width, request.Height))
            throw new HostRequestException(HostErrorKind.Invalid, $"{request.Width}x{request.Height} is not a size this model draws at.");
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new HostRequestException(HostErrorKind.Invalid, "Drawing needs a prompt.");

        Task Stage(string stage) => reply(new Progress(request.RequestId, stage, 0, 0));
        var model = request.Model;
        int h = request.Height / ZImageSampler.PixelsPerLatent, w = request.Width / ZImageSampler.PixelsPerLatent;

        // Each part is held only for its own stage: once a stage is done its part is idle, and the registry may unload it
        // to make room for the next — so a picture needs room for its largest part, not all three at once.
        (float[] Caption, int Rows, int Width) caption;
        using (var lease = await models.AcquireAsync(model.PromptEncoder, resident: false, Stage, ct))
        {
            var encoder = lease.As<ZImagePromptEncoder>();
            await reply(new Progress(request.RequestId, Drawing, 0, model.Steps));
            using (await gpu.NextAsync(request.Priority, ct))
            {
                ct.ThrowIfCancellationRequested();
                caption = encoder.Encode(encoder.Tokenize(request.Prompt));
            }
        }
        var (rows, padding, rowPositions) = ZImageSampler.PromptRows(caption.Caption, caption.Rows, caption.Width);
        var positions = ZImageSampler.ImagePositions(h, w, padding.Length);

        var sigmas  = ZImageSampler.Sigmas(model.Steps, model.Shift);
        var latents = ZImageSampler.Noise(request.Seed, h, w);
        using (var lease = await models.AcquireAsync(model.Denoiser, resident: false, Stage, ct))
        {
            var denoiser = lease.As<ZImageDenoiser>();
            for (int step = 0; step < model.Steps; step++)
            {
                float[] velocity;
                using (await gpu.NextAsync(request.Priority, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    velocity = denoiser.Velocity(ZImageSampler.Patchify(latents, h, w), positions, rows, padding, rowPositions,
                                                 1f - sigmas[step], lastStep: step == model.Steps - 1);
                }
                ZImageSampler.Step(latents, velocity, h, w, sigmas[step + 1] - sigmas[step]);
                await reply(new Progress(request.RequestId, Drawing, step + 1, model.Steps));
            }
        }

        float[] pixels;
        using (var lease = await models.AcquireAsync(model.Decoder, resident: false, Stage, ct))
        using (await gpu.NextAsync(request.Priority, ct))
        {
            ct.ThrowIfCancellationRequested();
            pixels = lease.As<ZImageDecoder>().Decode(latents, h, w);
        }
        var png = PngEncoder.EncodeRgb(ZImageSampler.Rgb(pixels, request.Height, request.Width), request.Width, request.Height);
        await reply(new Drawn(request.RequestId, png));
    }
}
