using Nexaflow.AiHost.Sampling;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Everything HiDream-O1's sampling does around its denoiser, in plain arithmetic. HiDream draws in pixels, not latents:
/// the picture is cut into 32×32 patches of three channels, each one token, and the model predicts the clean picture
/// from a noisy one. A step asks it twice — with the prompt and with an empty one — and steers away from the second by
/// the guidance scale; the steps run through UniPC at HiDream's noise levels (<see cref="FlowUniPc.ShiftedTwice"/>). The
/// layouts are the ones the build recipe traced the denoiser with (tools/ai-models/hidream-o1).
/// </summary>
internal static class HiDreamSampler
{
    /// <summary>Pixels per patch token, each way.</summary>
    public const int Patch = 32;

    /// <summary>Colour channels.</summary>
    public const int Channels = 3;

    /// <summary>Values in one patch token: 32 × 32 pixels of three channels, channel first.</summary>
    public const int TokenWidth = Patch * Patch * Channels;

    /// <summary>How far the noise a picture starts from spreads: HiDream starts at 8 standard deviations, not 1.</summary>
    public const float NoiseScale = 8f;

    /// <summary>The smallest noise level a prediction is divided by.</summary>
    private const float SmallestSigma = 0.001f;

    /// <summary>Where the picture's positions start on each of the three rotary axes, past any prompt.</summary>
    public const long ImagePositionBase = 4096;

    /// <summary>The sizes HiDream-O1 draws at; it was trained on these alone.</summary>
    public static readonly IReadOnlyList<(int Width, int Height)> Sizes =
    [
        (2048, 2048), (2304, 1728), (1728, 2304), (2560, 1440), (1440, 2560), (2496, 1664), (1664, 2496),
        (3104, 1312), (1312, 3104), (2304, 1792), (1792, 2304),
    ];

    /// <summary>Whether a picture of this size can be drawn.</summary>
    public static bool Draws(int width, int height) => Sizes.Contains((width, height));

    /// <summary>How many patch tokens a picture of this size is.</summary>
    public static int Tokens(int width, int height) => width / Patch * (height / Patch);

    /// <summary>The noise a picture starts from, in the tokens' layout — 8 × standard normal, the same for the same seed.</summary>
    public static float[] Noise(long seed, int tokens)
        => new GaussianNoise(seed).Draw(tokens * TokenWidth, NoiseScale);

    /// <summary>
    /// The picture's rotary positions, [3, rows · cols]: each patch at (4096, 4096 + row, 4096 + column), row by row. The
    /// prompt's and the timestep token's the denoiser numbers itself.
    /// </summary>
    public static long[] ImagePositions(int rows, int cols)
    {
        int count = rows * cols;
        var positions = new long[3 * count];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int at = r * cols + c;
                positions[at] = ImagePositionBase;
                positions[count + at] = ImagePositionBase + r;
                positions[2 * count + at] = ImagePositionBase + c;
            }
        return positions;
    }

    /// <summary>
    /// What the sampler steps along: the two predictions of the clean picture turned into velocities at the step's
    /// timestep, the one with the prompt pushed away from the one without by <paramref name="guidance"/>, negated.
    /// </summary>
    public static float[] Guided(ReadOnlySpan<float> withPrompt, ReadOnlySpan<float> withoutPrompt, ReadOnlySpan<float> sample,
                                 int timestep, float guidance)
    {
        float sigma = Math.Max(timestep / 1000f, SmallestSigma);
        var output = new float[sample.Length];
        for (int i = 0; i < output.Length; i++)
        {
            float vCond = (withPrompt[i] - sample[i]) / sigma, vUncond = (withoutPrompt[i] - sample[i]) / sigma;
            output[i] = -(vUncond + guidance * (vCond - vUncond));
        }
        return output;
    }

    /// <summary>The value HiDream's timestep embedder reads for a step's timestep: 1 − t/1000.</summary>
    public static float TimestepInput(int timestep) => 1f - timestep / 1000f;

    /// <summary>The finished tokens, in [−1, 1], as interleaved 8-bit RGB, rows × cols patches.</summary>
    public static byte[] Rgb(ReadOnlySpan<float> tokens, int rows, int cols)
    {
        int width = cols * Patch, height = rows * Patch;
        var rgb = new byte[width * height * 3];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int token = (r * cols + c) * TokenWidth;
                for (int ch = 0; ch < Channels; ch++)
                    for (int y = 0; y < Patch; y++)
                        for (int x = 0; x < Patch; x++)
                        {
                            float value = (tokens[token + (ch * Patch + y) * Patch + x] + 1f) / 2f;
                            int pixel = ((r * Patch + y) * width + c * Patch + x) * 3 + ch;
                            rgb[pixel] = (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
                        }
            }
        return rgb;
    }
}
