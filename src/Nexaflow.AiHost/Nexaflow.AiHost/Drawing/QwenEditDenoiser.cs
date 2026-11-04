using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// One denoising step: the latents' tokens followed by each picture worked from's, their positions, how many are the
/// latents', the prompt's hidden states and their positions, and the step's noise level in; the velocity of every token
/// out, the latents' first (<see cref="QwenEditSampler"/>).
/// </summary>
internal sealed class QwenEditDenoiser(ModelSpec spec, InferenceSession session) : IHostModel
{
    private static readonly string[] Inputs = ["image_tokens", "image_positions", "drawn", "prompt", "prompt_positions", "timestep"];

    public ModelSpec Spec => spec;

    /// <param name="tokens">[tokens, 64]: the latents', then each picture's.</param>
    /// <param name="drawn">How many of the tokens are the latents'.</param>
    /// <param name="sigma">The noise level the step starts from.</param>
    /// <param name="lastStep">The picture's last step: the session gives its scratch memory back after it.</param>
    public float[] Velocity(float[] tokens, long[] imagePositions, int drawn, float[] prompt, long[] promptPositions, float sigma, bool lastStep)
    {
        int count = tokens.Length / QwenEditSampler.Channels, rows = prompt.Length / QwenEditPictureReader.TokenWidth;
        using var image = OrtValue.CreateTensorValueFromMemory(tokens, [1, count, QwenEditSampler.Channels]);
        using var positions = OrtValue.CreateTensorValueFromMemory(imagePositions, [count, 3]);
        using var drawnValue = OrtValue.CreateTensorValueFromMemory(new long[] { drawn }, [1]);
        using var text = OrtValue.CreateTensorValueFromMemory(prompt, [1, rows, QwenEditPictureReader.TokenWidth]);
        using var textPositions = OrtValue.CreateTensorValueFromMemory(promptPositions, [rows, 3]);
        using var step = OrtValue.CreateTensorValueFromMemory(new[] { sigma }, [1]);
        using var run = lastStep ? GraphSessions.Releasing(Spec) : new RunOptions();
        using var outputs = session.Run(run, Inputs, [image, positions, drawnValue, text, textPositions, step], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class QwenEditDenoiserRunner : IModelRunner
{
    public string Id => "qwen-edit-denoiser";

    public IHostModel Load(ModelSpec spec)
    {
        var denoiser = new QwenEditDenoiser(spec, GraphSessions.Open(spec));
        try
        {
            // One step at the square megapixel with a picture beside it, so CUDA tunes its kernels at load.
            (int, int)[] grids = [(64, 64), (64, 64)];
            const int prompt = 32;
            denoiser.Velocity(new float[2 * 64 * 64 * QwenEditSampler.Channels], QwenEditSampler.ImagePositions(grids), 64 * 64,
                              new float[prompt * QwenEditPictureReader.TokenWidth], QwenEditSampler.DenoiserPromptPositions(prompt, grids), 0.5f, lastStep: true);
            return denoiser;
        }
        catch
        {
            denoiser.Dispose();
            throw;
        }
    }
}
