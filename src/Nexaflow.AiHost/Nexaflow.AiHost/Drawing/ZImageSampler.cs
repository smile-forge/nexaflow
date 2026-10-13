namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Everything Z-Image's sampling does around its three graphs, in plain arithmetic: the noise levels the steps run
/// through, the noise a picture starts from, the latents cut into the patch tokens the denoiser reads and put back,
/// and the prompt padded and numbered as the model expects. The layouts are the ones the build recipe traced the
/// denoiser with (tools/ai-models/z-image).
/// </summary>
internal static class ZImageSampler
{
    /// <summary>Latent channels.</summary>
    public const int Channels = 16;

    /// <summary>Pixels per latent cell, each way.</summary>
    public const int PixelsPerLatent = 8;

    /// <summary>Latent cells per patch token, each way.</summary>
    public const int Patch = 2;

    /// <summary>Values in one patch token: 2 x 2 cells of 16 channels.</summary>
    public const int TokenWidth = Patch * Patch * Channels;

    /// <summary>The prompt's rows and the picture's tokens each come in multiples of this.</summary>
    public const int SequenceMultiple = 32;

    /// <summary>
    /// The noise level before each step and after the last: <c>linspace(1, 1/steps, steps)</c>, each level s moved by the
    /// shift to <c>shift·s / (1 + (shift − 1)·s)</c>, then 0.
    /// </summary>
    public static float[] Sigmas(int steps, float shift)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        var sigmas = new float[steps + 1];
        for (int i = 0; i < steps; i++)
        {
            double s = steps == 1 ? 1.0 : 1.0 - i * (1.0 - 1.0 / steps) / (steps - 1);
            sigmas[i] = (float)(shift * s / (1 + (shift - 1) * s));
        }
        return sigmas;
    }

    /// <summary>Whether a picture of this size can be drawn: whole patches, and a token count the model takes.</summary>
    public static bool Draws(int width, int height) =>
        width > 0 && height > 0 && width % (PixelsPerLatent * Patch) == 0 && height % (PixelsPerLatent * Patch) == 0
        && Tokens(width, height) % SequenceMultiple == 0;

    /// <summary>How many patch tokens a picture of this size is.</summary>
    public static int Tokens(int width, int height) => width / (PixelsPerLatent * Patch) * (height / (PixelsPerLatent * Patch));

    /// <summary>The noise a picture starts from — standard normal, [16, h, w] — the same for the same seed.</summary>
    public static float[] Noise(long seed, int latentHeight, int latentWidth)
    {
        var random = new Random(unchecked((int)(seed ^ (seed >> 32))));
        var noise = new float[Channels * latentHeight * latentWidth];
        for (int i = 0; i < noise.Length; i += 2)
        {
            // Box–Muller: two uniforms in (0, 1] give two independent normals.
            double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
            double r = Math.Sqrt(-2.0 * Math.Log(u1));
            noise[i] = (float)(r * Math.Cos(2 * Math.PI * u2));
            if (i + 1 < noise.Length) noise[i + 1] = (float)(r * Math.Sin(2 * Math.PI * u2));
        }
        return noise;
    }

    /// <summary>[16, h, w] latents as [(h/2)(w/2), 64] tokens: patch by patch, row by row; within a patch, its row, its
    /// column, then the channel.</summary>
    public static float[] Patchify(ReadOnlySpan<float> latents, int height, int width)
    {
        var tokens = new float[latents.Length];
        int cols = width / Patch;
        for (int c = 0; c < Channels; c++)
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int token = (y / Patch) * cols + x / Patch;
                    int within = ((y % Patch) * Patch + x % Patch) * Channels + c;
                    tokens[token * TokenWidth + within] = latents[(c * height + y) * width + x];
                }
        return tokens;
    }

    /// <summary>Adds <paramref name="scale"/> × tokens, laid out as <see cref="Patchify"/> lays them, onto
    /// <paramref name="latents"/>: one Euler step when the tokens are the velocity and the scale the step in noise level.</summary>
    public static void Step(Span<float> latents, ReadOnlySpan<float> tokens, int height, int width, float scale)
    {
        int cols = width / Patch;
        for (int c = 0; c < Channels; c++)
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int token = (y / Patch) * cols + x / Patch;
                    int within = ((y % Patch) * Patch + x % Patch) * Channels + c;
                    latents[(c * height + y) * width + x] += scale * tokens[token * TokenWidth + within];
                }
    }

    /// <summary>The image tokens' positions: (prompt rows + 1, patch row, patch column), as [tokens, 3].</summary>
    public static long[] ImagePositions(int latentHeight, int latentWidth, int promptRows)
    {
        int rows = latentHeight / Patch, cols = latentWidth / Patch;
        var positions = new long[rows * cols * 3];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int at = (r * cols + c) * 3;
                positions[at] = promptRows + 1;
                positions[at + 1] = r;
                positions[at + 2] = c;
            }
        return positions;
    }

    /// <summary>
    /// The prompt as the denoiser takes it: its rows repeated from the last up to a multiple of 32, which rows are
    /// repeats, and every row's position (1 + i, 0, 0) — repeats included, as Z-Image numbers them.
    /// </summary>
    /// <param name="caption">The prompt encoder's output, [rows, width] row-major.</param>
    public static (Half[] Rows, bool[] Padding, long[] Positions) PromptRows(ReadOnlySpan<float> caption, int rows, int width)
    {
        int padded = rows + (SequenceMultiple - rows % SequenceMultiple) % SequenceMultiple;
        var values = new Half[padded * width];
        for (int r = 0; r < padded; r++)
        {
            var source = caption.Slice(Math.Min(r, rows - 1) * width, width);
            for (int i = 0; i < width; i++) values[r * width + i] = (Half)source[i];
        }
        var padding = new bool[padded];
        var positions = new long[padded * 3];
        for (int r = 0; r < padded; r++)
        {
            padding[r] = r >= rows;
            positions[r * 3] = r + 1;
        }
        return (values, padding, positions);
    }

    /// <summary>Decoder pixels, [3, h, w] in [0, 1], as interleaved 8-bit RGB.</summary>
    public static byte[] Rgb(ReadOnlySpan<float> pixels, int height, int width)
    {
        int plane = height * width;
        var rgb = new byte[plane * 3];
        for (int i = 0; i < plane; i++)
            for (int c = 0; c < 3; c++)
                rgb[i * 3 + c] = (byte)Math.Clamp((int)MathF.Round(pixels[c * plane + i] * 255f), 0, 255);
        return rgb;
    }
}
