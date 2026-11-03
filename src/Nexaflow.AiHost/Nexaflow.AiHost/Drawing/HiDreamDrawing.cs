using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Sampling;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Draws with HiDream-O1. Its denoiser is held for the whole picture; at each of the sampler's steps it is asked twice —
/// with the prompt, and with the empty prompt guidance steers away from — each a turn on the GPU at the request's
/// priority, so speech and replies get in between, and HiDream's UniPC sampler steps between steps. The finished tokens
/// are the picture's pixels: there is nothing to decode.
/// An edit first reads each picture it works from with the picture reader, into both prompts, as HiDream's pipeline does:
/// one picture as it comes, several each fitted within the square HiDream gives that many; then every step reads them
/// beside the picture drawn as clean patches. Under a mask, after each step the first picture is put back, noised to the
/// level reached, where the mask leaves it (<see cref="FlowInpainting"/>), so a fill draws again only what it covers.
/// </summary>
internal sealed class HiDreamDrawing(ModelRegistry models, GpuScheduler gpu) : IDrawingMethod
{
    public DrawingMethod Method => DrawingMethod.HiDream;

    public bool Draws(int width, int height) => HiDreamSampler.Draws(width, height);

    public bool Reads(int width, int height) => HiDreamSampler.Reads(width, height);

    public bool TakesNegativePrompts => false;

    public bool TakesLoras => false;

    public int MaxSources => HiDreamSampler.MaxSources;

    public bool Masks => true;

    public async Task<byte[]> DrawAsync(Draw request, Func<string, Task> stage, Func<int, int, Task> stepped, ModelRegistry.Room room, CancellationToken ct)
    {
        var model = request.Model;
        var lane  = gpu.Open(request.Priority);
        int rows = request.Height / HiDreamSampler.Patch, cols = request.Width / HiDreamSampler.Patch;
        int own = rows * cols * HiDreamSampler.TokenWidth;

        var references = References(request);
        var pictures = new List<HiDreamPictureTokens>(references.Count);
        if (references.Count > 0)
            using (var lease = await models.AcquireAsync(model.Parts[DrawingParts.PictureReader], resident: false, stage, ct, room))
            {
                var reader = lease.As<HiDreamPictureReader>();
                foreach (var (rgb, width, height) in references)
                {
                    var (readWidth, readHeight) = HiDreamSampler.ConditionSize(width, height);
                    var read = PictureResampling.ResizeAsPil(rgb, width, height, 3, readWidth, readHeight, PilFilter.Lanczos);
                    using (await lane.NextAsync(model.Parts[DrawingParts.PictureReader].Device, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        pictures.Add(reader.Read(read, readWidth, readHeight));
                    }
                }
            }

        var positions = references.Count == 0
            ? HiDreamSampler.ImagePositions(rows, cols)
            : HiDreamSampler.ImagePositions(rows, cols, [.. references.Select(r => (r.Height / HiDreamSampler.Patch, r.Width / HiDreamSampler.Patch))]);
        var sampler = FlowUniPc.ShiftedTwice(model.Steps, model.Shift);
        var noise   = HiDreamSampler.Noise(request.Seed, rows * cols);
        var sample  = noise.ToArray();

        // The denoiser's input after the picture drawn: each picture worked from, clean, the same at every step.
        float[]? input = null;
        if (references.Count > 0)
        {
            input = new float[own + references.Sum(r => HiDreamSampler.Tokens(r.Width, r.Height) * HiDreamSampler.TokenWidth)];
            int at = own;
            foreach (var (rgb, width, height) in references)
            {
                var clean = HiDreamSampler.TokensOf(rgb, width, height);
                clean.CopyTo(input, at);
                at += clean.Length;
            }
        }
        var mask = request.Mask is { } covered ? HiDreamSampler.PixelMask(covered, request.Width, request.Height) : null;
        var kept = mask is null ? null : HiDreamSampler.TokensOf(request.Sources[0].Rgb, request.Width, request.Height);

        int? device = model.Parts[DrawingParts.Denoiser].Device;
        using var denoiserLease = await models.AcquireAsync(model.Parts[DrawingParts.Denoiser], resident: false, stage, ct, room);
        var denoiser = denoiserLease.As<HiDreamDenoiser>();
        var prompt = denoiser.Read(request.Prompt, pictures);
        var empty  = denoiser.Read(" ", pictures);
        await stepped(0, model.Steps);
        for (int step = 0; step < model.Steps; step++)
        {
            int timestep = sampler.Timesteps[step];
            float t      = HiDreamSampler.TimestepInput(timestep);
            bool last    = step == model.Steps - 1;
            if (input is not null) sample.CopyTo(input, 0);
            var tokens = input ?? sample;
            float[] withPrompt, withoutPrompt;
            using (await lane.NextAsync(device, ct))
            {
                ct.ThrowIfCancellationRequested();
                withPrompt = denoiser.Predict(prompt, tokens, positions, t, lastStep: false);
            }
            using (await lane.NextAsync(device, ct))
            {
                ct.ThrowIfCancellationRequested();
                withoutPrompt = denoiser.Predict(empty, tokens, positions, t, lastStep: last);
            }
            sample = sampler.Step(HiDreamSampler.Guided(withPrompt.AsSpan(0, own), withoutPrompt.AsSpan(0, own), sample, timestep, model.Guidance), sample);
            if (mask is not null && kept is not null)
                FlowInpainting.Keep(sample, last ? kept : FlowInpainting.Noised(kept, noise, sampler.Sigmas[step + 1]), mask, 1);
            await stepped(step + 1, model.Steps);
        }
        return HiDreamSampler.Rgb(sample, rows, cols);
    }

    /// <summary>
    /// The pictures an edit works from at the sizes HiDream reads them as clean patches: one alone as it comes, where it
    /// is the size the edit is drawn at — HiDream drawing at a single picture's own proportions — and otherwise each fitted
    /// to whole patches within the square HiDream gives that many (<see cref="HiDreamSampler.ReferenceSide"/>).
    /// </summary>
    private static List<(byte[] Rgb, int Width, int Height)> References(Draw request)
    {
        var sources = request.Sources;
        if (sources.Count == 1 && (sources[0].Width, sources[0].Height) == (request.Width, request.Height))
            return [(sources[0].Rgb, sources[0].Width, sources[0].Height)];
        if (sources.Count == 0) return [];
        int side = HiDreamSampler.ReferenceSide(sources.Count, request.Width, request.Height);
        return [.. sources.Select(s => PatchFitting.Fit(s.Rgb, s.Width, s.Height, side, HiDreamSampler.Patch))];
    }
}
