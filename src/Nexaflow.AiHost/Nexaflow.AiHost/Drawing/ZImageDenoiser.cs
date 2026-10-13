using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// One denoising step: the picture's patch tokens and the padded prompt in, the velocity the sampler steps along out —
/// in the tokens' layout (<see cref="ZImageSampler"/>).
/// </summary>
internal sealed class ZImageDenoiser(ModelSpec spec, InferenceSession session) : IHostModel
{
    private static readonly string[] Inputs = ["image_tokens", "image_positions", "caption", "caption_padding", "caption_positions", "timestep"];

    public ModelSpec Spec => spec;

    /// <param name="timestep">1 − the noise level the step starts from.</param>
    /// <param name="lastStep">The picture's last step: the session gives its scratch memory back after it.</param>
    public float[] Velocity(float[] tokens, long[] imagePositions, Half[] prompt, bool[] promptPadding, long[] promptPositions,
                            float timestep, bool lastStep)
    {
        int count = tokens.Length / ZImageSampler.TokenWidth, rows = promptPadding.Length;
        var caption = MemoryMarshal.Cast<Half, Float16>(prompt).ToArray();
        using var image = OrtValue.CreateTensorValueFromMemory(tokens, [1, count, ZImageSampler.TokenWidth]);
        using var positions = OrtValue.CreateTensorValueFromMemory(imagePositions, [count, 3]);
        using var captionValue = OrtValue.CreateTensorValueFromMemory(caption, [1, rows, caption.Length / rows]);
        using var padding = OrtValue.CreateTensorValueFromMemory(promptPadding, [rows]);
        using var captionPositions = OrtValue.CreateTensorValueFromMemory(promptPositions, [rows, 3]);
        using var step = OrtValue.CreateTensorValueFromMemory(new[] { timestep }, [1]);
        using var run = lastStep ? ImageSessions.Releasing() : new RunOptions();
        using var outputs = session.Run(run, Inputs, [image, positions, captionValue, padding, captionPositions, step], session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

internal sealed class ZImageDenoiserRunner : IModelRunner
{
    public string Id => "z-image-denoiser";

    public IHostModel Load(ModelSpec spec, string executionProvider)
    {
        var denoiser = new ZImageDenoiser(spec, ImageSessions.Open(spec, executionProvider));
        try
        {
            // One step at the common size, so CUDA tunes its kernels at load rather than on somebody's first picture.
            int h = 128, w = 128;
            var (rows, padding, positions) = ZImageSampler.PromptRows(new float[32 * 2560], 32, 2560);
            denoiser.Velocity(ZImageSampler.Patchify(new float[ZImageSampler.Channels * h * w], h, w),
                              ZImageSampler.ImagePositions(h, w, rows.Length / 2560), rows, padding, positions, 0.5f, lastStep: true);
            return denoiser;
        }
        catch
        {
            denoiser.Dispose();
            throw;
        }
    }
}
