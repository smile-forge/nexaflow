using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Draws with FLUX.2 [klein]'s four parts: the prompt read once, each picture to work from read once, then the sampler's
/// Euler steps through the denoiser — which reads those pictures' tokens beside the latents at every step — then the
/// latents decoded. Under a mask, after each step the first picture is put back, noised to the level reached, where the
/// mask leaves it, as diffusers' inpaint pipeline does; so a fill draws again only what it covers. Each part is held only
/// for its own stage, so a picture needs room for its largest part, not all four at once.
/// </summary>
internal sealed class KleinDrawing(ModelRegistry models, GpuScheduler gpu) : IDrawingMethod
{
    /// <summary>The pictures to work from it takes: FLUX.2 [klein]'s multi-reference editing, within its token budget.</summary>
    public const int Sources = 4;

    public DrawingMethod Method => DrawingMethod.Flux2Klein;

    public bool Draws(int width, int height) => KleinSampler.Draws(width, height);

    public bool TakesNegativePrompts => false;

    public bool TakesLoras => false;

    public int MaxSources => Sources;

    public bool Masks => true;

    public async Task<byte[]> DrawAsync(Draw request, Func<string, Task> stage, Func<int, int, Task> stepped, ModelRegistry.Room room, CancellationToken ct)
    {
        var model = request.Model;
        var lane  = gpu.Open(request.Priority);
        int rows = request.Height / KleinSampler.PixelsPerToken, cols = request.Width / KleinSampler.PixelsPerToken, tokens = rows * cols;

        float[] prompt;
        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.PromptEncoder], resident: false, stage, ct, room))
        {
            var encoder = lease.As<KleinPromptEncoder>();
            await stepped(0, model.Steps);
            using (await lane.NextAsync(model.Parts[DrawingParts.PromptEncoder].Device, ct))
            {
                ct.ThrowIfCancellationRequested();
                prompt = encoder.Encode(encoder.Tokenize(request.Prompt));
            }
        }

        var sources = new List<float[]>();
        if (request.Sources.Count > 0)
            using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.PictureEncoder], resident: false, stage, ct, room))
            {
                var encoder = lease.As<KleinPictureEncoder>();
                foreach (var source in request.Sources)
                    using (await lane.NextAsync(model.Parts[DrawingParts.PictureEncoder].Device, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        sources.Add(encoder.Encode(source.Rgb, source.Width, source.Height));
                    }
            }

        var positions = KleinSampler.ImagePositions(rows, cols,
            [.. request.Sources.Select(s => (s.Height / KleinSampler.PixelsPerToken, s.Width / KleinSampler.PixelsPerToken))]);
        var promptPositions = KleinSampler.PromptPositions();
        var mask    = request.Mask is { } covered ? KleinSampler.TokenMask(covered, request.Width, request.Height) : null;
        var sigmas  = KleinSampler.Sigmas(model.Steps, tokens);
        var noise   = KleinSampler.Noise(request.Seed, tokens);
        var latents = noise.ToArray();

        // The denoiser's input: the latents, then every picture's tokens, which stay as they are from step to step.
        int own = tokens * KleinSampler.Channels;
        var input = new float[own + sources.Sum(s => s.Length)];
        for (int i = 0, at = own; i < sources.Count; at += sources[i].Length, i++) sources[i].CopyTo(input, at);

        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.Denoiser], resident: false, stage, ct, room))
        {
            var denoiser = lease.As<KleinDenoiser>();
            for (int step = 0; step < model.Steps; step++)
            {
                latents.CopyTo(input, 0);
                float[] velocity;
                using (await lane.NextAsync(model.Parts[DrawingParts.Denoiser].Device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    velocity = denoiser.Velocity(input, positions, prompt, promptPositions, sigmas[step], lastStep: step == model.Steps - 1);
                }
                KleinSampler.Step(latents, velocity, sigmas[step + 1] - sigmas[step]);
                if (mask is not null)
                    KleinSampler.Keep(latents, step == model.Steps - 1 ? sources[0] : KleinSampler.Noised(sources[0], noise, sigmas[step + 1]), mask);
                await stepped(step + 1, model.Steps);
            }
        }

        float[] pixels;
        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.Decoder], resident: false, stage, ct, room))
        using (await lane.NextAsync(model.Parts[DrawingParts.Decoder].Device, ct))
        {
            ct.ThrowIfCancellationRequested();
            pixels = lease.As<KleinDecoder>().Decode(latents, rows, cols);
        }
        return DecodedPixels.Rgb(pixels, request.Height, request.Width);
    }
}
