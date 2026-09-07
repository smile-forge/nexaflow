using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A rendering reduced to how much ink is at each pixel: 0 is paper, 1 is full ink. Colour, alpha and
/// which renderer drew it stop mattering here, which is the point - two engines never agree on a
/// pixel, and this is the form in which they can be asked whether they agree on a shape.
///
/// <para>
/// It sits above the languages rather than beside one of them because the question is the same for all of
/// them: a formula held against LaTeX's own rendering and a tune held against an engraver's are one
/// measurement asked twice. Which was not obvious until the second caller wanted it.
/// </para>
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
    /// The tightest rectangle holding every pixel with ink in it, or an empty one where there is none.
    /// <para>
    /// Separate from <see cref="CropToInk"/> because two callers want different things from the same
    /// answer: the scoring wants the pixels, and whoever is drawing the review page wants the rectangle,
    /// so it can crop the picture it is about to save rather than a copy of it.
    /// </para>
    /// </summary>
    public (int X, int Y, int Width, int Height) InkBounds(float threshold = 0.15f)
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

        return right < 0 ? (0, 0, 0, 0) : (left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// How far apart the staff lines are, in this picture's own pixels, or null where nothing on it looks
    /// like a staff.
    ///
    /// <para>
    /// <strong>Measured at the right-hand end of the systems, because that is the only clear stave on the
    /// page.</strong> Everywhere else a row of ink is as likely to be note heads, a beam, a lyric or a
    /// chord symbol, and a profile of the whole page dutifully measures those instead — the spacing it
    /// returns is then half the real one, because heads sit on lines <em>and</em> in spaces. Past the last
    /// note of a line there is nothing left but the five lines themselves.
    /// </para>
    /// <para>
    /// Five taps rather than a peak search, because five evenly spaced lines is what a staff <em>is</em> —
    /// plus two taps just outside it, subtracted. Those are what settle the octave question, and they had
    /// to be added: on their own the five taps will happily lock onto the pitch between one staff and the
    /// next, landing every tap on a line of a different staff and scoring beautifully. What tells the two
    /// apart is that a real staff has nothing above its top line or below its bottom one, and a stack of
    /// staves has another staff. The taps between the lines are subtracted for the same reason at half the
    /// spacing, where every other tap falls in a space.
    /// </para>
    /// <para>
    /// This exists because a rendering cannot be held against a picture whose scale is unknown. Where the
    /// reference was engraved at is not recorded anywhere — but it is <em>in</em> the picture, one
    /// measurement away, and one number per tune beats one number for a whole corpus that was drawn at as
    /// many sizes as it has tunes.
    /// </para>
    /// </summary>
    public double? StaffSpace(double from = 2.0, double to = 22.0, double step = 0.05)
    {
        if (this.IsEmpty) return null;

        var (x, _, width, _) = this.InkBounds(0.05f);
        if (width < 40) return null;

        // The right end of the systems: past the last note, and short of the closing bar line.
        var right = x + width - 1;
        var lo = (int)(right - (0.12 * width));
        var hi = (int)(right - (0.01 * width));
        if (hi - lo < 6) return null;

        var profile = new double[this.Height];
        for (var y = 0; y < this.Height; y++)
        {
            var total = 0.0;
            for (var at = lo; at <= hi; at++) total += this[at, y];
            profile[y] = total / (hi - lo + 1);
        }

        var most = profile.Max();
        if (most <= 0) return null;
        for (var y = 0; y < profile.Length; y++) profile[y] /= most;

        double? best = null;
        var strongest = double.MinValue;

        for (var space = from; space <= to; space += step)
        {
            var top = double.MinValue;
            for (var y = 0.0; y + (4 * space) < this.Height; y += 0.25)
            {
                var on = 0.0;
                for (var line = 0; line < 5; line++) on += Sample(profile, y + (line * space));

                var between = 0.0;
                for (var gap = 0; gap < 4; gap++) between += Sample(profile, y + ((gap + 0.5) * space));

                var outside = Sample(profile, y - space) + Sample(profile, y + (5 * space));

                var got = on - (0.8 * between) - (1.5 * outside);
                if (got > top) top = got;
            }

            if (top <= strongest) continue;
            strongest = top;
            best = space;
        }

        return best;

        static double Sample(double[] of, double y)
        {
            if (y < 0 || y > of.Length - 1) return 0;
            var below = (int)y;
            var above = Math.Min(below + 1, of.Length - 1);
            var part = y - below;
            return (of[below] * (1 - part)) + (of[above] * part);
        }
    }

    /// <summary>
    /// Trims the paper away. Two renderers pad their output differently and there is nothing to learn
    /// from that, so every comparison starts from the ink and nothing else.
    /// </summary>
    public GrayImage CropToInk(float threshold = 0.15f)
    {
        var (left, top, width, height) = this.InkBounds(threshold);
        if (width == 0) return new GrayImage(0, 0, []);

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
