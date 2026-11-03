using Nexaflow.AiHost.Sampling;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Everything FLUX.2 [klein]'s sampling does around its four graphs, in plain arithmetic, as diffusers'
/// Flux2KleinPipeline and Flux2KleinInpaintPipeline do it: the noise levels the steps run through — shifted by an amount
/// that follows the picture's size — the noise a picture starts from, the rotary positions of the latents and of each
/// picture worked from, an Euler step, and, under a mask, the first picture kept where the mask leaves it. Latents are
/// kept as the denoiser reads them, [tokens, 128] row by row: each token a 16×16-pixel patch. The layouts are the ones
/// the build recipe traced the graphs with (tools/ai-models/flux2-klein-4b).
/// </summary>
internal static class KleinSampler
{
    /// <summary>Values in one token: the VAE's 32 channels over a 2×2 patch of its cells.</summary>
    public const int Channels = 128;

    /// <summary>Pixels per token, each way.</summary>
    public const int PixelsPerToken = 16;

    /// <summary>The prompt's rows, padded or cut to this many, as the pipeline reads every prompt.</summary>
    public const int PromptTokens = 512;

    /// <summary>The first rotary coordinate of the i-th picture worked from is this × (i + 1).</summary>
    public const int SourceSpacing = 10;

    /// <summary>The most pixels a picture drawn or worked from may have: the pipeline works within a megapixel.</summary>
    public const int MaxPixels = 1024 * 1024;

    /// <summary>Whether a picture of this size is drawn or read: whole tokens, within a megapixel.</summary>
    public static bool Draws(int width, int height) =>
        width >= PixelsPerToken && height >= PixelsPerToken && width % PixelsPerToken == 0 && height % PixelsPerToken == 0
        && (long)width * height <= MaxPixels;

    /// <summary>
    /// The shift's exponent, μ, for a picture of <paramref name="imageTokens"/> tokens drawn in <paramref name="steps"/>
    /// steps: the pipeline's compute_empirical_mu, a line through what Black Forest Labs measured at 10 and 200 steps.
    /// </summary>
    public static double EmpiricalMu(int imageTokens, int steps)
    {
        const double a1 = 8.73809524e-05, b1 = 1.89833333, a2 = 0.00016927, b2 = 0.45666666;
        if (imageTokens > 4300) return a2 * imageTokens + b2;
        double m200 = a2 * imageTokens + b2, m10 = a1 * imageTokens + b1;
        double a = (m200 - m10) / 190.0;
        return a * steps + (m200 - 200.0 * a);
    }

    /// <summary>
    /// The noise level before each step and after the last: <c>linspace(1, 1/steps, steps)</c>, each level s moved by the
    /// exponential time shift to <c>e^μ / (e^μ + 1/s − 1)</c> at <see cref="EmpiricalMu"/>, then 0.
    /// </summary>
    public static float[] Sigmas(int steps, int imageTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        double shift = Math.Exp(EmpiricalMu(imageTokens, steps));
        var sigmas = new float[steps + 1];
        for (int i = 0; i < steps; i++)
        {
            double s = steps == 1 ? 1.0 : 1.0 - i * (1.0 - 1.0 / steps) / (steps - 1);
            sigmas[i] = (float)(shift / (shift + (1.0 / s - 1.0)));
        }
        return sigmas;
    }

    /// <summary>The noise a picture starts from — standard normal, [tokens, 128] — the same for the same seed.</summary>
    public static float[] Noise(long seed, int tokens) => new GaussianNoise(seed).Draw(tokens * Channels);

    /// <summary>
    /// The positions of the latents' tokens, then of each picture worked from: (0, row, column, 0) for the latents and
    /// (10 (i + 1), row, column, 0) for the i-th picture, as [tokens, 4].
    /// </summary>
    /// <param name="sources">Each picture worked from, in tokens: its rows and its columns.</param>
    public static long[] ImagePositions(int rows, int cols, IReadOnlyList<(int Rows, int Cols)> sources)
    {
        int count = rows * cols + sources.Sum(s => s.Rows * s.Cols);
        var positions = new long[count * 4];
        int at = Grid(positions, 0, 0, rows, cols);
        for (int i = 0; i < sources.Count; i++) at = Grid(positions, at, SourceSpacing * (i + 1), sources[i].Rows, sources[i].Cols);
        return positions;
    }

    private static int Grid(long[] positions, int at, int time, int rows, int cols)
    {
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++, at += 4)
            {
                positions[at]     = time;
                positions[at + 1] = r;
                positions[at + 2] = c;
            }
        return at;
    }

    /// <summary>The prompt's rows' positions: (0, 0, 0, i), as [512, 4].</summary>
    public static long[] PromptPositions()
    {
        var positions = new long[PromptTokens * 4];
        for (int i = 0; i < PromptTokens; i++) positions[i * 4 + 3] = i;
        return positions;
    }

    /// <summary>One Euler step: <paramref name="scale"/> × the velocity of the latents' own tokens — the first of the
    /// denoiser's output, which also gives one for each picture worked from — added onto them.</summary>
    public static void Step(Span<float> latents, ReadOnlySpan<float> velocity, float scale)
    {
        for (int i = 0; i < latents.Length; i++) latents[i] += scale * velocity[i];
    }

    /// <summary>
    /// The first picture kept where a mask leaves it: each token's latents blended toward <paramref name="kept"/> by
    /// 1 − its mask — <paramref name="kept"/> being that picture noised to the level the step reached, or the picture
    /// itself after the last step.
    /// </summary>
    /// <param name="mask">Per token, 1 where it is drawn again and 0 where the picture is kept.</param>
    public static void Keep(Span<float> latents, ReadOnlySpan<float> kept, ReadOnlySpan<float> mask)
    {
        for (int t = 0; t < mask.Length; t++)
        {
            float m = mask[t];
            var row = latents.Slice(t * Channels, Channels);
            var from = kept.Slice(t * Channels, Channels);
            for (int c = 0; c < Channels; c++) row[c] = (1f - m) * from[c] + m * row[c];
        }
    }

    /// <summary>A picture's latents noised to <paramref name="sigma"/> with the noise the picture started from, as the
    /// scheduler's scale_noise does: <c>σ·noise + (1 − σ)·latents</c>.</summary>
    public static float[] Noised(ReadOnlySpan<float> latents, ReadOnlySpan<float> noise, float sigma)
    {
        var noised = new float[latents.Length];
        for (int i = 0; i < noised.Length; i++) noised[i] = sigma * noise[i] + (1f - sigma) * latents[i];
        return noised;
    }

    /// <summary>
    /// A mask over the picture, a byte a pixel, as one value a token: resized to the tokens' grid bilinearly, from pixel
    /// centres and without antialiasing, as torch's interpolate does in the pipeline, and scaled to [0, 1].
    /// </summary>
    public static float[] TokenMask(ReadOnlySpan<byte> mask, int width, int height)
    {
        int rows = height / PixelsPerToken, cols = width / PixelsPerToken;
        var tokens = new float[rows * cols];
        double sy = (double)height / rows, sx = (double)width / cols;
        for (int r = 0; r < rows; r++)
        {
            double y = Math.Max((r + 0.5) * sy - 0.5, 0);
            int y0 = Math.Min((int)y, height - 1), y1 = Math.Min(y0 + 1, height - 1);
            double fy = y - y0;
            for (int c = 0; c < cols; c++)
            {
                double x = Math.Max((c + 0.5) * sx - 0.5, 0);
                int x0 = Math.Min((int)x, width - 1), x1 = Math.Min(x0 + 1, width - 1);
                double fx = x - x0;
                double top = mask[y0 * width + x0] * (1 - fx) + mask[y0 * width + x1] * fx;
                double bottom = mask[y1 * width + x0] * (1 - fx) + mask[y1 * width + x1] * fx;
                tokens[r * cols + c] = (float)((top * (1 - fy) + bottom * fy) / 255.0);
            }
        }
        return tokens;
    }

    /// <summary>[128, rows, cols] latents, as the picture encoder gives them, as [rows·cols, 128] tokens.</summary>
    public static float[] Tokens(ReadOnlySpan<float> planes, int rows, int cols)
    {
        int cells = rows * cols;
        var tokens = new float[planes.Length];
        for (int c = 0; c < Channels; c++)
            for (int t = 0; t < cells; t++)
                tokens[t * Channels + c] = planes[c * cells + t];
        return tokens;
    }

    /// <summary>[rows·cols, 128] tokens as [128, rows, cols] latents, as the decoder takes them.</summary>
    public static float[] Planes(ReadOnlySpan<float> tokens, int rows, int cols)
    {
        int cells = rows * cols;
        var planes = new float[tokens.Length];
        for (int t = 0; t < cells; t++)
            for (int c = 0; c < Channels; c++)
                planes[c * cells + t] = tokens[t * Channels + c];
        return planes;
    }
}
