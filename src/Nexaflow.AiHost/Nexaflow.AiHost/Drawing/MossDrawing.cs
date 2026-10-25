using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Draws with MOSS-Image's three parts: the prompt and the negative prompt read once, then the sampler's steps through
/// the denoiser, then the latents decoded. Its own sampler takes fifty Euler steps, the denoiser asked at each with the
/// prompt and, block 22 left out, with the negative prompt — each a turn on the GPU at the request's priority. A LoRA
/// is active on every pass at its strength; a distillation's LoRA brings its own sampler (<see cref="DataSpaceSampler"/>),
/// one pass a step and no negative prompt. Each part is held only for its own stage, so a picture needs room for its
/// largest part, not all three at once.
/// </summary>
internal sealed class MossDrawing(ModelRegistry models, GpuScheduler gpu) : IDrawingMethod
{
    public DrawingMethod Method => DrawingMethod.MossImage;

    public bool Draws(int width, int height) => MossSampler.Draws(width, height);

    public bool TakesNegativePrompts => true;

    public bool TakesLoras => true;

    public async Task<byte[]> DrawAsync(Draw request, Func<string, Task> stage, Func<int, int, Task> stepped, ModelRegistry.Room room, CancellationToken ct)
    {
        var model = request.Model;
        var lane  = gpu.Open(request.Priority);
        int h = request.Height / MossSampler.PixelsPerLatent, w = request.Width / MossSampler.PixelsPerLatent;
        var distilled = request.Lora?.Sampler is { } file ? DataSpaceSampler.Read(file) : null;
        if (distilled is not null && request.NegativePrompt is not null)
            throw new HostRequestException(HostErrorKind.Invalid, "A distillation draws with no negative prompt.");
        int steps = distilled?.StepCodes.Count ?? model.Steps;

        (float[] Caption, int Rows, int Width) prompt;
        (float[] Caption, int Rows, int Width)? negative = null;
        var encoderSpec = model.Parts[DrawingParts.PromptEncoder];
        using (var lease = await models.AcquireAsync(encoderSpec, resident: false, stage, ct, room))
        {
            var encoder = lease.As<MossPromptEncoder>();
            await stepped(0, steps);
            using (await lane.NextAsync(encoderSpec.Device, ct))
            {
                ct.ThrowIfCancellationRequested();
                prompt = encoder.Encode(encoder.Tokenize(request.Prompt));
                if (distilled is null) negative = encoder.Encode(encoder.Tokenize(request.NegativePrompt ?? ""));
            }
        }

        float[] latents;
        var denoiserSpec = model.Parts[DrawingParts.Denoiser];
        using (var lease = await models.AcquireAsync(denoiserSpec, resident: false, stage, ct, room))
        {
            var denoiser = lease.As<MossDenoiser>();
            using var lora = request.Lora is { } chosen ? denoiser.LoadLora(chosen.Adapter) : null;
            float strength = request.Lora?.Strength ?? 0f;
            var noise = MossSampler.Noise(request.Seed, h, w);
            var rope  = MossSampler.Rope(h, w, prompt.Rows);
            latents = distilled is null
                ? await GuidedAsync(denoiser, denoiserSpec.Device, lane, noise, h, w, prompt, negative!.Value, rope, lora, strength, model, stepped, ct)
                : await DistilledAsync(denoiser, denoiserSpec.Device, lane, distilled, noise, h, w, prompt, rope, lora, strength, stepped, ct);
        }

        float[] pixels;
        var decoderSpec = model.Parts[DrawingParts.Decoder];
        using (var lease = await models.AcquireAsync(decoderSpec, resident: false, stage, ct, room))
        using (await lane.NextAsync(decoderSpec.Device, ct))
        {
            ct.ThrowIfCancellationRequested();
            pixels = lease.As<MossDecoder>().Decode(latents, h, w);
        }
        return DecodedPixels.Rgb(pixels, request.Height, request.Width);
    }

    /// <summary>MOSS-Image's own sampler: Euler over its snapped noise levels, each step asking with the prompt and,
    /// block 22 left out, with the negative prompt, and steering by the model's guidance.</summary>
    private static async Task<float[]> GuidedAsync(MossDenoiser denoiser, int? device, GpuScheduler.Lane lane, float[] noise, int h, int w,
                                                   (float[] Caption, int Rows, int Width) prompt, (float[] Caption, int Rows, int Width) negative,
                                                   float[] rope, OrtLoraAdapter? lora, float strength, DrawingModel model,
                                                   Func<int, int, Task> stepped, CancellationToken ct)
    {
        var latents = noise;
        var negativeRope = MossSampler.Rope(h, w, negative.Rows);
        var sigmas = MossSampler.Sigmas(model.Steps, model.Shift);
        int steps = sigmas.Length - 1;
        for (int step = 0; step < steps; step++)
        {
            var time = MossSampler.TimeRows(MossSampler.Timestep(sigmas[step]));
            float[] withPrompt, withNegative;
            using (await lane.NextAsync(device, ct))
            {
                ct.ThrowIfCancellationRequested();
                withPrompt = denoiser.Velocity(latents, h, w, prompt.Caption, prompt.Rows, rope, time, skip: false, lora, strength, lastStep: false);
            }
            using (await lane.NextAsync(device, ct))
            {
                ct.ThrowIfCancellationRequested();
                withNegative = denoiser.Velocity(latents, h, w, negative.Caption, negative.Rows, negativeRope, time, skip: true, lora, strength,
                                                 lastStep: step == steps - 1);
            }
            MossSampler.Step(latents, withPrompt, withNegative, model.Guidance, sigmas[step + 1] - sigmas[step]);
            await stepped(step + 1, steps);
        }
        return latents;
    }

    /// <summary>A distillation's sampler: one pass a step, at z + Q·x0 with the step's code, the clean estimate after
    /// each taken as z − v.</summary>
    private static async Task<float[]> DistilledAsync(MossDenoiser denoiser, int? device, GpuScheduler.Lane lane, DataSpaceSampler sampler, float[] noise,
                                                      int h, int w, (float[] Caption, int Rows, int Width) prompt, float[] rope,
                                                      OrtLoraAdapter? lora, float strength, Func<int, int, Task> stepped, CancellationToken ct)
    {
        var clean = new float[noise.Length];
        int steps = sampler.StepCodes.Count;
        for (int step = 0; step < steps; step++)
        {
            var input = sampler.Input(noise, clean);
            var time  = MossSampler.TimeRows(sampler.StepCodes[step]);
            float[] velocity;
            using (await lane.NextAsync(device, ct))
            {
                ct.ThrowIfCancellationRequested();
                velocity = denoiser.Velocity(input, h, w, prompt.Caption, prompt.Rows, rope, time, skip: false, lora, strength,
                                             lastStep: step == steps - 1);
            }
            DataSpaceSampler.Estimate(clean, noise, velocity);
            await stepped(step + 1, steps);
        }
        return clean;
    }
}
