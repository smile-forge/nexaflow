using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Filming;

/// <summary>
/// One call of Wan2.2-TI2V's transformer — the full model's or a distillation of it, the same graph: a clip's latents at
/// each latent frame's timestep, and the prompt's rows, in; the flow (noise − clean) the model predicts out, laid out as
/// the latents (<see cref="WanSampler"/>).
/// </summary>
internal sealed class WanDenoiser(ModelSpec spec, InferenceSession session) : IHostModel
{
    private static readonly string[] Inputs = ["latents", "timesteps", "text"];

    public ModelSpec Spec => spec;

    /// <param name="latents">[48, frames, rows, cols] — rows and cols even.</param>
    /// <param name="timesteps">Each latent frame's timestep out of 1000 — 0 for a frame given, not made.</param>
    /// <param name="text">The prompt's rows, [512, 4096] (<see cref="WanPromptEncoder.Encode"/>).</param>
    /// <param name="lastCall">The clip's last call: the session gives its scratch memory back after it.</param>
    public float[] Flow(float[] latents, LatentShape shape, float[] timesteps, float[] text, bool lastCall)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(latents, [1, WanSampler.Channels, shape.Frames, shape.Rows, shape.Cols]);
        using var steps = OrtValue.CreateTensorValueFromMemory(timesteps, [1, shape.Frames]);
        using var rows = OrtValue.CreateTensorValueFromMemory(text, [1, WanPromptEncoder.Rows, WanPromptEncoder.Width]);
        using var run = lastCall ? GraphSessions.Releasing(Spec) : new RunOptions();
        using var outputs = session.Run(run, Inputs, [input, steps, rows], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class WanDenoiserRunner : IModelRunner
{
    public string Id => "wan-denoiser";

    public IHostModel Load(ModelSpec spec)
    {
        var denoiser = new WanDenoiser(spec, GraphSessions.Open(spec));
        try
        {
            // One call on a short clip, so CUDA tunes its kernels at load rather than on somebody's first clip.
            var shape = new LatentShape(2, 8, 8);
            denoiser.Flow(new float[shape.Values], shape, [0f, 500f], new float[WanPromptEncoder.Rows * WanPromptEncoder.Width], lastCall: true);
            return denoiser;
        }
        catch
        {
            denoiser.Dispose();
            throw;
        }
    }
}
