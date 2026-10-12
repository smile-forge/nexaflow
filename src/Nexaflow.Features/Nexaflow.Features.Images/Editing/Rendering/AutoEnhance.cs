using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>
/// One-click enhancement, as slider positions rather than a filter: it reads the picture's statistics and sets
/// exposure, contrast, highlights, shadows, white balance and saturation, so the result is one undo step and every
/// part of it can be adjusted after.
/// </summary>
/// <remarks>
/// Deliberately gentle — each correction is a fraction of what the statistics ask, and capped — because the same
/// numbers describe a dark photo and a photo of something dark.
/// </remarks>
public static class AutoEnhance
{
    /// <summary>The picture is reduced to this before it is read; statistics need no more.</summary>
    private const int SampleEdge = 512;

    /// <summary>Where a well-exposed picture's median luminance sits (sRGB-encoded).</summary>
    private const double TargetMedian = 0.46;

    /// <summary>A pixel this close to grey (max − min channel) can show a colour cast.</summary>
    private const double NeutralChroma = 0.15;

    /// <summary>The share of the picture that must be near-neutral before its cast is corrected.</summary>
    private const double MinNeutralShare = 0.02;

    public static RecipeAdjustments Analyse(SKImage picture)
    {
        var stats = Read(picture);
        if (stats.Count == 0) return RecipeAdjustments.Identity;

        double median = stats.Percentile(0.5), low = stats.Percentile(0.005), high = stats.Percentile(0.995);

        // Exposure: move the median towards the target, by half the distance in stops.
        double exposure = Math.Clamp(0.5 * Math.Log2(ToLinear(TargetMedian) / Math.Max(ToLinear(median), 1e-4)), -1.5, 1.5);

        // Contrast: a narrow histogram is stretched towards a full one.
        double spread = high - low;
        double contrast = Math.Clamp((0.85 - spread) / 0.85 * 60, 0, 35);

        // Highlights and shadows: pull back clipped lights, open up crushed darks.
        double highlights = -Math.Clamp((stats.FractionAbove(0.96) - 0.01) * 800, 0, 50);
        double shadows    =  Math.Clamp((stats.FractionBelow(0.04) - 0.01) * 700, 0, 45);

        // White balance: what should be grey, is. Read only from the near-neutral mid-tones — a picture of a red car
        // says nothing about the light — and only when there are enough of them to mean something.
        double temperature = 0, tint = 0;
        if (stats.NeutralShare >= MinNeutralShare)
        {
            double r = stats.NeutralRed, g = stats.NeutralGreen, b = stats.NeutralBlue;
            temperature = Math.Clamp((b - r) / Math.Max(r + b, 1e-4) * 150, -30, 30);
            tint        = Math.Clamp((g - (r + b) / 2) / Math.Max(g, 1e-4) * 150, -25, 25);
        }

        // Saturation: a little life for a flat picture, nothing for a vivid one.
        double saturation = Math.Clamp((0.18 - stats.MeanChroma) * 120, 0, 15);

        return new RecipeAdjustments
        {
            Exposure    = Math.Round(exposure, 2),
            Contrast    = Math.Round(contrast),
            Highlights  = Math.Round(highlights),
            Shadows     = Math.Round(shadows),
            Temperature = Math.Round(temperature),
            Tint        = Math.Round(tint),
            Saturation  = Math.Round(saturation),
        };
    }

    private static Statistics Read(SKImage picture)
    {
        double scale = Math.Min(1, (double)SampleEdge / Math.Max(picture.Width, picture.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(picture.Width * scale)), Math.Max(1, (int)(picture.Height * scale)),
                                   SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        using var sample = new SKBitmap(info);
        using (var canvas = new SKCanvas(sample))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawImage(picture, SKRect.Create(info.Width, info.Height),
                             new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        var stats = new Statistics();
        var pixels = sample.GetPixelSpan();
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 128) continue;   // transparent areas say nothing about the picture
            stats.Add(pixels[i] / 255.0, pixels[i + 1] / 255.0, pixels[i + 2] / 255.0);
        }
        return stats;
    }

    private static double ToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private sealed class Statistics
    {
        private readonly int[] _histogram = new int[256];
        private double _red, _green, _blue, _chroma;
        private int _neutral;

        public int Count { get; private set; }

        /// <summary>The share of pixels that are near-neutral mid-tones — the ones a colour cast shows in.</summary>
        public double NeutralShare => Count == 0 ? 0 : (double)_neutral / Count;

        public double NeutralRed   => _neutral == 0 ? 0 : _red / _neutral;
        public double NeutralGreen => _neutral == 0 ? 0 : _green / _neutral;
        public double NeutralBlue  => _neutral == 0 ? 0 : _blue / _neutral;
        public double MeanChroma   => Count == 0 ? 0 : _chroma / Count;

        public void Add(double r, double g, double b)
        {
            double luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double chroma = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            _histogram[(int)Math.Round(luma * 255)]++;
            _chroma += chroma;
            Count++;

            if (luma is > 0.15 and < 0.85 && chroma < NeutralChroma)
            {
                _red   += ToLinear(r);
                _green += ToLinear(g);
                _blue  += ToLinear(b);
                _neutral++;
            }
        }

        public double Percentile(double p)
        {
            long target = (long)Math.Ceiling(p * Count), seen = 0;
            for (int i = 0; i < 256; i++)
            {
                seen += _histogram[i];
                if (seen >= Math.Max(1, target)) return i / 255.0;
            }
            return 1;
        }

        public double FractionAbove(double level) => Fraction(i => i / 255.0 > level);
        public double FractionBelow(double level) => Fraction(i => i / 255.0 < level);

        private double Fraction(Func<int, bool> inBand)
        {
            long n = 0;
            for (int i = 0; i < 256; i++) if (inBand(i)) n += _histogram[i];
            return Count == 0 ? 0 : (double)n / Count;
        }
    }
}
