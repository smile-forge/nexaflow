namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// Haze laid over a picture by its scene depth, as air lays it over distance: each part keeps
/// <c>e^(−thickness × distance)</c> of its own light and takes the rest from the haze's colour, so the near stays clear and
/// the far fade into it. Distance is the picture's own (<see cref="SceneDepthPlane.Distance"/>), on which each third is ten
/// times as far as the last, run from the haze's start, nearer than which is clear, to the farthest part.
/// </summary>
public static class DepthHaze
{
    /// <summary>How thick the haze is at density 1: the farthest part keeps e^−4, under 2 %, of its own light.</summary>
    public const float Thickest = 4;

    /// <summary>The share of the farthest pixels a haze's own colour is taken from, where none is given.</summary>
    public const float FarthestShare = 0.03f;

    /// <summary>
    /// <paramref name="rgba"/> — premultiplied linear light, four values a pixel — hazed as <paramref name="density"/> (0–1) and
    /// <paramref name="start"/> (0–1, nearest to farthest) say, in <paramref name="haze"/>'s linear light. A pixel's coverage is
    /// kept: haze lies over what is there, never over what is not.
    /// </summary>
    /// <param name="distances">Each pixel's distance (<see cref="SceneDepthPlane.Distance"/>), 0 nearest.</param>
    public static void Apply(float[] rgba, float[] distances, int width, int height, float density, float start, (float R, float G, float B) haze)
    {
        if (rgba.Length != width * height * 4 || distances.Length != width * height) throw new ArgumentException("The picture and its depth are not the size given.");
        float thickness = Math.Clamp(density, 0, 1) * Thickest;
        float from = Math.Clamp(start, 0, 0.99f);
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++)
            {
                float distance = Math.Max(0, distances[i] - from) / (1 - from);
                float kept = MathF.Exp(-thickness * distance);
                float a = rgba[i * 4 + 3], into = (1 - kept) * a;
                rgba[i * 4] = rgba[i * 4] * kept + haze.R * into;
                rgba[i * 4 + 1] = rgba[i * 4 + 1] * kept + haze.G * into;
                rgba[i * 4 + 2] = rgba[i * 4 + 2] * kept + haze.B * into;
            }
        });
    }

    /// <summary>
    /// The colour of the picture's distance — the average linear light of its farthest <see cref="FarthestShare"/> — what a haze
    /// with no colour of its own takes: the sky's, or the distance's own. Transparent pixels are passed over.
    /// </summary>
    /// <param name="rgba">Premultiplied linear light, four values a pixel.</param>
    public static (float R, float G, float B) FarthestColour(ReadOnlySpan<float> rgba, ReadOnlySpan<float> nearness)
    {
        // Nearness in 1024 steps, counted, to find where the farthest share ends without sorting the picture.
        Span<int> counts = stackalloc int[1024];
        int seen = 0;
        for (int i = 0; i < nearness.Length; i++)
        {
            if (rgba[i * 4 + 3] <= 0.5f) continue;
            counts[Math.Clamp((int)(nearness[i] * 1023), 0, 1023)]++;
            seen++;
        }
        if (seen == 0) return (0.5f, 0.5f, 0.5f);
        int wanted = Math.Max(1, (int)(seen * FarthestShare)), cut = 0;
        for (int total = 0; cut < 1024 && (total += counts[cut]) < wanted; cut++) { }
        double r = 0, g = 0, b = 0, weight = 0;
        for (int i = 0; i < nearness.Length; i++)
        {
            float a = rgba[i * 4 + 3];
            if (a <= 0.5f || Math.Clamp((int)(nearness[i] * 1023), 0, 1023) > cut) continue;
            r += rgba[i * 4];
            g += rgba[i * 4 + 1];
            b += rgba[i * 4 + 2];
            weight += a;
        }
        return weight <= 0 ? (0.5f, 0.5f, 0.5f) : ((float)(r / weight), (float)(g / weight), (float)(b / weight));
    }
}
