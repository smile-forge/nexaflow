using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Draws with HiDream-O1. Its one part, the denoiser, is held for the whole picture; at each of the sampler's steps it is
/// asked twice — with the prompt, and with the empty prompt guidance steers away from — each a turn on the GPU at the
/// request's priority, so speech and replies get in between, and HiDream's UniPC sampler steps between steps. The
/// finished tokens are the picture's pixels: there is nothing to decode.
/// </summary>
internal sealed class HiDreamDrawing(ModelRegistry models, GpuScheduler gpu) : IDrawingMethod
{
    public DrawingMethod Method => DrawingMethod.HiDream;

    public bool Draws(int width, int height) => HiDreamSampler.Draws(width, height);

    public async Task<byte[]> DrawAsync(Draw request, Func<string, Task> stage, Func<int, int, Task> stepped, ModelRegistry.Room room, CancellationToken ct)
    {
        var model = request.Model;
        int rows = request.Height / HiDreamSampler.Patch, cols = request.Width / HiDreamSampler.Patch;
        var positions = HiDreamSampler.ImagePositions(rows, cols);
        var sampler   = new HiDreamUniPc(model.Steps, model.Shift);
        var sample    = HiDreamSampler.Noise(request.Seed, rows * cols);

        using var lease = await models.AcquireAsync(model.Parts[DrawingParts.Denoiser], resident: false, stage, ct, room);
        var denoiser = lease.As<HiDreamDenoiser>();
        var prompt = denoiser.Read(request.Prompt);
        var empty  = denoiser.Read(" ");
        await stepped(0, model.Steps);
        for (int step = 0; step < model.Steps; step++)
        {
            int timestep = sampler.Timesteps[step];
            float input  = HiDreamSampler.TimestepInput(timestep);
            bool last    = step == model.Steps - 1;
            float[] withPrompt, withoutPrompt;
            using (await gpu.NextAsync(request.Priority, ct))
            {
                ct.ThrowIfCancellationRequested();
                withPrompt = denoiser.Predict(prompt, sample, positions, input, lastStep: false);
            }
            using (await gpu.NextAsync(request.Priority, ct))
            {
                ct.ThrowIfCancellationRequested();
                withoutPrompt = denoiser.Predict(empty, sample, positions, input, lastStep: last);
            }
            sample = sampler.Step(HiDreamSampler.Guided(withPrompt, withoutPrompt, sample, timestep, model.Guidance), sample);
            await stepped(step + 1, model.Steps);
        }
        return HiDreamSampler.Rgb(sample, rows, cols);
    }
}
