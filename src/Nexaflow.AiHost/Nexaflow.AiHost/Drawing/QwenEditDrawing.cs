using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Sampling;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Edits with Qwen-Image-Edit's five parts, as diffusers' QwenImageEditPlusPipeline does: each picture worked from read
/// small by the picture reader, the instruction and the empty prompt guidance steers from each read with those pictures
/// by the prompt encoder, each picture read whole by the picture encoder; then the sampler's Euler steps through the
/// denoiser — asked with the prompt and without it, the pictures' latents beside the edit's at every step — then the
/// latents decoded. Under a mask, after each step the first picture is put back, noised to the level reached, where the
/// mask leaves it, as diffusers' inpaint pipeline does. Each part is held only for its own stage, so an edit needs room
/// for its largest part, not all five at once. It draws nothing from a prompt alone.
/// </summary>
internal sealed class QwenEditDrawing(ModelRegistry models, GpuScheduler gpu) : IDrawingMethod
{
    public DrawingMethod Method => DrawingMethod.QwenImageEdit;

    public bool Draws(int width, int height) => false;

    public bool Reads(int width, int height) => QwenEditSampler.Reads(width, height);

    public bool TakesNegativePrompts => false;

    public bool TakesLoras => false;

    public int MaxSources => QwenEditSampler.MaxSources;

    public bool Masks => true;

    public async Task<byte[]> DrawAsync(Draw request, Func<string, Task> stage, Func<int, int, Task> stepped, ModelRegistry.Room room, CancellationToken ct)
    {
        var model = request.Model;
        var lane  = gpu.Open(request.Priority);
        int rows = request.Height / QwenEditSampler.PixelsPerToken, cols = request.Width / QwenEditSampler.PixelsPerToken, tokens = rows * cols;

        // Each picture as the vision tower reads it: resized within its area as the pipeline resizes it, then again to the
        // processor's multiple as the processor does.
        var pictures = new List<(float[] Embeds, int Rows, int Cols)>(request.Sources.Count);
        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.PictureReader], resident: false, stage, ct, room))
        {
            var reader = lease.As<QwenEditPictureReader>();
            await stepped(0, model.Steps);
            foreach (var source in request.Sources)
            {
                var (conditionWidth, conditionHeight) = QwenEditSampler.SizeWithin(QwenEditSampler.ConditionArea, source.Width, source.Height);
                var condition = PictureResampling.ResizeAsPil(source.Rgb, source.Width, source.Height, 3, conditionWidth, conditionHeight, PilFilter.Lanczos);
                var (readWidth, readHeight) = QwenEditSampler.ProcessorSize(conditionWidth, conditionHeight);
                var read = PictureResampling.ResizeAsTorch(condition, conditionWidth, conditionHeight, 3, readWidth, readHeight, PilFilter.Bicubic);
                using (await lane.NextAsync(model.Parts[DrawingParts.PictureReader].Device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var (embeds, _) = reader.Read(read, readWidth, readHeight);
                    int merge = QwenEditSampler.ReaderPatch * QwenEditSampler.ReaderMerge;
                    pictures.Add((embeds, readHeight / merge, readWidth / merge));
                }
            }
        }

        float[] prompt, empty;
        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.PromptEncoder], resident: false, stage, ct, room))
        {
            var encoder = lease.As<QwenEditPromptEncoder>();
            using (await lane.NextAsync(model.Parts[DrawingParts.PromptEncoder].Device, ct))
            {
                ct.ThrowIfCancellationRequested();
                prompt = encoder.Encode(request.Prompt, pictures);
            }
            using (await lane.NextAsync(model.Parts[DrawingParts.PromptEncoder].Device, ct))
            {
                ct.ThrowIfCancellationRequested();
                empty = encoder.Encode(" ", pictures);
            }
        }

        // Each picture's latents, read at its own size within the VAE's area — the provider sends each at it.
        var sources = new List<(float[] Tokens, int Rows, int Cols)>(request.Sources.Count);
        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.PictureEncoder], resident: false, stage, ct, room))
        {
            var encoder = lease.As<QwenEditPictureEncoder>();
            foreach (var source in request.Sources)
            {
                var (width, height) = QwenEditSampler.SizeWithin(QwenEditSampler.VaeArea, source.Width, source.Height);
                var rgb = (width, height) == (source.Width, source.Height)
                    ? source.Rgb
                    : PictureResampling.ResizeAsPil(source.Rgb, source.Width, source.Height, 3, width, height, PilFilter.Lanczos);
                using (await lane.NextAsync(model.Parts[DrawingParts.PictureEncoder].Device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    sources.Add((encoder.Encode(rgb, width, height), height / QwenEditSampler.PixelsPerToken, width / QwenEditSampler.PixelsPerToken));
                }
            }
        }

        (int Rows, int Cols)[] grids = [(rows, cols), .. sources.Select(s => (s.Rows, s.Cols))];
        var positions = QwenEditSampler.ImagePositions(grids);
        var promptPositions = QwenEditSampler.DenoiserPromptPositions(prompt.Length / QwenEditPictureReader.TokenWidth, grids);
        var emptyPositions = QwenEditSampler.DenoiserPromptPositions(empty.Length / QwenEditPictureReader.TokenWidth, grids);
        var sigmas  = QwenEditSampler.Sigmas(model.Steps, tokens);
        var noise   = QwenEditSampler.Noise(request.Seed, tokens);
        var latents = noise.ToArray();
        var mask    = request.Mask is { } covered ? QwenEditSampler.TokenMask(covered, request.Width, request.Height) : null;
        var kept    = mask is null ? null : sources[0].Tokens;
        if (kept is not null && kept.Length != latents.Length)
            throw new HostRequestException(HostErrorKind.Invalid, "A mask lies over a first picture read at the size of the edit.");

        // The denoiser's input: the latents, then every picture's, which stay as they are from step to step.
        int own = tokens * QwenEditSampler.Channels;
        var input = new float[own + sources.Sum(s => s.Tokens.Length)];
        for (int i = 0, at = own; i < sources.Count; at += sources[i].Tokens.Length, i++) sources[i].Tokens.CopyTo(input, at);

        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.Denoiser], resident: false, stage, ct, room))
        {
            var denoiser = lease.As<QwenEditDenoiser>();
            int? device = model.Parts[DrawingParts.Denoiser].Device;
            for (int step = 0; step < model.Steps; step++)
            {
                latents.CopyTo(input, 0);
                bool last = step == model.Steps - 1;
                float[] withPrompt, withoutPrompt;
                using (await lane.NextAsync(device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    withPrompt = denoiser.Velocity(input, positions, tokens, prompt, promptPositions, sigmas[step], lastStep: false);
                }
                using (await lane.NextAsync(device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    withoutPrompt = denoiser.Velocity(input, positions, tokens, empty, emptyPositions, sigmas[step], lastStep: last);
                }
                var velocity = QwenEditSampler.Guided(withPrompt.AsSpan(0, own), withoutPrompt.AsSpan(0, own), model.Guidance);
                QwenEditSampler.Step(latents, velocity, sigmas[step + 1] - sigmas[step]);
                if (mask is not null && kept is not null)
                    FlowInpainting.Keep(latents, last ? kept : FlowInpainting.Noised(kept, noise, sigmas[step + 1]), mask, 1);
                await stepped(step + 1, model.Steps);
            }
        }

        using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.Decoder], resident: false, stage, ct, room))
        using (await lane.NextAsync(model.Parts[DrawingParts.Decoder].Device, ct))
        {
            ct.ThrowIfCancellationRequested();
            return lease.As<QwenEditDecoder>().Decode(latents, rows, cols);
        }
    }
}
