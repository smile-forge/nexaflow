using Nexaflow.AiHost.Sampling;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Everything MOSS-Image's sampling does around its three graphs, in plain arithmetic: the noise levels its Euler steps
/// run through, the noise a picture starts from, the rotary rows and the timestep's sinusoid the denoiser is handed, and
/// the guided step. The layouts are the ones the build recipe traced the denoiser with (tools/ai-models/moss-image), and
/// each is computed as its check_moss_image.py computes it.
/// </summary>
internal static class MossSampler
{
    /// <summary>Latent channels.</summary>
    public const int Channels = 128;

    /// <summary>Pixels per latent cell, each way.</summary>
    public const int PixelsPerLatent = 16;

    /// <summary>The fewest and the most latent cells each way the denoiser was traced to take.</summary>
    public const int FewestCells = 16, MostCells = 128;

    /// <summary>Values in a rotary row: sines then cosines of four coordinates over <see cref="Bands"/> frequencies.</summary>
    public const int RopeWidth = 128;

    /// <summary>Values in the timestep's sinusoid.</summary>
    public const int TimeWidth = 256;

    /// <summary>Values in each of the prompt's rows: Qwen3.5-9B's hidden state.</summary>
    public const int PromptWidth = 4096;

    /// <summary>The noise levels the model was trained on: k / 1000.</summary>
    public const int TrainingSteps = 1000;

    private const int Bands = RopeWidth / 8;
    private const float RopeBase = 10000f;

    /// <summary>Whether a picture of this size can be drawn: whole latent cells, as many each way as the denoiser takes.</summary>
    public static bool Draws(int width, int height) =>
        width % PixelsPerLatent == 0 && height % PixelsPerLatent == 0
        && width / PixelsPerLatent is >= FewestCells and <= MostCells && height / PixelsPerLatent is >= FewestCells and <= MostCells;

    /// <summary>
    /// The noise level before each step and after the last, as MOSSImageScheduler sets them: <paramref name="steps"/>
    /// levels from 1 to shift / (1000 + shift − 1), each moved by the shift to shift·s / (1 + (shift − 1)·s) and snapped
    /// to the nearest level it was trained on, a tie to the lower; a level the one before already took is dropped; then 0.
    /// </summary>
    public static float[] Sigmas(int steps, float shift)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        // The grid as numpy's linspace lays it out — in float64, a step at a time, its last point the end itself — and
        // rounds once to float32; the shift then in float32, as the reference.
        double last = (float)(shift / (double)(TrainingSteps + shift - 1));
        double spacing = steps == 1 ? 0 : (last - 1.0) / (steps - 1);
        var levels = new List<float>(steps + 1);
        for (int i = 0; i < steps; i++)
        {
            float grid = (float)(i == steps - 1 && steps > 1 ? last : i * spacing + 1.0);
            float wanted = shift * grid / (1f + (shift - 1f) * grid);
            float snapped = Trained(Nearest(wanted));
            if (levels.Count == 0 || levels[^1] != snapped) levels.Add(snapped);
        }
        levels.Add(0f);
        return [.. levels];
    }

    /// <summary>The trained level nearest <paramref name="wanted"/>, as its k: of two as near, the lower.</summary>
    private static int Nearest(float wanted)
    {
        int upper = 1;
        while (upper < TrainingSteps && Trained(upper) < wanted) upper++;
        int lower = Math.Max(upper - 1, 1);
        return Math.Abs(wanted - Trained(lower)) <= Math.Abs(Trained(upper) - wanted) ? lower : upper;
    }

    /// <summary>The k-th trained level, k / 1000 rounded once to float32.</summary>
    private static float Trained(int k) => (float)(k / (double)TrainingSteps);

    /// <summary>The timestep the denoiser is shown at a noise level: the level times 1000, in float32.</summary>
    public static float Timestep(float sigma) => sigma * TrainingSteps;

    /// <summary>The noise a picture starts from — standard normal, [128, h, w] — the same for the same seed.</summary>
    public static float[] Noise(long seed, int latentHeight, int latentWidth)
        => new GaussianNoise(seed).Draw(Channels * latentHeight * latentWidth);

    /// <summary>
    /// The rotary rows of a picture h by w cells and a prompt of <paramref name="promptRows"/> rows, [h·w + rows, 128],
    /// image first: per token the angles of four coordinates — (0, rows, row − (h−1)/2, column − (w−1)/2) for a cell,
    /// (0, i, 0, 0) for the i-th prompt row — at 16 frequencies 10000^(−k/16), the sines of all four, then the cosines.
    /// </summary>
    public static float[] Rope(int latentHeight, int latentWidth, int promptRows)
    {
        Span<float> bands = stackalloc float[Bands];
        for (int k = 0; k < Bands; k++) bands[k] = 1f / MathF.Pow(RopeBase, k / (float)Bands);

        int cells = latentHeight * latentWidth;
        var rows = new float[(cells + promptRows) * RopeWidth];
        Span<float> coordinates = stackalloc float[4];
        float middleRow = (latentHeight - 1) / 2f, middleColumn = (latentWidth - 1) / 2f;
        for (int token = 0; token < cells + promptRows; token++)
        {
            if (token < cells)
            {
                coordinates[0] = 0f;
                coordinates[1] = promptRows;
                coordinates[2] = token / latentWidth - middleRow;
                coordinates[3] = token % latentWidth - middleColumn;
            }
            else
            {
                coordinates.Clear();
                coordinates[1] = token - cells;
            }
            var row = rows.AsSpan(token * RopeWidth, RopeWidth);
            for (int c = 0; c < 4; c++)
                for (int k = 0; k < Bands; k++)
                {
                    float phase = coordinates[c] * bands[k];
                    row[c * Bands + k] = MathF.Sin(phase);
                    row[RopeWidth / 2 + c * Bands + k] = MathF.Cos(phase);
                }
        }
        return rows;
    }

    /// <summary>The timestep's sinusoid, [256]: the cosines then the sines of t · 10000^(−k/128), computed in float64.</summary>
    public static float[] TimeRows(float timestep)
    {
        const int half = TimeWidth / 2;
        var rows = new float[TimeWidth];
        for (int k = 0; k < half; k++)
        {
            double phase = timestep * Math.Pow(10000.0, -k / (double)half);
            rows[k] = (float)Math.Cos(phase);
            rows[half + k] = (float)Math.Sin(phase);
        }
        return rows;
    }

    /// <summary>One guided Euler step: <paramref name="latents"/> moved by <paramref name="step"/> — the change in noise
    /// level — along the negative prompt's velocity steered <paramref name="guidance"/> times its distance toward the
    /// prompt's.</summary>
    public static void Step(Span<float> latents, ReadOnlySpan<float> withPrompt, ReadOnlySpan<float> withNegative, float guidance, float step)
    {
        for (int i = 0; i < latents.Length; i++)
            latents[i] += step * (withNegative[i] + guidance * (withPrompt[i] - withNegative[i]));
    }
}
