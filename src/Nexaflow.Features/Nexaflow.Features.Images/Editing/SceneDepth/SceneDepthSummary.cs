using System.Globalization;
using System.Text;
using Nexaflow.Features.Common;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>One of the three bands a photo's depth is told in: near, middle or far.</summary>
public enum DepthBand
{
    Near,
    Middle,
    Far,
}

/// <summary>A band of the photo's depth: how much of the photo lies in it, and the box round the most of it, in fractions of
/// the photo's width and height; null where none of it does.</summary>
public sealed record DepthBandSummary(DepthBand Band, double Share, PictureBox? Box);

/// <summary>A thing named and found in the photo: how surely, the box round it in fractions of the photo, and where it lies.</summary>
public sealed record ThingDepth(string Phrase, double Score, PictureBox Box, SubjectDepth Depth)
{
    public DepthBand Band => SceneDepthSummary.BandOf(Depth.Distance);
}

/// <summary>
/// A photo's scene depth told in words, for an assistant: never the depth itself, but what is near, in the middle and far —
/// how much of the photo each band holds and where — and, for the things it was asked about, which band each is in and how far
/// it lies, from 0 the photo's nearest to 1 its farthest. Boxes are in fractions of the photo, so they mean the same at any size.
/// </summary>
public static class SceneDepthSummary
{
    /// <summary>The distance below which a part is near, and at or above which it is far.</summary>
    public const double NearUnder = 1.0 / 3, FarFrom = 2.0 / 3;

    /// <summary>A band is boxed round the part of it that holds this share of its pixels, so a stray speck does not stretch its box.</summary>
    private const double BoxedShare = 0.9;

    public static DepthBand BandOf(double distance) => distance < NearUnder ? DepthBand.Near : distance < FarFrom ? DepthBand.Middle : DepthBand.Far;

    /// <summary>The three bands of <paramref name="plane"/>, nearest first.</summary>
    public static IReadOnlyList<DepthBandSummary> Bands(SceneDepthPlane plane)
    {
        int w = plane.Width, h = plane.Height;
        var nearness = plane.Nearness;
        var bands = new List<DepthBandSummary>();
        foreach (var band in Enum.GetValues<DepthBand>())
        {
            var columns = new int[w];
            var rows = new int[h];
            int count = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (BandOf(1 - nearness[y * w + x]) != band) continue;
                    columns[x]++;
                    rows[y]++;
                    count++;
                }
            if (count == 0)
            {
                bands.Add(new DepthBandSummary(band, 0, null));
                continue;
            }
            var (left, right) = Spread(columns, count);
            var (top, bottom) = Spread(rows, count);
            bands.Add(new DepthBandSummary(band, (double)count / (w * h), new PictureBox((double)left / w, (double)top / h, (right + 1.0) / w, (bottom + 1.0) / h)));
        }
        return bands;
    }

    /// <summary>The first and last positions between which <see cref="BoxedShare"/> of the counts fall, as much cut from each end.</summary>
    private static (int First, int Last) Spread(int[] counts, int total)
    {
        double cut = total * (1 - BoxedShare) / 2;
        int first = 0, last = counts.Length - 1;
        for (double seen = 0; first < last && (seen += counts[first]) <= cut; first++) { }
        for (double seen = 0; last > first && (seen += counts[last]) <= cut; last--) { }
        return (first, last);
    }

    /// <summary>The summary as an assistant reads it.</summary>
    public static string Describe(string picture, SKSizeI size, IReadOnlyList<DepthBandSummary> bands, IReadOnlyList<ThingDepth> things,
                                  IReadOnlyList<string> unfound)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Scene depth of {picture} ({size.Width}x{size.Height}), as distance from 0 (its nearest part) to 1 (its farthest). ");
        text.AppendLine("Boxes are [left, top, right, bottom] in fractions of the picture's width and height.");
        foreach (var band in bands)
        {
            string range = band.Band switch { DepthBand.Near => "0-0.33", DepthBand.Middle => "0.33-0.67", _ => "0.67-1" };
            text.Append(CultureInfo.InvariantCulture, $"- {band.Band.ToString().ToLowerInvariant()} ({range}): {band.Share:P0} of the picture");
            text.AppendLine(band.Box is { } box ? $", mostly in {Box(box)}" : "");
        }
        foreach (var thing in things)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"- \"{thing.Phrase}\" ({thing.Score:0.00} sure) at {Box(thing.Box)}: {thing.Band.ToString().ToLowerInvariant()}, distance {thing.Depth.Distance:0.00} ({thing.Depth.Nearest:0.00}-{thing.Depth.Farthest:0.00})"));
        foreach (var phrase in unfound) text.AppendLine($"- \"{phrase}\": not found in the picture");
        return text.ToString().TrimEnd();
    }

    private static string Box(PictureBox box) => string.Create(CultureInfo.InvariantCulture, $"[{box.Left:0.00}, {box.Top:0.00}, {box.Right:0.00}, {box.Bottom:0.00}]");
}
