using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.SceneDepth;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Selection;

/// <summary>
/// What a region's spans of distance cover on the photo they were chosen on. A <see cref="RegionSceneDepth"/> is kept as its
/// span, so a region read back from a recipe holds spans not yet worked out: whatever turns it into a mask works them out
/// first, from the photo's scene depth — kept, or estimated where it is not — only where a span needs it.
/// </summary>
public static class RegionSceneDepths
{
    /// <summary>Whether <paramref name="region"/> holds a span not yet worked out.</summary>
    public static bool Unresolved(RecipeRegion? region) => region is not null && region.Steps.Any(s => s is RegionSceneDepth { Reach: null });

    /// <summary>
    /// Each pixel's coverage, 0–255, of a <paramref name="size"/> photo by the span from <paramref name="near"/> to
    /// <paramref name="far"/> — 0 the nearest part, 1 the farthest — fading out over <paramref name="softness"/> past either end.
    /// </summary>
    public static byte[] Span(SceneDepthPlane plane, SKSizeI size, double near, double far, double softness)
        => SpanOnCanvas(plane, SKMatrix.Identity, size, size, near, far, softness);

    /// <summary>
    /// Each pixel's coverage, 0–255, of a <paramref name="canvas"/>-sized picture that <paramref name="photoToCanvas"/> draws a
    /// <paramref name="size"/> photo onto, by the span from <paramref name="near"/> to <paramref name="far"/> of the photo's
    /// distance (<see cref="SceneDepthPlane.Distance"/>), fading out over <paramref name="softness"/> past either end; nothing
    /// where the photo does not reach.
    /// </summary>
    public static byte[] SpanOnCanvas(SceneDepthPlane plane, SKMatrix photoToCanvas, SKSizeI size, SKSizeI canvas, double near, double far, double softness)
    {
        if (!photoToCanvas.TryInvert(out var toPhoto)) return new byte[canvas.Width * canvas.Height];
        var coverage = new byte[canvas.Width * canvas.Height];
        (near, far) = (Math.Min(near, far), Math.Max(near, far));
        double soft = Math.Max(0, softness);
        Parallel.For(0, canvas.Height, row =>
        {
            double cy = row + 0.5;
            for (int col = 0; col < canvas.Width; col++)
            {
                double cx = col + 0.5;
                double px = toPhoto.ScaleX * cx + toPhoto.SkewX * cy + toPhoto.TransX;
                double py = toPhoto.SkewY * cx + toPhoto.ScaleY * cy + toPhoto.TransY;
                if (px < 0 || py < 0 || px >= size.Width || py >= size.Height) continue;
                double distance = plane.Distance(plane.At(px, py, size));
                double inside = distance < near ? Fade(near - distance, soft) : distance > far ? Fade(distance - far, soft) : 1;
                coverage[row * canvas.Width + col] = (byte)Math.Round(inside * 255);
            }
        });
        return coverage;
    }

    /// <summary>How much of a pixel <paramref name="past"/> beyond the span's end is still covered: a smooth fall to nothing over
    /// <paramref name="soft"/>, nothing at once where there is no softness.</summary>
    private static double Fade(double past, double soft)
    {
        if (soft <= 0 || past >= soft) return 0;
        double t = 1 - past / soft;
        return t * t * (3 - 2 * t);
    }

    /// <summary>What <paramref name="step"/> covers on a <paramref name="size"/> photo of scene depth <paramref name="plane"/>.</summary>
    public static TiledCoverage ReachOn(RegionSceneDepth step, SceneDepthPlane plane, SKSizeI size)
        => TiledCoverage.FromBytes(Span(plane, size, step.Near, step.Far, step.Softness), size.Width, size.Height);

    /// <summary><paramref name="region"/> with every span in it worked out on a <paramref name="size"/> photo of scene depth
    /// <paramref name="plane"/>.</summary>
    public static RecipeRegion? Resolved(RecipeRegion? region, SceneDepthPlane plane, SKSizeI size)
        => !Unresolved(region) ? region
         : region! with { Steps = [.. region.Steps.Select(s => s is RegionSceneDepth { Reach: null } span ? span with { Reach = ReachOn(span, plane, size) } : s)] };

    /// <summary><paramref name="region"/> with every span in it worked out on <paramref name="photo"/>'s scene depth, read — or
    /// estimated — only where a span is still to be worked out.</summary>
    /// <exception cref="InvalidOperationException">A span is to be worked out and scene depth is not here.</exception>
    public static async Task<RecipeRegion?> ResolvedAsync(ImageEditingServices services, string photo, RecipeRegion? region, CancellationToken ct)
    {
        if (!Unresolved(region)) return region;
        if (services.SceneDepth is not { State: ToolModelsState.Ready } depth) throw new InvalidOperationException("Scene depth is not here.");
        var plane = await services.SceneDepths.PlaneAsync(services, photo, depth, ct);
        SKSizeI size;
        using (var decoded = await services.DecodeAsync(photo, SceneDepthSizeEdge, ct))
            size = new SKSizeI((int)Math.Round(decoded.SourceSize.Width), (int)Math.Round(decoded.SourceSize.Height));
        return await Task.Run(() => Resolved(region, plane, size), ct);
    }

    /// <summary>The long edge the photo is decoded at to learn its size: as small as a decoder goes.</summary>
    private const int SceneDepthSizeEdge = 64;
}
