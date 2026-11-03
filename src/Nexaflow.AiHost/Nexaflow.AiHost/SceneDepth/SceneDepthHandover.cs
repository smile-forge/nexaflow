using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.SceneDepth;

/// <summary>A picture as the estimator reads it: planar, normalised RGB at its working size.</summary>
internal sealed record SceneDepthInput(float[] Planes, int Width, int Height);

/// <summary>
/// A picture handed to Depth Anything V2 as its makers' <c>infer_image</c> prepares one: RGB over 255, brought by OpenCV's
/// cubic resize to the lower bound of 518 — the shorter side 518, each side the nearest multiple of the ViT's 14-pixel
/// patch, rounded up where that falls short — and normalised by ImageNet's mean and deviation, in double precision before
/// it is made single. A picture more than three times as long as it is wide would make a working size past
/// <see cref="LongestSide"/>: it is brought to that along its length instead, so the estimator peaks under 3 GB and a 4 GB
/// card runs it.
/// </summary>
internal static class SceneDepthHandover
{
    /// <summary>The makers' input size: what the shorter side is brought to.</summary>
    public const int Side = 518;

    /// <summary>The ViT's patch: both sides are multiples of it.</summary>
    public const int Patch = 14;

    /// <summary>The longest side a picture is read at: three times <see cref="Side"/>.</summary>
    public const int LongestSide = 3 * Side;

    private static readonly double[] Mean = [0.485, 0.456, 0.406];
    private static readonly double[] Deviation = [0.229, 0.224, 0.225];

    /// <summary>The size a <paramref name="width"/> × <paramref name="height"/> picture is read at.</summary>
    public static (int Width, int Height) WorkingSize(int width, int height)
    {
        double scale = Math.Max((double)Side / width, (double)Side / height);
        int w = Constrained(scale * width, Side), h = Constrained(scale * height, Side);
        if (Math.Max(w, h) <= LongestSide) return (w, h);
        double along = (double)LongestSide / Math.Max(width, height);
        return (Constrained(along * width, Patch), Constrained(along * height, Patch));
    }

    /// <summary>The makers' <c>constrain_to_multiple_of</c>: the nearest multiple of the patch, halves to even as NumPy rounds
    /// them, or the next one up where that is under <paramref name="least"/>.</summary>
    private static int Constrained(double size, int least)
    {
        int multiple = (int)(Math.Round(size / Patch, MidpointRounding.ToEven) * Patch);
        return multiple < least ? (int)(Math.Ceiling(size / Patch) * Patch) : multiple;
    }

    /// <summary>The picture in <paramref name="rgba"/> — interleaved 8-bit RGBA — as the estimator reads it; its alpha is not read,
    /// as the makers' reading of a file has none.</summary>
    public static SceneDepthInput Prepare(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var (w, h) = WorkingSize(width, height);
        var rgb = new double[width * height * 3];
        for (int i = 0, n = width * height; i < n; i++)
            for (int c = 0; c < 3; c++)
                rgb[i * 3 + c] = rgba[i * 4 + c] / 255.0;
        var resized = PictureResampling.ResizeCubicAsOpenCv(rgb, width, height, 3, w, h);
        int plane = w * h;
        var planes = new float[3 * plane];
        Parallel.For(0, 3, c =>
        {
            for (int i = 0; i < plane; i++) planes[c * plane + i] = (float)((resized[i * 3 + c] - Mean[c]) / Deviation[c]);
        });
        return new SceneDepthInput(planes, w, h);
    }
}
