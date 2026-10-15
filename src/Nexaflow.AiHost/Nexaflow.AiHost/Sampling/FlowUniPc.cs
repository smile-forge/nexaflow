namespace Nexaflow.AiHost.Sampling;

/// <summary>
/// UniPC for flow matching, as diffusers' multistep scheduler runs it for the models that use it here: the model's output
/// is the flow (noise − clean), from which each step takes the clean prediction; B(h) = eʰ − 1, second order, dropping
/// to first on the last step; each step's prediction corrected once the next model output is in. The models differ only
/// in the noise levels they step through (<see cref="Shifted"/>, <see cref="ShiftedTwice"/>), and the model is shown
/// each level as a whole timestep out of 1000.
/// </summary>
internal sealed class FlowUniPc
{
    private const int Order = 2;
    private const int TrainSteps = 1000;

    private readonly float[] _sigmas;

    /// <summary>The last <see cref="Order"/> predictions of the clean sample, newest last.</summary>
    private readonly float[]?[] _outputs = new float[]?[Order];

    private float[]? _lastSample;
    private int _lowerOrders;
    private int _thisOrder;
    private int _step;

    /// <param name="levels">The noise level before each step, in double precision: each step's timestep is taken from
    /// it before it is rounded to the float the arithmetic runs in, as the schedulers do.</param>
    private FlowUniPc(double[] levels)
    {
        _sigmas = [.. levels.Select(s => (float)s), 0f];
        Timesteps = [.. levels.Select(s => (int)(s * TrainSteps))];
    }

    /// <summary>
    /// The noise levels diffusers' UniPC scheduler sets for flow sigmas, as Wan2.2 runs it: <c>linspace(1, 1/1000,
    /// steps + 1)</c> without its last, each level s moved by the shift to <c>shift·s / (1 + (shift − 1)·s)</c>, the first
    /// a hair under 1, then 0.
    /// </summary>
    public static FlowUniPc Shifted(int steps, double shift)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        var levels = new double[steps];
        // numpy's linspace: start + i · ((stop − start) / divisions).
        double spacing = (1.0 / TrainSteps - 1.0) / steps;
        for (int i = 0; i < steps; i++) levels[i] = Shift(1.0 + i * spacing, shift);
        // A first level of 1 is nudged under it, as diffusers does: α = 0 there, and the first step takes its log.
        if (Math.Abs(levels[0] - 1) < 1e-6) levels[0] -= 1e-6;
        return new FlowUniPc(levels);
    }

    /// <summary>
    /// HiDream-O1's noise levels, shifted twice as its own scheduler does — once when it is made and again when its steps
    /// are set: from the shifted 1 − 1/1000 down to 0 in equal steps, each shifted again, then 0.
    /// </summary>
    public static FlowUniPc ShiftedTwice(int steps, double shift)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        // As made: 1000 levels 1 − linspace(1, 1/1000)[::-1], shifted; the first and last bound the steps.
        double sigmaMax = (float)Shift(1.0 - 1.0 / TrainSteps, shift);
        const double sigmaMin = 0;

        // As set: linspace(max, min, steps + 1) without its last, shifted again, then 0.
        var levels = new double[steps];
        for (int i = 0; i < steps; i++) levels[i] = Shift(sigmaMax + (sigmaMin - sigmaMax) * i / steps, shift);
        return new FlowUniPc(levels);
    }

    private static double Shift(double s, double shift) => shift * s / (1 + (shift - 1) * s);

    /// <summary>Each step's timestep, as the model is shown it.</summary>
    public int[] Timesteps { get; }

    /// <summary>The noise level before each step and after the last.</summary>
    public IReadOnlyList<float> Sigmas => _sigmas;

    /// <summary>One step: <paramref name="flow"/> is the model's flow (noise − clean) at <paramref name="sample"/>, guided
    /// where the model is.</summary>
    public float[] Step(ReadOnlySpan<float> flow, float[] sample)
    {
        if (_step >= Timesteps.Length) throw new InvalidOperationException("The sampler has taken every step.");

        // The prediction of the clean sample: sample − σ · flow.
        float sigma = _sigmas[_step];
        var converted = new float[sample.Length];
        for (int i = 0; i < converted.Length; i++) converted[i] = sample[i] - sigma * flow[i];

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
