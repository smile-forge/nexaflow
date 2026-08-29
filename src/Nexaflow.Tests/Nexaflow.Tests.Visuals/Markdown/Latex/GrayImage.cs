using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// A rendering reduced to how much ink is at each pixel: 0 is paper, 1 is full ink. Colour, alpha and
/// which renderer drew it stop mattering here, which is the point - two engines never agree on a
/// pixel, and this is the form in which they can be asked whether they agree on a shape.
/// </summary>
internal sealed class GrayImage
{
    public GrayImage(int width, int height, float[] ink)
    {
        this.Width = width;
        this.Height = height;
        this.Ink = ink;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major, <see cref="Width"/> × <see cref="Height"/>.</summary>
    public float[] Ink { get; }

    public bool IsEmpty => this.Width == 0 || this.Height == 0;

    public float this[int x, int y] => this.Ink[(y * this.Width) + x];

    public static GrayImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return FromBitmap(decoder.Frames[0]);
    }

    public static GrayImage FromBitmap(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        var ink = new float[width * height];
        for (var i = 0; i < ink.Length; i++)
        {
            var p = i * 4;
            // Over white paper, so a transparent pixel is blank rather than black.
            var alpha = pixels[p + 3] / 255f;
            var luminance = ((0.114f * pixels[p]) + (0.587f * pixels[p + 1]) + (0.299f * pixels[p + 2])) / 255f;
            ink[i] = 1f - ((alpha * luminance) + (1f - alpha));
        }

        return new GrayImage(width, height, ink);
    }

    /// <summary>
    /// Trims the paper away. Two renderers pad their output differently and there is nothing to learn
    /// from that, so every comparison starts from the ink and nothing else.
    /// </summary>
    public GrayImage CropToInk(float threshold = 0.15f)
    {
        int left = this.Width, right = -1, top = this.Height, bottom = -1;
        for (var y = 0; y < this.Height; y++)
        {
            for (var x = 0; x < this.Width; x++)
            {
                if (this[x, y] < threshold) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        if (right < 0) return new GrayImage(0, 0, []);

        var width = right - left + 1;
        var height = bottom - top + 1;
        var ink = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                ink[(y * width) + x] = this[left + x, top + y];
        return new GrayImage(width, height, ink);
    }

    /// <summary>
    /// Resamples to a given height, keeping the aspect ratio, by averaging over each source area. The
    /// averaging is the useful half: it blurs away the anti-aliasing and the sub-pixel placement that
    /// no two rasterisers agree on, and leaves the shape.
    /// </summary>
    public GrayImage ResampleToHeight(int height)
    {
        if (this.IsEmpty || height <= 0) return new GrayImage(0, 0, []);

        var width = Math.Max(1, (int)Math.Round((double)this.Width * height / this.Height));
        var ink = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            var y0 = (double)y * this.Height / height;
            var y1 = (double)(y + 1) * this.Height / height;
            for (var x = 0; x < width; x++)
            {
                var x0 = (double)x * this.Width / width;
                var x1 = (double)(x + 1) * this.Width / width;
                ink[(y * width) + x] = (float)this.AverageOver(x0, x1, y0, y1);
            }
        }

        return new GrayImage(width, height, ink);
    }

    private double AverageOver(double x0, double x1, double y0, double y1)
    {
        var total = 0.0;
        var weight = 0.0;
        for (var y = (int)Math.Floor(y0); y < Math.Min(this.Height, Math.Ceiling(y1)); y++)
        {
            var dy = Math.Min(y + 1, y1) - Math.Max(y, y0);
            if (dy <= 0) continue;
            for (var x = (int)Math.Floor(x0); x < Math.Min(this.Width, Math.Ceiling(x1)); x++)
            {
                var dx = Math.Min(x + 1, x1) - Math.Max(x, x0);
                if (dx <= 0) continue;
                total += this[x, y] * dx * dy;
                weight += dx * dy;
            }
        }

        return weight > 0 ? total / weight : 0.0;
    }

    /// <summary>Total ink, as a fraction of the area.</summary>
    public double InkFraction => this.IsEmpty ? 0 : this.Ink.Sum() / this.Ink.Length;

    /// <summary>Blurs by one pass of a separable 1-2-1 kernel: a pixel of slack in either direction.</summary>
    public GrayImage Blur()
    {
        if (this.IsEmpty) return this;

        var pass = new float[this.Ink.Length];
        for (var y = 0; y < this.Height; y++)
            for (var x = 0; x < this.Width; x++)
                pass[(y * this.Width) + x] =
                    ((x > 0 ? this[x - 1, y] : 0f) + (2 * this[x, y]) + (x < this.Width - 1 ? this[x + 1, y] : 0f)) / 4f;

        var ink = new float[this.Ink.Length];
        for (var y = 0; y < this.Height; y++)
            for (var x = 0; x < this.Width; x++)
                ink[(y * this.Width) + x] =
                    ((y > 0 ? pass[((y - 1) * this.Width) + x] : 0f)
                     + (2 * pass[(y * this.Width) + x])
                     + (y < this.Height - 1 ? pass[((y + 1) * this.Width) + x] : 0f)) / 4f;

        return new GrayImage(this.Width, this.Height, ink);
    }

    /// <summary>
    /// How much of the two renderings' ink lands in the same place, from 0 to 1. Both are brought to a
    /// common height and to the same total amount of ink, then the overlap is the ink they share.
    /// </summary>
    /// <remarks>
    /// Normalising the total is what makes this a question about placement rather than about weight:
    /// one rasteriser at 20 pixels tall lays down pale grey strokes where another at 50 lays down black
    /// ones, and no amount of that difference is a rendering bug. It is a ranking signal and not a
    /// verdict even so - a small spacing difference early in a long formula shifts everything after it,
    /// and the score falls for a rendering that is different rather than wrong.
    /// </remarks>
    /// <summary>
    /// How much of the two renderings' ink lands in the same place, from 0 to 1. Both are brought to a
    /// common height and to the same total amount of ink, blurred, and then slid over each other to see
    /// how well they can be made to agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Normalising the total is what makes this a question about placement rather than about weight:
    /// one rasteriser at 20 pixels tall lays down pale grey strokes where another at 50 lays down black
    /// ones, and no amount of that difference is a rendering bug. The blur and the slide are the same
    /// idea carried further — anti-aliasing, hinting and sub-pixel placement are differences between
    /// rasterisers rather than between renderings, and every one of them survives into the pixels.
    /// </para>
    /// <para>
    /// The height, the blur and the slide were chosen by measurement rather than taste. Each of a
    /// thousand corpus formulas was drawn correctly and then drawn <em>damaged</em> — one token
    /// dropped, a superscript turned into a subscript, two tokens transposed — and both were scored
    /// against the same reference. What settles the question is how often the correct drawing beats the
    /// damaged one; 24-tall with a single blur and no slide managed 66-79%, and this manages 76-83%
    /// while also putting a correct drawing near 0.78 instead of near 0.54.
    /// </para>
    /// <para>
    /// <strong>What that measurement also says, and it is the more important half:</strong> damaging a
    /// formula costs it only about 0.05 of score, while one correct formula differs from the next by
    /// 0.25. So no fixed threshold can separate a wrong drawing from a merely long one, and none should
    /// be asked to. The number is worth reading as a ranking, and worth trusting as a comparison
    /// between two runs over the <em>same</em> formula — where the variation between formulas, which is
    /// most of it, cancels out.
    /// </para>
    /// </remarks>
    public static double InkOverlap(GrayImage a, GrayImage b, int height = 16)
    {
        var left = Normalise(Soften(a.ResampleToHeight(height)));
        var right = Normalise(Soften(b.ResampleToHeight(height)));
        if (left is null || right is null) return left is null && right is null ? 1 : 0;

        var width = Math.Max(left.Width, right.Width);
        var best = 0.0;

        for (var dx = -Slide; dx <= Slide; dx++)
        {
            for (var dy = -Slide; dy <= Slide; dy++)
            {
                var shared = 0.0;
                for (var y = 0; y < height; y++)
                {
                    var line = y + dy;
                    if (line < 0 || line >= height) continue;

                    for (var x = 0; x < width; x++)
                    {
                        var across = x + dx;
                        var p = x < left.Width ? left[x, y] : 0f;
                        var q = across >= 0 && across < right.Width ? right[across, line] : 0f;
                        shared += Math.Min(p, q);
                    }
                }

                if (shared > best) best = shared;
            }
        }

        return best;
    }

    /// <summary>Scales the ink so it totals 1, or null where there is none.</summary>
    private static GrayImage? Normalise(GrayImage image)
    {
        if (image.IsEmpty) return null;
        var total = image.Ink.Sum();
        if (total <= 0) return null;

        var ink = new float[image.Ink.Length];
        for (var i = 0; i < ink.Length; i++)
            ink[i] = (float)(image.Ink[i] / total);
        return new GrayImage(image.Width, image.Height, ink);
    }


    /// <summary>How far either way the two are slid over each other looking for their best agreement.</summary>
    private const int Slide = 2;

    /// <summary>Three passes of the 1-2-1 blur: enough slack that a stroke landing a pixel out still counts.</summary>
    private static GrayImage Soften(GrayImage image) => image.Blur().Blur().Blur();
}
