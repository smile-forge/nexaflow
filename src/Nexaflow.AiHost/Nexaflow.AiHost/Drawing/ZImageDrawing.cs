using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Draws with Z-Image's three parts: the prompt read once, then the sampler's Euler steps through the denoiser, then the
/// latents decoded. Each part is held only for its own stage: once a stage is done its part is idle, and the registry
/// may unload it to make room for the next — so a picture needs room for its largest part, not all three at once.
/// </summary>
internal sealed class ZImageDrawing(ModelRegistry models, GpuScheduler gpu) : IDrawingMethod
{
    public DrawingMethod Method => DrawingMethod.ZImage;

    public bool Draws(int width, int height) => ZImageSampler.Draws(width, height);

    public async Task<byte[]> DrawAsync(Draw request, Func<string, Task> stage, Func<int, int, Task> stepped, ModelRegistry.Room room, CancellationToken ct)
    {
        var model = request.Model;
        int h = request.Height / ZImageSampler.PixelsPerLatent, w = request.Width / ZImageSampler.PixelsPerLatent;

        (float[] Caption, int Rows, int Width) caption;
        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.PromptEncoder], resident: false, stage, ct, room))
        {
            var encoder = lease.As<ZImagePromptEncoder>();
            await stepped(0, model.Steps);
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
        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.Denoiser], resident: false, stage, ct, room))
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
                await stepped(step + 1, model.Steps);
            }
        }

        float[] pixels;
        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.Decoder], resident: false, stage, ct, room))
        using (await gpu.NextAsync(request.Priority, ct))
        {
            ct.ThrowIfCancellationRequested();
            pixels = lease.As<ZImageDecoder>().Decode(latents, h, w);
        }
        return ZImageSampler.Rgb(pixels, request.Height, request.Width);
    }
}
