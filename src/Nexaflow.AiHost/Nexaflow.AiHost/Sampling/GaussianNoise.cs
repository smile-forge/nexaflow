namespace Nexaflow.AiHost.Sampling;

/// <summary>
/// The noise a picture or a clip is made from: normal values drawn in turn from one seed — the noise it starts from, then,
/// for a distilled model, fresh noise at each step — so the same seed and prompt make the same picture or clip.
/// </summary>
internal sealed class GaussianNoise(long seed)
{
    private readonly Random _random = new(unchecked((int)(seed ^ (seed >> 32))));

    /// <summary>The next <paramref name="count"/> values, normal with a standard deviation of <paramref name="scale"/>.</summary>
    public float[] Draw(int count, double scale = 1.0)
    {
        var noise = new float[count];
        for (int i = 0; i < noise.Length; i += 2)
        {
            // Box–Muller: two uniforms in (0, 1] give two independent normals.
            double u1 = 1.0 - _random.NextDouble(), u2 = _random.NextDouble();
            double r = scale * Math.Sqrt(-2.0 * Math.Log(u1));
            noise[i] = (float)(r * Math.Cos(2 * Math.PI * u2));
            if (i + 1 < noise.Length) noise[i + 1] = (float)(r * Math.Sin(2 * Math.PI * u2));
        }
        return noise;
    }
}
