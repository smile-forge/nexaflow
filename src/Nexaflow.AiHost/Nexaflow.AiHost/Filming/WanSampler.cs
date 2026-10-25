namespace Nexaflow.AiHost.Filming;

/// <summary>The size of a clip's latents: latent frames, rows and columns, each of 48 channels.</summary>
internal readonly record struct LatentShape(int Frames, int Rows, int Cols)
{
    /// <summary>The values of one latent frame.</summary>
    public int FrameValues => WanSampler.Channels * Rows * Cols;

    /// <summary>All of them.</summary>
    public int Values => Frames * Rows * Cols * WanSampler.Channels;

    /// <summary>The latents of a clip of this size.</summary>
    public static LatentShape Of(int frames, int width, int height)
        => new((frames - 1) / WanSampler.FramesPerLatent + 1, height / WanSampler.PixelsPerLatent, width / WanSampler.PixelsPerLatent);
}

/// <summary>
/// Everything Wan2.2-TI2V's sampling does around its graphs, in plain arithmetic: the noise a clip starts from, each
/// latent frame's timestep, a picture to start from held as the first latent frame, guidance, and a distilled model's
/// steps — each predicting the clean clip, the next noising it again. Latents are [48, frames, rows, cols], channel
/// first, as the graphs take them (tools/ai-models/wan2.2-ti2v-5b).
/// </summary>
internal static class WanSampler
{
    /// <summary>Latent channels.</summary>
    public const int Channels = 48;

    /// <summary>Pixels per latent cell, each way.</summary>
    public const int PixelsPerLatent = 16;

    /// <summary>Frames per latent frame, after the first, which is one picture.</summary>
    public const int FramesPerLatent = 4;

    /// <summary>Whether a clip of this size can be made: whole latent cells cut into the denoiser's 2×2 patches, and one
    /// frame and then whole latent frames.</summary>
    public static bool Films(int width, int height, int frames)
        => width > 0 && height > 0 && width % (2 * PixelsPerLatent) == 0 && height % (2 * PixelsPerLatent) == 0
           && frames >= 1 && (frames - 1) % FramesPerLatent == 0;


    /// <summary>Every latent frame's timestep: <paramref name="timestep"/>, but 0 for the first where it is a picture given.</summary>
    public static float[] Timesteps(int frames, float timestep, bool startsFromPicture)
    {
        var steps = new float[frames];
        Array.Fill(steps, timestep);
        if (startsFromPicture) steps[0] = 0f;
        return steps;
    }

    /// <summary>Puts the picture's latent frame in as the clip's first: what the model is shown at every step, and what
    /// the clip ends with, so the clip starts from exactly the picture.</summary>
    public static void KeepFirstFrame(Span<float> latents, ReadOnlySpan<float> firstFrame, LatentShape shape)
    {
        int plane = shape.Rows * shape.Cols;
        for (int c = 0; c < Channels; c++)
            firstFrame.Slice(c * plane, plane).CopyTo(latents.Slice(c * shape.Frames * plane, plane));
    }

    /// <summary>Latent frame <paramref name="frame"/> of the clip, [48, 1, rows, cols], as the decoder takes it.</summary>
    public static float[] LatentFrame(ReadOnlySpan<float> latents, LatentShape shape, int frame)
    {
        int plane = shape.Rows * shape.Cols;
        var one = new float[Channels * plane];
        for (int c = 0; c < Channels; c++)
            latents.Slice((c * shape.Frames + frame) * plane, plane).CopyTo(one.AsSpan(c * plane));
        return one;
    }

    /// <summary>The flow with the prompt steered away from the flow with the negative prompt by <paramref name="guidance"/>.</summary>
    public static float[] Guided(ReadOnlySpan<float> withPrompt, ReadOnlySpan<float> withNegative, float guidance)
    {
        var guided = new float[withPrompt.Length];
        for (int i = 0; i < guided.Length; i++) guided[i] = withNegative[i] + guidance * (withPrompt[i] - withNegative[i]);
        return guided;
    }

    /// <summary>
    /// The noise level a distilled model's timestep stands for: of the 1000 levels <c>s = i/1000</c>, shifted by
    /// <paramref name="shift"/> — the shift it was trained with — the one whose timestep is nearest, as FastVideo reads its
    /// distilled steps.
    /// </summary>
    public static double DistilledSigma(int timestep, float shift)
    {
    // FastVideo's table is made and shifted in float32, then read in float64.
        double best = 1, distance = double.MaxValue;
        for (int i = 1; i <= 1000; i++)
        {
            float s = i / 1000f;
            float sigma = shift * s / (1f + (shift - 1f) * s);
            double d = Math.Abs((double)(sigma * 1000f) - timestep);
            if (d < distance) (best, distance) = (sigma, d);
        }
        return best;
    }

    /// <summary>
    /// One distilled step: the clean clip predicted from <paramref name="latents"/> at noise level
    /// <paramref name="sigma"/> — latents − σ · flow — then, unless this was the last step, noised afresh with
    /// <paramref name="noise"/> to the next step's level.
    /// </summary>
    public static float[] DistilledStep(ReadOnlySpan<float> latents, ReadOnlySpan<float> flow, double sigma, double? nextSigma, ReadOnlySpan<float> noise)
    {
        var next = new float[latents.Length];
        for (int i = 0; i < next.Length; i++)
        {
            double clean = latents[i] - sigma * flow[i];
            next[i] = nextSigma is { } n ? (float)((1 - n) * clean + n * noise[i]) : (float)clean;
        }
        return next;
    }
}
