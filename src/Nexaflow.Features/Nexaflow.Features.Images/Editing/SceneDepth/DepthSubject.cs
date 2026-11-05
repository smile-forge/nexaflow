using Nexaflow.Features.Common;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// Where a photo's subject lies, read from its scene depth: the nearest stretch of the picture, favouring its middle — what was
/// put in front of the camera — as a box round it in the photo's pixels, for picture selection to pick the thing within. A box
/// round the whole photo leaves picture selection to guess, and it often takes the background.
/// </summary>
public static class DepthSubject
{
    /// <summary>How much less a corner counts than the middle, as a share of its nearness.</summary>
    private const float CornerDiscount = 0.6f;

    /// <summary>How far behind the subject's nearest a pixel may lie and still be the subject, as a share of the depth's range.</summary>
    private const float Reach = 0.2f;

    /// <summary>A subject covering more than this share of the picture is the picture itself: nothing stands out.</summary>
    private const double MostOfThePicture = 0.9;

    /// <summary>The subject's box in the pixels of a photo <paramref name="photoWidth"/> by <paramref name="photoHeight"/>; null where the depth shows nothing standing out.</summary>
    public static PictureBox? Box(SceneDepthPlane plane, double photoWidth, double photoHeight)
    {
        int width = plane.Width, height = plane.Height;
        if (width < 2 || height < 2 || photoWidth <= 0 || photoHeight <= 0) return null;
        var near = Smoothed(plane.Nearness, width, height, Math.Max(1, Math.Min(width, height) / 48));

        // The seed: the nearest place, counted down toward the corners.
        int seed = 0;
        float best = float.MinValue, farthest = float.MaxValue;
        double cx = (width - 1) / 2.0, cy = (height - 1) / 2.0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                double dx = (x - cx) / cx, dy = (y - cy) / cy;
                float centred = (float)Math.Min(1, (dx * dx + dy * dy) / 2);
                float score = near[i] * (1 - CornerDiscount * centred);
                if (score > best) (best, seed) = (score, i);
                farthest = Math.Min(farthest, near[i]);
            }

        // The subject: what is joined to the seed and lies about as near — grown over the depth itself, not its smoothing,
        // which softens the subject's edge away.
        float[] raw = plane.Nearness.ToArray();
        float floor = near[seed] - Reach * (near[seed] - farthest);
        if (near[seed] - farthest <= 1e-4f) return null;
        var reached = new bool[near.Length];
        var open = new Stack<int>();
        open.Push(seed);
        reached[seed] = true;
        int count = 0, left = width, top = height, right = -1, bottom = -1;
        while (open.TryPop(out int i))
        {
            count++;
            int x = i % width, y = i / width;
            (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
            Visit(x - 1, y);
            Visit(x + 1, y);
            Visit(x, y - 1);
            Visit(x, y + 1);
        }
        if (count > MostOfThePicture * near.Length) return null;

        // Into the photo's pixels, a little wider than the subject so its edge is inside the box.
        double sx = photoWidth / width, sy = photoHeight / height;
        double padX = 0.02 * photoWidth, padY = 0.02 * photoHeight;
        return new PictureBox(Math.Max(0, left * sx - padX), Math.Max(0, top * sy - padY),
                              Math.Min(photoWidth, (right + 1) * sx + padX), Math.Min(photoHeight, (bottom + 1) * sy + padY));

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int i = y * width + x;
            if (reached[i] || raw[i] < floor) return;
            reached[i] = true;
            open.Push(i);
        }
    }

    // The nearness averaged over a square reach either side, by running sums, so specks and noise do not seed the subject.
    private static float[] Smoothed(ReadOnlySpan<float> nearness, int width, int height, int reach)
    {
        var across = new float[nearness.Length];
        for (int y = 0; y < height; y++)
        {
            double sum = 0;
            int row = y * width;
            for (int x = -reach; x < width; x++)
            {
                if (x + reach < width) sum += nearness[row + x + reach];
                if (x - reach - 1 >= 0) sum -= nearness[row + x - reach - 1];
                if (x >= 0) across[row + x] = (float)(sum / (Math.Min(width - 1, x + reach) - Math.Max(0, x - reach) + 1));
            }
        }
        var smoothed = new float[nearness.Length];
        for (int x = 0; x < width; x++)
        {
            double sum = 0;
            for (int y = -reach; y < height; y++)
            {
                if (y + reach < height) sum += across[(y + reach) * width + x];
                if (y - reach - 1 >= 0) sum -= across[(y - reach - 1) * width + x];
                if (y >= 0) smoothed[y * width + x] = (float)(sum / (Math.Min(height - 1, y + reach) - Math.Max(0, y - reach) + 1));
            }
        }
        return smoothed;
    }
}
