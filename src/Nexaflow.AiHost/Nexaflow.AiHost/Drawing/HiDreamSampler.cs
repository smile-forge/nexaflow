namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// Everything HiDream-O1's sampling does around its denoiser, in plain arithmetic. HiDream draws in pixels, not latents:
/// the picture is cut into 32×32 patches of three channels, each one token, and the model predicts the clean picture
/// from a noisy one. A step asks it twice — with the prompt and with an empty one — and steers away from the second by
/// the guidance scale; the steps run through HiDream's multistep UniPC sampler (<see cref="HiDreamUniPc"/>). The
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
    {
        var random = new Random(unchecked((int)(seed ^ (seed >> 32))));
        var noise = new float[tokens * TokenWidth];
        for (int i = 0; i < noise.Length; i += 2)
        {
            // Box–Muller: two uniforms in (0, 1] give two independent normals.
            double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
            double r = NoiseScale * Math.Sqrt(-2.0 * Math.Log(u1));
            noise[i] = (float)(r * Math.Cos(2 * Math.PI * u2));
            if (i + 1 < noise.Length) noise[i + 1] = (float)(r * Math.Sin(2 * Math.PI * u2));
        }
        return noise;
    }

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

/// <summary>
/// HiDream-O1's sampler: UniPC for flow matching (predicting the clean picture, B(h) = eʰ − 1, second order, dropping
/// to first on the last step), each step's prediction corrected once the next model output is in. Its noise levels are
/// shifted twice, as HiDream's own scheduler does — once when it is made and again when its steps are set — and the
/// model is shown each level as a whole timestep out of 1000.
/// </summary>
internal sealed class HiDreamUniPc
{
    private const int Order = 2;
    private const int TrainSteps = 1000;

    private readonly float[] _sigmas;

    /// <summary>The last <see cref="Order"/> predictions of the clean picture, newest last.</summary>
    private readonly float[]?[] _outputs = new float[]?[Order];

    private float[]? _lastSample;
    private int _lowerOrders;
    private int _thisOrder;
    private int _step;

    public HiDreamUniPc(int steps, double shift)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        // As made: 1000 levels 1 − linspace(1, 1/1000)[::-1], shifted; the first and last bound the steps.
        double Shifted(double s) => shift * s / (1 + (shift - 1) * s);
        float sigmaMax = (float)Shifted(1.0 - 1.0 / TrainSteps);
        float sigmaMin = 0f;

        // As set: linspace(max, min, steps + 1) without its last, shifted again, then 0.
        _sigmas = new float[steps + 1];
        Timesteps = new int[steps];
        for (int i = 0; i < steps; i++)
        {
            double s = sigmaMax + (sigmaMin - (double)sigmaMax) * i / steps;
            double shifted = Shifted(s);
            _sigmas[i] = (float)shifted;
            Timesteps[i] = (int)(shifted * TrainSteps);
        }
        _sigmas[steps] = 0f;
    }

    /// <summary>Each step's timestep, as the model is shown it.</summary>
    public int[] Timesteps { get; }

    /// <summary>The noise level before each step and after the last.</summary>
    public IReadOnlyList<float> Sigmas => _sigmas;

    /// <summary>One step: <paramref name="modelOutput"/> is the guided, negated velocity at <paramref name="sample"/>.</summary>
    public float[] Step(ReadOnlySpan<float> modelOutput, float[] sample)
    {
        if (_step >= Timesteps.Length) throw new InvalidOperationException("The sampler has taken every step.");

        // The prediction of the clean picture: sample − σ · output.
        float sigma = _sigmas[_step];
        var converted = new float[sample.Length];
        for (int i = 0; i < converted.Length; i++) converted[i] = sample[i] - sigma * modelOutput[i];

        if (_step > 0 && _lastSample is not null) sample = Corrected(converted, _lastSample, _thisOrder);

        for (int i = 0; i < Order - 1; i++) _outputs[i] = _outputs[i + 1];
        _outputs[Order - 1] = converted;

        // Second order, first on the last step and until two outputs are in.
        _thisOrder = Math.Min(Math.Min(Order, Timesteps.Length - _step), _lowerOrders + 1);
        _lastSample = sample;
        var next = Predicted(sample, _thisOrder);
        if (_lowerOrders < Order) _lowerOrders++;
        _step++;
        return next;
    }

    /// <summary>log α − log σ at a noise level, α = 1 − σ: +∞ at σ = 0.</summary>
    private static double Lambda(double sigma) => Math.Log(1 - sigma) - Math.Log(sigma);

    /// <summary>UniP: from <paramref name="x"/> at this step's level to the next's.</summary>
    private float[] Predicted(float[] x, int order)
    {
        var m0 = _outputs[Order - 1]!;
        double sigmaT = _sigmas[_step + 1], sigmaS0 = _sigmas[_step];
        double alphaT = 1 - sigmaT;
        double h = Lambda(sigmaT) - Lambda(sigmaS0);
        double hh = -h, hPhi1 = Math.Exp(hh) - 1, bH = hPhi1;

        float[]? d1 = null;
        if (order == 2)
        {
            var m1 = _outputs[Order - 2]!;
            double rk = (Lambda(_sigmas[_step - 1]) - Lambda(sigmaS0)) / h;
            d1 = new float[x.Length];
            for (int i = 0; i < d1.Length; i++) d1[i] = (float)((m1[i] - m0[i]) / rk);
        }

        // x_t = σt/σs0 · x − αt (eʰʰ − 1) m0 − αt B(h) · ½ D1, the last term only at second order.
        float a = (float)(sigmaT / sigmaS0), b = (float)(alphaT * hPhi1), c = (float)(alphaT * bH * 0.5);
        var next = new float[x.Length];
        for (int i = 0; i < next.Length; i++)
            next[i] = a * x[i] - b * m0[i] - (d1 is null ? 0f : c * d1[i]);
        return next;
    }

    /// <summary>UniC: the sample this step began from, corrected now that the model's output there is known.</summary>
    private float[] Corrected(float[] modelT, float[] last, int order)
    {
        var m0 = _outputs[Order - 1]!;
        double sigmaT = _sigmas[_step], sigmaS0 = _sigmas[_step - 1];
        double alphaT = 1 - sigmaT;
        double h = Lambda(sigmaT) - Lambda(sigmaS0);
        double hh = -h, hPhi1 = Math.Exp(hh) - 1, bH = hPhi1;

        double rk = 0;
        float[]? d1 = null;
        if (order == 2)
        {
            var m1 = _outputs[Order - 2]!;
            rk = (Lambda(_sigmas[_step - 2]) - Lambda(sigmaS0)) / h;
            d1 = new float[last.Length];
            for (int i = 0; i < d1.Length; i++) d1[i] = (float)((m1[i] - m0[i]) / rk);
        }

        // The weights: ½ at first order; at second, R ρ = b with R = [[1, 1], [rk, 1]].
        double rhoD1, rhoT;
        if (order == 1)
        {
            rhoD1 = 0;
            rhoT = 0.5;
        }
        else
        {
            double hPhiK = hPhi1 / hh - 1, b1 = hPhiK / bH;
            hPhiK = hPhiK / hh - 1.0 / 2;
            double b2 = hPhiK * 2 / bH;
            // [[1, 1], [rk, 1]] · (ρ1, ρt) = (b1, b2)
            double det = 1 - rk;
            rhoD1 = (b1 - b2) / det;
            rhoT = (b2 - rk * b1) / det;
        }

        float a = (float)(sigmaT / sigmaS0), b = (float)(alphaT * hPhi1), c = (float)(alphaT * bH);
        var corrected = new float[last.Length];
        for (int i = 0; i < corrected.Length; i++)
        {
            double residual = (d1 is null ? 0 : rhoD1 * d1[i]) + rhoT * (modelT[i] - m0[i]);
            corrected[i] = (float)(a * last[i] - b * m0[i] - c * residual);
        }
        return corrected;
    }
}
