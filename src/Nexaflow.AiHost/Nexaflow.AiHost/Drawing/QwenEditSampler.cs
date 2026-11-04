using Nexaflow.AiHost.Sampling;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Everything Qwen-Image-Edit's sampling does around its graphs, in plain arithmetic, as diffusers'
/// QwenImageEditPlusPipeline does it: the sizes each picture is read at, the vision tower's patches and windows, the
/// prompt's tokens and positions, the image tokens' positions, the noise levels, true guidance with its norm kept, and
/// a mask laid over the latents. The layouts are the ones the build recipe traced the graphs with
/// (tools/ai-models/qwen-image-edit).
/// </summary>
internal static class QwenEditSampler
{
    /// <summary>Latent channels per token: 16, in 2 × 2 patches.</summary>
    public const int Channels = 64;

    /// <summary>Pixels per token each way: the VAE's 8 times its 2 × 2 patch.</summary>
    public const int PixelsPerToken = 16;

    /// <summary>The areas a picture is read within: by the vision tower, and by the VAE — the picture drawn's too.</summary>
    public const int ConditionArea = 384 * 384, VaeArea = 1024 * 1024;

    /// <summary>The most an edit's picture may hold, its sides each rounded to 32 about <see cref="VaeArea"/>.</summary>
    public const long MaxPixels = VaeArea * 5L / 4;

    /// <summary>The most pictures an edit works from: as many as Qwen-Image-Edit-2511 is made to combine.</summary>
    public const int MaxSources = 3;

    /// <summary>The system prompt's tokens, which the denoiser does not read.</summary>
    public const int TemplateTokens = 64;

    /// <summary>Qwen2.5-VL's vision tokens: a picture's placeholder, and the tokens either side of a picture's run of them.</summary>
    public const long ImageToken = 151655, VisionStart = 151652, VisionEnd = 151653;

    /// <summary>The vision tower's patch, the groups it merges into one token each way, and the side of its windows in
    /// those tokens.</summary>
    public const int ReaderPatch = 14, ReaderMerge = 2, ReaderWindow = 112 / ReaderMerge / ReaderPatch;

    /// <summary>Values in one of the vision tower's patches: three channels, two identical frames, 14 × 14 pixels.</summary>
    public const int ReaderPatchWidth = 3 * 2 * ReaderPatch * ReaderPatch;

    /// <summary>The processor's bounds on a picture's pixels, and the multiple its sides are rounded to.</summary>
    private const int ProcessorMinPixels = 3136, ProcessorMaxPixels = 12845056, ProcessorFactor = ReaderPatch * ReaderMerge;

    /// <summary>The processor's normalisation: CLIP's mean and deviation per channel.</summary>
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f], Deviation = [0.26862954f, 0.26130258f, 0.27577711f];

    /// <summary>The flow's noise-level shift: from the smallest picture's to the largest's, in tokens, and the level the
    /// last step stretches to.</summary>
    private const double BaseShift = 0.5, MaxShift = 0.9, BaseTokens = 256, MaxTokens = 8192;
    private const double TerminalSigma = 0.02;

    /// <summary>Whether an edit is drawn at this size, and a picture to work from read at it: whole tokens, about a megapixel.</summary>
    public static bool Reads(int width, int height) =>
        width >= 2 * PixelsPerToken && height >= 2 * PixelsPerToken && width % PixelsPerToken == 0 && height % PixelsPerToken == 0
        && (long)width * height <= MaxPixels;

    /// <summary>The size a picture of these proportions is read at within <paramref name="area"/>, each side rounded to
    /// 32, as the pipeline's <c>calculate_dimensions</c> sizes it.</summary>
    public static (int Width, int Height) SizeWithin(int area, int width, int height)
    {
        double ratio = (double)width / height;
        double w = Math.Sqrt(area * ratio), h = w / ratio;
        return ((int)Math.Round(w / 32) * 32, (int)Math.Round(h / 32) * 32);
    }

    /// <summary>The size Qwen2-VL's processor resizes a picture to: each side rounded to 28, within its bounds, as its
    /// <c>smart_resize</c> does.</summary>
    public static (int Width, int Height) ProcessorSize(int width, int height)
    {
        int f = ProcessorFactor;
        int h = (int)Math.Round((double)height / f) * f, w = (int)Math.Round((double)width / f) * f;
        if ((long)h * w > ProcessorMaxPixels)
        {
            double beta = Math.Sqrt((double)height * width / ProcessorMaxPixels);
            h = Math.Max(f, (int)Math.Floor(height / beta / f) * f);
            w = Math.Max(f, (int)Math.Floor(width / beta / f) * f);
        }
        else if ((long)h * w < ProcessorMinPixels)
        {
            double beta = Math.Sqrt(ProcessorMinPixels / ((double)height * width));
            h = (int)Math.Ceiling(height * beta / f) * f;
            w = (int)Math.Ceiling(width * beta / f) * f;
        }
        return (w, h);
    }

    /// <summary>
    /// A picture as the vision tower reads it, [patches, 1176]: its 14 × 14 patches as Qwen2-VL's processor lays them out —
    /// in 2 × 2 groups row by row, each patch its channel, two identical frames, its row and its column — each value
    /// normalised by CLIP's mean and deviation. The picture is at its <see cref="ProcessorSize"/>.
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
                        for (int ch = 0; ch < 3; ch++)
                            for (int frame = 0; frame < 2; frame++)
                                for (int y = 0; y < ReaderPatch; y++)
                                    for (int x = 0; x < ReaderPatch; x++)
                                        patches[at + ((ch * 2 + frame) * ReaderPatch + y) * ReaderPatch + x] =
                                            (rgb[((top + y) * width + left + x) * 3 + ch] / 255f - Mean[ch]) / Deviation[ch];
                    }
        return patches;
    }

    /// <summary>
    /// What the vision tower reads beside a <paramref name="rows"/> × <paramref name="cols"/> picture's patches: each patch's
    /// row and column for its rotary position, [patches, 2], in the processor's order; the order its windows hold the
    /// groups of patches in, [groups]; and the window each patch falls in, in that order, [patches] — as Qwen2.5-VL's
    /// <c>get_vision_position_ids</c> and <c>get_vision_window_index</c> work them out.
    /// </summary>
    public static (long[] Positions, long[] WindowOrder, long[] Windows) ReaderLayout(int rows, int cols)
    {
        int m = ReaderMerge, unit = m * m, count = rows * cols;
        var positions = new long[2 * count];
        int at = 0;
        for (int gr = 0; gr < rows / m; gr++)
            for (int gc = 0; gc < cols / m; gc++)
                for (int mr = 0; mr < m; mr++)
                    for (int mc = 0; mc < m; mc++, at++)
                    {
                        positions[2 * at] = gr * m + mr;
                        positions[2 * at + 1] = gc * m + mc;
                    }

        // The merged grid cut into windows of ReaderWindow × ReaderWindow groups, row by row, each window's groups row by
        // row, a window at the edge holding only the groups there are.
        int groupRows = rows / m, groupCols = cols / m, w = ReaderWindow;
        // As get_vision_window_index pads: always past the last whole window, the windows wholly in the padding empty.
        int windowRows = groupRows / w + 1, windowCols = groupCols / w + 1;
        var order = new List<long>(groupRows * groupCols);
        var windows = new List<long>(count);
        int window = 0;
        for (int wr = 0; wr < windowRows; wr++)
            for (int wc = 0; wc < windowCols; wc++)
            {
                int held = 0;
                for (int r = wr * w; r < Math.Min((wr + 1) * w, groupRows); r++)
                    for (int c = wc * w; c < Math.Min((wc + 1) * w, groupCols); c++)
                    {
                        order.Add(r * groupCols + c);
                        held++;
                    }
                if (held == 0) continue;
                for (int i = 0; i < held * unit; i++) windows.Add(window);
                window++;
            }
        return (positions, [.. order], [.. windows]);
    }

    /// <summary>
    /// The prompt as the pipeline writes it: the system prompt, then the user's turn — each picture as "Picture n: " and
    /// its vision tokens, then the instruction — and the reply's start. One placeholder stands for each picture; the
    /// prompt encoder's caller widens it to the picture's tokens (<see cref="Widened"/>).
    /// </summary>
    public static string PromptText(string instruction, int pictures)
    {
        var text = new System.Text.StringBuilder(
            "<|im_start|>system\nDescribe the key features of the input image (color, shape, size, texture, objects, background), " +
            "then explain how the user's text instruction should alter or modify the image. Generate a new image that meets the " +
            "user's requirements while maintaining consistency with the original input where appropriate.<|im_end|>\n<|im_start|>user\n");
        for (int i = 0; i < pictures; i++) text.Append($"Picture {i + 1}: <|vision_start|><|image_pad|><|vision_end|>");
        return text.Append(instruction).Append("<|im_end|>\n<|im_start|>assistant\n").ToString();
    }

    /// <summary>The prompt's tokens with each picture's one placeholder widened to its vision tokens' count, as Qwen2-VL's
    /// processor widens it.</summary>
    public static long[] Widened(ReadOnlySpan<long> ids, IReadOnlyList<int> pictures)
    {
        var widened = new List<long>(ids.Length + pictures.Sum());
        int picture = 0;
        foreach (long id in ids)
        {
            if (id != ImageToken) { widened.Add(id); continue; }
            if (picture >= pictures.Count) throw new ArgumentException("The prompt holds more pictures than it is given.", nameof(pictures));
            widened.AddRange(Enumerable.Repeat(ImageToken, pictures[picture++]));
        }
        if (picture != pictures.Count) throw new ArgumentException("The prompt holds fewer pictures than it is given.", nameof(pictures));
        return [.. widened];
    }

    /// <summary>
    /// The image tokens' rotary positions, [tokens, 3]: each picture's — the one drawn first, then each worked from — as
    /// (its place, row, column), rows and columns centred on 0 as QwenEmbedRope's scale_rope centres them, row by row.
    /// </summary>
    public static long[] ImagePositions(IReadOnlyList<(int Rows, int Cols)> grids)
    {
        var positions = new long[3 * grids.Sum(g => g.Rows * g.Cols)];
        int at = 0;
        for (int i = 0; i < grids.Count; i++)
        {
            var (rows, cols) = grids[i];
            int top = rows - rows / 2, left = cols - cols / 2;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++, at += 3)
                {
                    positions[at] = i;
                    positions[at + 1] = r - top;
                    positions[at + 2] = c - left;
                }
        }
        return positions;
    }

    /// <summary>The prompt's tokens' positions in the denoiser, [tokens, 3]: on the diagonal from the largest half side of
    /// any picture.</summary>
    public static long[] DenoiserPromptPositions(int tokens, IReadOnlyList<(int Rows, int Cols)> grids)
    {
        long start = grids.Max(g => Math.Max(g.Rows / 2, g.Cols / 2));
        var positions = new long[3 * tokens];
        for (int i = 0; i < tokens; i++) positions[3 * i] = positions[3 * i + 1] = positions[3 * i + 2] = start + i;
        return positions;
    }

    /// <summary>
    /// The noise level before each step and after the last, for a picture of <paramref name="imageTokens"/> tokens:
    /// equal steps from 1 down to 1/steps, shifted by the size's mu as the scheduler's exponential time shift does, stretched
    /// so the last reaches 0.02, then 0 — in single precision, as numpy computes them.
    /// </summary>
    public static float[] Sigmas(int steps, int imageTokens)
    {
        double m = (MaxShift - BaseShift) / (MaxTokens - BaseTokens);
        double mu = imageTokens * m + (BaseShift - m * BaseTokens);
        float e = (float)Math.Exp(mu);
        var shifted = new float[steps];
        for (int i = 0; i < steps; i++)
        {
            float t = (float)(i == steps - 1 ? 1.0 / steps : 1.0 + i * ((1.0 / steps - 1.0) / (steps - 1)));   // numpy's linspace
            shifted[i] = e / (e + (1f / t - 1f));
        }
        float scale = (1f - shifted[^1]) / (float)(1.0 - TerminalSigma);
        var sigmas = new float[steps + 1];
        for (int i = 0; i < steps; i++) sigmas[i] = 1f - (1f - shifted[i]) / scale;
        return sigmas;
    }

    /// <summary>The noise the latents start from, in the tokens' layout — standard normal, the same for the same seed.</summary>
    public static float[] Noise(long seed, int tokens) => new GaussianNoise(seed).Draw(tokens * Channels, 1f);

    /// <summary>
    /// True guidance as the pipeline applies it: the prediction without the prompt pushed toward the one with it by
    /// <paramref name="scale"/>, then each token's brought back to the length the prediction with the prompt has.
    /// </summary>
    public static float[] Guided(ReadOnlySpan<float> withPrompt, ReadOnlySpan<float> withoutPrompt, float scale)
    {
        var guided = new float[withPrompt.Length];
        for (int t = 0; t < guided.Length; t += Channels)
        {
            double withNorm = 0, guidedNorm = 0;
            for (int c = t; c < t + Channels; c++)
            {
                float g = withoutPrompt[c] + scale * (withPrompt[c] - withoutPrompt[c]);
                guided[c] = g;
                withNorm += withPrompt[c] * withPrompt[c];
                guidedNorm += g * g;
            }
            float ratio = (float)(Math.Sqrt(withNorm) / Math.Sqrt(guidedNorm));
            for (int c = t; c < t + Channels; c++) guided[c] *= ratio;
        }
        return guided;
    }

    /// <summary>An Euler step: the latents moved along <paramref name="velocity"/> by the change in noise level.</summary>
    public static void Step(Span<float> latents, ReadOnlySpan<float> velocity, float change)
    {
        for (int i = 0; i < latents.Length; i++) latents[i] += change * velocity[i];
    }

    /// <summary>
    /// A mask over the picture, a byte a pixel, as the inpaint pipeline lays it over the latents: halved into drawn again
    /// or kept, sampled at each latent pixel's top-left — torch's nearest — and spread over each token's 16 channels in
    /// the denoiser's 2 × 2 packing; one value for each of the tokens' values.
    /// </summary>
    public static float[] TokenMask(ReadOnlySpan<byte> mask, int width, int height)
    {
        int rows = height / PixelsPerToken, cols = width / PixelsPerToken, latent = PixelsPerToken / 2;
        var values = new float[rows * cols * Channels];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        float m = mask[(2 * r + dy) * latent * width + (2 * c + dx) * latent] >= 128 ? 1f : 0f;
                        for (int ch = 0; ch < 16; ch++) values[(r * cols + c) * Channels + ch * 4 + dy * 2 + dx] = m;
                    }
        return values;
    }
}
