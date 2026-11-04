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

    // ── editing ──────────────────────────────────────────────────────────────────────────────────────────────────────
    // An edit reads each picture it works from twice, as HiDream's pipeline does: small, by Qwen3-VL's vision tower, into the
    // prompt's image placeholders; and whole, as clean patches after the picture drawn, which every step reads beside it.

    /// <summary>The most pictures an edit works from: as many as HiDream reads at their largest, each in a square half the
    /// picture's longest side.</summary>
    public const int MaxSources = 4;

    /// <summary>The side of the square an edit is drawn within — at the first picture's proportions, as HiDream keeps a
    /// single reference's.</summary>
    public const int EditSide = 2048;

    /// <summary>The side of the square each picture is read within by the vision tower, for up to four.</summary>
    public const int ConditionSide = 384;

    /// <summary>Qwen3-VL's vision tokens: a picture's placeholder, and the tokens either side of a picture's run of them.</summary>
    public const long ImageToken = 151655, VisionStart = 151652, VisionEnd = 151653;

    /// <summary>The vision tower's patch, the groups of patches it merges into one token each way, and the side of the grid
    /// its position embedding is learned on.</summary>
    public const int ReaderPatch = 16, ReaderMerge = 2, ReaderGrid = 48;

    /// <summary>Values in one of the vision tower's patches: three channels, two identical frames, 16 × 16 pixels.</summary>
    public const int ReaderPatchWidth = Channels * 2 * ReaderPatch * ReaderPatch;

    /// <summary>Whether an edit is drawn at this size, and a picture to work from read at it: whole patches within
    /// <see cref="EditSide"/>².</summary>
    public static bool Reads(int width, int height) =>
        width >= Patch && height >= Patch && width % Patch == 0 && height % Patch == 0 && (long)width * height <= (long)EditSide * EditSide;

    /// <summary>The side of the square each of <paramref name="sources"/> pictures is read within as clean patches, for a
    /// <paramref name="width"/> × <paramref name="height"/> edit: the picture's longest side for one, three quarters of it
    /// for two, half for up to four.</summary>
    public static int ReferenceSide(int sources, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sources, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sources, MaxSources);
        int longest = Math.Max(width, height);
        return sources switch { 1 => longest, 2 => longest * 48 / 64, _ => longest / 2 };
    }

    /// <summary>The size a picture of these proportions is read at by the vision tower: within
    /// <see cref="ConditionSide"/>², each side cut to 32, as HiDream's <c>calculate_dimensions</c> sizes it.</summary>
    public static (int Width, int Height) ConditionSize(int width, int height)
    {
        double ratio = (double)width / height;
        double w = Math.Sqrt((double)ConditionSide * ConditionSide * ratio), h = w / ratio;
        return ((int)(w / 32) * 32, (int)(h / 32) * 32);
    }

    /// <summary>A picture's pixels, interleaved 8-bit RGB, as tokens in [−1, 1] laid out as <see cref="Rgb"/> reads them.</summary>
    public static float[] TokensOf(ReadOnlySpan<byte> rgb, int width, int height)
    {
        int rows = height / Patch, cols = width / Patch;
        var tokens = new float[rows * cols * TokenWidth];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int token = (r * cols + c) * TokenWidth;
                for (int ch = 0; ch < Channels; ch++)
                    for (int y = 0; y < Patch; y++)
                        for (int x = 0; x < Patch; x++)
                            tokens[token + (ch * Patch + y) * Patch + x] = (rgb[((r * Patch + y) * width + c * Patch + x) * 3 + ch] / 255f - 0.5f) / 0.5f;
            }
        return tokens;
    }

    /// <summary>A mask over the picture, a byte a pixel, as one value for each of the tokens' values, scaled to [0, 1].</summary>
    public static float[] PixelMask(ReadOnlySpan<byte> mask, int width, int height)
    {
        int rows = height / Patch, cols = width / Patch;
        var values = new float[rows * cols * TokenWidth];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                for (int ch = 0; ch < Channels; ch++)
                    for (int y = 0; y < Patch; y++)
                        for (int x = 0; x < Patch; x++)
                            values[(r * cols + c) * TokenWidth + (ch * Patch + y) * Patch + x] = mask[(r * Patch + y) * width + c * Patch + x] / 255f;
        return values;
    }

    /// <summary>
    /// The image tokens' rotary positions, [3, tokens], for an edit: the picture drawn's at (4096, 4096 + row, 4096 +
    /// column), then each picture worked from after it, numbered on from one past the largest position before it — as
    /// HiDream's <c>get_rope_index_fix_point</c> numbers them.
    /// </summary>
    public static long[] ImagePositions(int rows, int cols, IReadOnlyList<(int Rows, int Cols)> sources)
    {
        int count = rows * cols + sources.Sum(s => s.Rows * s.Cols);
        var positions = new long[3 * count];
        int at = Grid(positions, 0, count, ImagePositionBase, rows, cols);
        long next = ImagePositionBase + Math.Max(rows, cols);
        foreach (var (r, c) in sources)
        {
            at = Grid(positions, at, count, next, r, c);
            next += Math.Max(r, c);
        }
        return positions;
    }

    /// <summary>A grid's positions from <paramref name="at"/> on: (from, from + row, from + column), row by row.</summary>
    private static int Grid(long[] positions, int at, int count, long from, int rows, int cols)
    {
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++, at++)
            {
                positions[at] = from;
                positions[count + at] = from + r;
                positions[2 * count + at] = from + c;
            }
        return at;
    }

    /// <summary>
    /// A prompt's rotary positions, [3, tokens + 1], as HiDream numbers them (<see cref="VisionPromptPositions"/>), the
    /// timestep token that follows it on the diagonal after its text.
    /// </summary>
    /// <param name="ids">The prompt's tokens, each picture's placeholders a run of <see cref="ImageToken"/>.</param>
    /// <param name="grids">Each picture's merged grid, in the prompt's order.</param>
    public static long[] PromptPositions(ReadOnlySpan<long> ids, IReadOnlyList<(int Rows, int Cols)> grids) =>
        VisionPromptPositions.Of(ids, ImageToken, grids, trailing: 1);

    /// <summary>
    /// A picture as the vision tower reads it, [patches, 1536]: its 16 × 16 patches as Qwen3-VL's processor lays them out —
    /// in 2 × 2 groups row by row, each patch its channel, two identical frames, its row and its column — each value
    /// normalised to [−1, 1]. The picture's sides are whole groups.
    /// </summary>
    public static float[] ReaderPatches(ReadOnlySpan<byte> rgb, int width, int height)
    {
        int rows = height / ReaderPatch, cols = width / ReaderPatch, m = ReaderMerge;
        if (height % (ReaderPatch * m) != 0 || width % (ReaderPatch * m) != 0 || rgb.Length != width * height * 3)
            throw new ArgumentException($"A {width}x{height} picture is not read in whole groups of patches.", nameof(rgb));
        var patches = new float[rows * cols * ReaderPatchWidth];
        int at = 0;
        for (int gr = 0; gr < rows / m; gr++)
            for (int gc = 0; gc < cols / m; gc++)
                for (int mr = 0; mr < m; mr++)
                    for (int mc = 0; mc < m; mc++, at += ReaderPatchWidth)
                    {
                        int top = (gr * m + mr) * ReaderPatch, left = (gc * m + mc) * ReaderPatch;
                        for (int ch = 0; ch < Channels; ch++)
                            for (int frame = 0; frame < 2; frame++)
                                for (int y = 0; y < ReaderPatch; y++)
                                    for (int x = 0; x < ReaderPatch; x++)
                                        patches[at + ((ch * 2 + frame) * ReaderPatch + y) * ReaderPatch + x] =
                                            (rgb[((top + y) * width + left + x) * 3 + ch] - 127.5f) / 127.5f;
                    }
        return patches;
    }

    /// <summary>
    /// What the vision tower reads beside a <paramref name="rows"/> × <paramref name="cols"/> picture's patches, in their
    /// order: the four cells of its 48 × 48 position embedding each patch is sampled from bilinearly, [4, patches], with
    /// their weights, and each patch's row and column for its rotary position, [patches, 2] — as Qwen3-VL's
    /// <c>fast_pos_embed_interpolate</c> and <c>rot_pos_emb</c> compute them, the sampling points spaced as torch's
    /// <c>linspace</c> spaces them in single precision.
    /// </summary>
    public static (long[] Index, float[] Weights, long[] Positions) ReaderSampling(int rows, int cols)
    {
        int count = rows * cols, m = ReaderMerge;
        float[] down = Linspace(ReaderGrid - 1, rows), across = Linspace(ReaderGrid - 1, cols);
        var index = new long[4 * count];
        var weights = new float[4 * count];
        var positions = new long[2 * count];
        int at = 0;
        for (int gr = 0; gr < rows / m; gr++)
            for (int gc = 0; gc < cols / m; gc++)
                for (int mr = 0; mr < m; mr++)
                    for (int mc = 0; mc < m; mc++, at++)
                    {
                        int r = gr * m + mr, c = gc * m + mc;
                        int r0 = (int)down[r], c0 = (int)across[c];
                        int r1 = Math.Min(r0 + 1, ReaderGrid - 1), c1 = Math.Min(c0 + 1, ReaderGrid - 1);
                        float dr = down[r] - r0, dc = across[c] - c0;
                        index[at] = r0 * ReaderGrid + c0;
                        index[count + at] = r0 * ReaderGrid + c1;
                        index[2 * count + at] = r1 * ReaderGrid + c0;
                        index[3 * count + at] = r1 * ReaderGrid + c1;
                        weights[at] = (1 - dr) * (1 - dc);
                        weights[count + at] = (1 - dr) * dc;
                        weights[2 * count + at] = dr * (1 - dc);
                        weights[3 * count + at] = dr * dc;
                        positions[2 * at] = r;
                        positions[2 * at + 1] = c;
                    }
        return (index, weights, positions);
    }

    /// <summary>torch's <c>linspace(0, end, steps)</c> in single precision: from the start up to halfway, from the end after.</summary>
    private static float[] Linspace(float end, int steps)
    {
        var values = new float[steps];
        if (steps == 1) return values;
        float step = end / (steps - 1);
        int halfway = steps / 2;
        for (int i = 0; i < steps; i++) values[i] = i < halfway ? step * i : end - step * (steps - i - 1);
        return values;
    }
}
