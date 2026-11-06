using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// One denoising step: the latents' tokens followed by those of each picture worked from, their positions, the prompt's
/// rows and theirs, and the step's noise level in; the velocity of every token out, the latents' first
/// (<see cref="KleinSampler"/>).
/// </summary>
internal sealed class KleinDenoiser(ModelSpec spec, IGraph graph) : IHostModel
{
    private static readonly string[] Inputs = ["image_tokens", "image_positions", "prompt", "prompt_positions", "timestep"];

    public ModelSpec Spec => spec;

    /// <param name="tokens">[tokens, 128]: the latents', then each picture's.</param>
    /// <param name="sigma">The noise level the step starts from.</param>
    /// <param name="lastStep">The picture's last step: the session gives its scratch memory back after it.</param>
    public float[] Velocity(float[] tokens, long[] imagePositions, float[] prompt, long[] promptPositions, float sigma, bool lastStep)
    {
        int count = tokens.Length / KleinSampler.Channels, rows = promptPositions.Length / 4;
        using var image = OrtValue.CreateTensorValueFromMemory(tokens, [1, count, KleinSampler.Channels]);
        using var positions = OrtValue.CreateTensorValueFromMemory(imagePositions, [count, 4]);
        using var text = OrtValue.CreateTensorValueFromMemory(prompt, [1, rows, prompt.Length / rows]);
        using var textPositions = OrtValue.CreateTensorValueFromMemory(promptPositions, [rows, 4]);
        using var step = OrtValue.CreateTensorValueFromMemory(new[] { sigma }, [1]);
        using var run = lastStep ? GraphSessions.Releasing(Spec) : new RunOptions();
        using var outputs = graph.Run(run, Inputs, [image, positions, text, textPositions, step], graph.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => graph.Dispose();
}

internal sealed class KleinDenoiserRunner : IModelRunner
{
    public string Id => "klein-denoiser";

    public IHostModel Load(ModelSpec spec)
    {
        var denoiser = new KleinDenoiser(spec, GraphSessions.OpenGraph(spec));
        try
        {
            // One step at the common size, so CUDA tunes its kernels at load rather than on somebody's first picture.
            int rows = 64, cols = 64;
            denoiser.Velocity(new float[rows * cols * KleinSampler.Channels], KleinSampler.ImagePositions(rows, cols, []),
                              new float[KleinSampler.PromptTokens * KleinPromptEncoder.Width], KleinSampler.PromptPositions(), 0.5f, lastStep: true);
            return denoiser;
        }
        catch
        {
            denoiser.Dispose();
            throw;
        }
    }
}
