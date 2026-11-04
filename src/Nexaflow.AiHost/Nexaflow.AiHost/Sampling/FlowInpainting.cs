namespace Nexaflow.AiHost.Sampling;

/// <summary>
/// Drawing again only what a mask covers, in a flow-matching sampler, as diffusers' inpaint pipelines do: after each step
/// the picture worked from is put back where the mask leaves it, noised to the level the step reached — or as it is after
/// the last step — so what is drawn joins what is kept.
/// </summary>
internal static class FlowInpainting
{
    /// <summary>A picture noised to <paramref name="sigma"/> with the noise the drawing started from, as the scheduler's
    /// scale_noise does: <c>σ·noise + (1 − σ)·picture</c>.</summary>
    public static float[] Noised(ReadOnlySpan<float> picture, ReadOnlySpan<float> noise, float sigma)
    {
        var noised = new float[picture.Length];
        for (int i = 0; i < noised.Length; i++) noised[i] = sigma * noise[i] + (1f - sigma) * picture[i];
        return noised;
    }

    /// <summary>
    /// <paramref name="sample"/> blended toward <paramref name="kept"/> by 1 − the mask, each mask value covering
    /// <paramref name="width"/> of the sample's values in turn — a latent token's channels, or one value of a pixel.
    /// </summary>
    /// <param name="mask">1 where the picture is drawn again and 0 where it is kept.</param>
    public static void Keep(Span<float> sample, ReadOnlySpan<float> kept, ReadOnlySpan<float> mask, int width)
    {
        if (mask.Length * width > sample.Length || kept.Length < mask.Length * width)
            throw new ArgumentException("The mask covers more than the sample or what is kept.", nameof(mask));
        for (int t = 0; t < mask.Length; t++)
        {
            float m = mask[t];
            var row = sample.Slice(t * width, width);
            var from = kept.Slice(t * width, width);
            for (int c = 0; c < width; c++) row[c] = (1f - m) * from[c] + m * row[c];
        }
    }
}
