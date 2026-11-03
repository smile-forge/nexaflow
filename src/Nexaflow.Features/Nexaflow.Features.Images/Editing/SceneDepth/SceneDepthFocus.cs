using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// How a part picked in a photo lies in its scene depth (<see cref="SceneDepthPlane.Distance"/>, 0 the photo's nearest part, 1 a
/// thousand times as far): its distance as the middle of its pixels', and the span its middle four fifths cover — what a lens
/// keeps sharp to keep all of it sharp.
/// </summary>
public sealed record SubjectDepth(double Distance, double Nearest, double Farthest);

/// <summary>
/// Focusing on a part of a photo rather than a point: where a part — a thing picture selection found, a rectangle or an outline
/// drawn, the selection — lies in the photo's scene depth, so a lens blur keeps the whole of it sharp.
/// </summary>
public static class SceneDepthFocus
{
    /// <summary>The share of a part's pixels at either end of its depth left out of its span: its edges, where the depth runs
    /// into what is behind it.</summary>
    private const double EdgeShare = 0.1;

    /// <summary>The most of a part's pixels looked at: enough to know its distances, few enough to be instant.</summary>
    private const int MostSampled = 40_000;

    /// <summary>Where <paramref name="mask"/> — a thing picked on a <paramref name="photo"/>-sized photo, at any size — lies in
    /// <paramref name="plane"/>; null where it covers nothing.</summary>
    public static SubjectDepth? Of(PictureMask mask, SceneDepthPlane plane, SKSizeI photo)
        => Of(new SKRectI(0, 0, mask.Width, mask.Height), (x, y) => mask.Pixels[y * mask.Width + x] >= 128,
              SKMatrix.CreateScale((float)photo.Width / mask.Width, (float)photo.Height / mask.Height), plane, photo);

    /// <summary>Where <paramref name="coverage"/> — drawn or selected over any picture <paramref name="coverageToPhoto"/> carries
    /// onto the <paramref name="photo"/>-sized photo — lies in <paramref name="plane"/>, counting only what is on the photo; null
    /// where it covers none of it.</summary>
    public static SubjectDepth? Of(TiledCoverage coverage, SKMatrix coverageToPhoto, SceneDepthPlane plane, SKSizeI photo)
        => coverage.IsEmpty ? null : Of(coverage.Bounds, (x, y) => coverage.ValueAt(x, y) >= 0.5f, coverageToPhoto, plane, photo);

    private static SubjectDepth? Of(SKRectI bounds, Func<int, int, bool> covered, SKMatrix toPhoto, SceneDepthPlane plane, SKSizeI photo)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        int step = Math.Max(1, (int)Math.Sqrt((double)bounds.Width * bounds.Height / MostSampled));
        var samples = new List<float>();
        for (int y = bounds.Top; y < bounds.Bottom; y += step)
            for (int x = bounds.Left; x < bounds.Right; x += step)
            {
                if (!covered(x, y)) continue;
                var at = toPhoto.MapPoint(x + 0.5f, y + 0.5f);
                if (at.X < 0 || at.Y < 0 || at.X >= photo.Width || at.Y >= photo.Height) continue;
                samples.Add(plane.At(at.X, at.Y, photo));
            }
        if (samples.Count == 0) return null;
        // Nearest first: distance falls as nearness rises, so the middle and the ends are the same pixels either way.
        samples.Sort((a, b) => b.CompareTo(a));
        float middle = samples[samples.Count / 2];
        float nearest = samples[(int)(EdgeShare * (samples.Count - 1))], farthest = samples[(int)((1 - EdgeShare) * (samples.Count - 1))];
        return new SubjectDepth(plane.Distance(middle), plane.Distance(nearest), plane.Distance(farthest));
    }

    /// <summary>A lens blur that keeps all of <paramref name="subject"/> sharp, as <paramref name="blur"/> is otherwise.</summary>
    public static RecipeLensBlur FocusedOn(RecipeLensBlur blur, SubjectDepth subject) => blur.Banded(subject.Nearest, subject.Farthest);
}
