using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Sampling;

namespace Nexaflow.AiHost.Filming;

/// <summary>
/// Films with Wan2.2-TI2V's parts: the prompt read once (and the negative prompt, for a guided model), the picture to
/// start from read into the first latent frame, then the sampler's steps through the denoiser — the full model's guided
/// UniPC steps, or a distillation's few steps — then the latents decoded a latent frame at a time. Each part is held only
/// for its own stage, so a clip needs room for its largest part, not all four; and each call takes the GPU at the
/// request's priority, so speech and replies get in between.
/// </summary>
internal sealed class WanFilming(ModelRegistry models, GpuScheduler gpu) : IFilmingMethod
{
    public bool Films(FilmingMethod method) => method is FilmingMethod.Wan or FilmingMethod.WanDistilled;

    public bool Films(int width, int height, int frames) => WanSampler.Films(width, height, frames);

    public async Task FilmAsync(Film request, FilmingCallbacks report, ModelRegistry.Room room, CancellationToken ct)
    {
        var model = request.Model;
        var lane  = gpu.Open(request.Priority);
        var shape = LatentShape.Of(request.Frames, request.Width, request.Height);
        bool guided = model.Method == FilmingMethod.Wan;
        int steps = guided ? model.Steps : model.Timesteps.Count;
        if (request.FirstFrame is not null && !FilmingParts.Of(model.Method).Contains(FilmingParts.FrameEncoder))
            throw new HostRequestException(HostErrorKind.Invalid, $"A model that films the {model.Method} way makes clips from a description alone.");

        float[] text;
        float[]? negative = null;
        var encoderSpec = model.Parts[FilmingParts.PromptEncoder];
        using (var lease = await models.AcquireAsync(encoderSpec, resident: false, report.Stage, ct, room))
        {
            var encoder = lease.As<WanPromptEncoder>();
            await report.Stepped(0, steps);
            using (await lane.NextAsync(encoderSpec.Device, ct))
            {
                ct.ThrowIfCancellationRequested();
                text = encoder.Encode(encoder.Tokenize(request.Prompt));
                if (guided) negative = encoder.Encode(encoder.Tokenize(model.NegativePrompt));
            }
        }

        float[]? firstFrame = null;
        if (request.FirstFrame is { } picture)
        {
            var frameSpec = model.Parts[FilmingParts.FrameEncoder];
            using var lease = await models.AcquireAsync(frameSpec, resident: false, report.Stage, ct, room);
            using (await lane.NextAsync(frameSpec.Device, ct))
            {
                ct.ThrowIfCancellationRequested();
                firstFrame = lease.As<WanFrameEncoder>().Encode(picture, request.Width, request.Height);
            }
        }

        var noise   = new ClipNoise(request.Seed);
        var latents = noise.Draw(shape.Values);
        var denoiserSpec = model.Parts[FilmingParts.Denoiser];
        using (var lease = await models.AcquireAsync(denoiserSpec, resident: false, report.Stage, ct, room))
        {
            var denoiser = lease.As<WanDenoiser>();
            if (guided)
            {
                var sampler = FlowUniPc.Shifted(model.Steps, model.Shift);
                for (int step = 0; step < steps; step++)
                {
                    if (firstFrame is not null) WanSampler.KeepFirstFrame(latents, firstFrame, shape);
                    var timesteps = WanSampler.Timesteps(shape.Frames, sampler.Timesteps[step], firstFrame is not null);
                    float[] withPrompt, withNegative;
                    using (await lane.NextAsync(denoiserSpec.Device, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        withPrompt = denoiser.Flow(latents, shape, timesteps, text, lastCall: false);
                    }
                    using (await lane.NextAsync(denoiserSpec.Device, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        withNegative = denoiser.Flow(latents, shape, timesteps, negative!, lastCall: step == steps - 1);
                    }
                    latents = sampler.Step(WanSampler.Guided(withPrompt, withNegative, model.Guidance), latents);
                    await report.Stepped(step + 1, steps);
                }
            }
            else
            {
                for (int step = 0; step < steps; step++)
                {
                    if (firstFrame is not null) WanSampler.KeepFirstFrame(latents, firstFrame, shape);
                    int timestep = model.Timesteps[step];
                    bool last = step == steps - 1;
                    float[] flow;
                    using (await lane.NextAsync(denoiserSpec.Device, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        flow = denoiser.Flow(latents, shape, WanSampler.Timesteps(shape.Frames, timestep, firstFrame is not null), text, lastCall: last);
                    }
                    double? next = last ? null : WanSampler.DistilledSigma(model.Timesteps[step + 1]);
                    latents = WanSampler.DistilledStep(latents, flow, WanSampler.DistilledSigma(timestep), next, last ? [] : noise.Draw(shape.Values));
                    await report.Stepped(step + 1, steps);
                }
            }
        }
        if (firstFrame is not null) WanSampler.KeepFirstFrame(latents, firstFrame, shape);

        var decoderSpec = model.Parts[FilmingParts.Decoder];
        using (var lease = await models.AcquireAsync(decoderSpec, resident: false, report.Stage, ct, room))
        using (var decoding = lease.As<WanDecoder>().Start(shape.Rows, shape.Cols))
        {
            for (int frame = 0; frame < shape.Frames; frame++)
            {
                byte[] rgb;
                using (await lane.NextAsync(decoderSpec.Device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    rgb = decoding.Next(WanSampler.LatentFrame(latents, shape, frame), lastCall: frame == shape.Frames - 1);
                }
                await report.Frames(rgb);
                await report.Decoded(frame + 1, shape.Frames);
            }
        }
    }
}
