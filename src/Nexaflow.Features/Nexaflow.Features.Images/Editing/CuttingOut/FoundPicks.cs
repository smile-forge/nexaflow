using Nexaflow.Features.Common;

namespace Nexaflow.Features.Images.Editing.CuttingOut;

/// <summary>
/// The things a phrase found, refined by clicks in the order they were made: a click off a thing lets go of whatever is picked
/// under it, and a click on a thing not picked yet adds that thing. So five chairs found, one right-clicked, leaves four.
/// </summary>
/// <remarks>
/// Masks are at the size the finds were asked for; clicks and boxes are in the photo's pixels. The same steps serve the live
/// pick and the kept mask, so a cut-out made later is the one that was shown.
/// </remarks>
public static class FoundPicks
{
    /// <summary>
    /// <paramref name="finds"/> with <paramref name="clicks"/> applied, as one mask with the bounds of what it keeps, and how
    /// many things it keeps.
    /// </summary>
    /// <param name="pickAt">Picks the one thing under a click, at the finds' size, for a click on something not found.</param>
    public static async Task<(PictureMask Mask, int Things)> CombineAsync(IReadOnlyList<PictureFind> finds, IReadOnlyList<PicturePoint> clicks,
                                                                         double photoWidth, double photoHeight, int width, int height,
                                                                         Func<PicturePoint, Task<PictureMask>> pickAt)
    {
        double sx = width / photoWidth, sy = height / photoHeight;
        bool Covers(PictureMask mask, PicturePoint click)
        {
            int x = (int)(click.X * sx), y = (int)(click.Y * sy);
            return x >= 0 && y >= 0 && x < mask.Width && y < mask.Height && mask.Pixels[y * mask.Width + x] >= 128;
        }

        var kept = finds.Select(f => (f.Mask, f.Box)).ToList();
        foreach (var click in clicks)
        {
            if (!click.Include) kept.RemoveAll(thing => Covers(thing.Mask, click));
            else if (!kept.Any(thing => Covers(thing.Mask, click)) && await pickAt(click) is { Bounds: { } box } picked) kept.Add((picked, box));
        }

        var pixels = new byte[width * height];
        foreach (var (mask, _) in kept)
            for (int i = 0; i < pixels.Length; i++) pixels[i] |= mask.Pixels[i];
        PictureBox? bounds = kept.Count == 0 ? null : new PictureBox(kept.Min(k => k.Box.Left), kept.Min(k => k.Box.Top),
                                                                     kept.Max(k => k.Box.Right), kept.Max(k => k.Box.Bottom));
        return (new PictureMask(pixels, width, height, bounds), kept.Count);
    }
}
