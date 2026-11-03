using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>How a thing picked in a photo lies in its scene depth: its distance, from 0 the photo's nearest to 1 its farthest,
/// as the middle of its pixels' and the span its middle four fifths cover — and the point of it at its middle distance.</summary>
public sealed record SubjectDepth(double Distance, double Nearest, double Farthest, MarkupPoint Point);

/// <summary>
/// Focusing on a thing rather than a point: where a thing picked in a photo — by a click on it, or a phrase naming it — lies in
/// the photo's scene depth, so a lens blur focuses at its middle distance and keeps all of it sharp.
/// </summary>
public static class SceneDepthFocus
{
    /// <summary>The share of a thing's pixels at either end of its distances left out of its span: its edges, where the depth
    /// runs into what is behind it.</summary>
    private const double EdgeShare = 0.1;

    /// <summary>The most of a thing's pixels looked at: enough to know its distances, few enough to be instant.</summary>
    private const int MostSampled = 40_000;

    /// <summary>Where <paramref name="mask"/> — a thing picked on a <paramref name="photo"/>-sized photo, at any size — lies in
    /// <paramref name="plane"/>; null where it covers nothing.</summary>
    public static SubjectDepth? Of(PictureMask mask, SceneDepthPlane plane, SKSizeI photo)
    {
        int covered = 0;
        foreach (var b in mask.Pixels) if (b >= 128) covered++;
        if (covered == 0) return null;
        int step = Math.Max(1, (int)Math.Sqrt((double)covered / MostSampled));
        double sx = (double)photo.Width / mask.Width, sy = (double)photo.Height / mask.Height;
        var samples = new List<(float Distance, double X, double Y)>();
        for (int y = 0; y < mask.Height; y += step)
            for (int x = 0; x < mask.Width; x += step)
            {
                if (mask.Pixels[y * mask.Width + x] < 128) continue;
                double px = (x + 0.5) * sx, py = (y + 0.5) * sy;
                samples.Add((1 - plane.At(px, py, photo), px, py));
            }
        if (samples.Count == 0) return null;
        samples.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var middle = samples[samples.Count / 2];
        float nearest = samples[(int)(EdgeShare * (samples.Count - 1))].Distance, farthest = samples[(int)((1 - EdgeShare) * (samples.Count - 1))].Distance;
        return new SubjectDepth(middle.Distance, nearest, farthest, new MarkupPoint(middle.X, middle.Y));
    }

    /// <summary>A lens blur that focuses on <paramref name="subject"/> and keeps its span sharp, as <paramref name="blur"/> is otherwise.</summary>
    public static RecipeLensBlur FocusedOn(RecipeLensBlur blur, SubjectDepth subject)
        => blur with
        {
            FocusX = subject.Point.X,
            FocusY = subject.Point.Y,
            Range = Math.Clamp(Math.Max(subject.Distance - subject.Nearest, subject.Farthest - subject.Distance) + 0.01, 0.01, RecipeLensBlur.WidestRange),
        };
}
