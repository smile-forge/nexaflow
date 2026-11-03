using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// How a thing picked in a photo lies in its scene depth: its distance (<see cref="SceneDepthPlane.Distance"/>, 0 the photo's
/// nearest part, 1 a thousand times as far) as the middle of its pixels', and the span its middle four fifths cover; the
/// point of it at its middle distance; and how far either side of that point's nearness its pixels reach — what a lens
/// keeps sharp to keep all of it sharp.
/// </summary>
public sealed record SubjectDepth(double Distance, double Nearest, double Farthest, MarkupPoint Point, double NearnessReach);

/// <summary>
/// Focusing on a thing rather than a point: where a thing picked in a photo — by a click on it, or a phrase naming it — lies in
/// the photo's scene depth, so a lens blur focuses at its middle distance and keeps all of it sharp.
/// </summary>
public static class SceneDepthFocus
{
    /// <summary>The share of a thing's pixels at either end of its depth left out of its span: its edges, where the depth runs
    /// into what is behind it.</summary>
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
        var samples = new List<(float Nearness, double X, double Y)>();
        for (int y = 0; y < mask.Height; y += step)
            for (int x = 0; x < mask.Width; x += step)
            {
                if (mask.Pixels[y * mask.Width + x] < 128) continue;
                double px = (x + 0.5) * sx, py = (y + 0.5) * sy;
                samples.Add((plane.At(px, py, photo), px, py));
            }
        if (samples.Count == 0) return null;
        // Nearest first: distance falls as nearness rises, so the middle and the ends are the same pixels either way.
        samples.Sort((a, b) => b.Nearness.CompareTo(a.Nearness));
        var middle = samples[samples.Count / 2];
        float nearest = samples[(int)(EdgeShare * (samples.Count - 1))].Nearness, farthest = samples[(int)((1 - EdgeShare) * (samples.Count - 1))].Nearness;
        return new SubjectDepth(plane.Distance(middle.Nearness), plane.Distance(nearest), plane.Distance(farthest), new MarkupPoint(middle.X, middle.Y),
                                Math.Max(nearest - middle.Nearness, middle.Nearness - farthest));
    }

    /// <summary>A lens blur that focuses on <paramref name="subject"/> and keeps its span sharp, as <paramref name="blur"/> is otherwise.</summary>
    public static RecipeLensBlur FocusedOn(RecipeLensBlur blur, SubjectDepth subject)
        => blur with
        {
            FocusX = subject.Point.X,
            FocusY = subject.Point.Y,
            Range = Math.Clamp(subject.NearnessReach + 0.01, 0.01, RecipeLensBlur.WidestRange),
        };
}
