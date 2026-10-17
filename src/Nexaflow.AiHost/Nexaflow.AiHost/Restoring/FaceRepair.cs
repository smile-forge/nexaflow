using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Restoring;

/// <summary>A face found: its box, how surely it is a face, and its five points — eyes, nose, mouth corners — in pixels.</summary>
internal sealed record FoundFace(float Left, float Top, float Right, float Bottom, float Score, IReadOnlyList<(double X, double Y)> Points);

/// <summary>
/// Reads RetinaFace's regressions as facexlib does: each against its prior — three scales of square anchors, two sizes at
/// each, over the picture in steps of 8, 16 and 32 pixels — kept where it is surely a face, the surest first, a box that
/// overlaps a surer one by 40% or more dropped.
/// </summary>
internal static class RetinaFaceDecoding
{
    private static readonly int[] Steps = [8, 16, 32];
    private static readonly int[][] AnchorSizes = [[16, 32], [64, 128], [256, 512]];
    private const float CentreVariance = 0.1f, SizeVariance = 0.2f;

    /// <summary>How sure a face must be to be kept, as GFPGAN's helper asks.</summary>
    public const float Threshold = 0.97f;

    /// <summary>How far a box may overlap a surer one before it is taken for the same face.</summary>
    public const float Overlap = 0.4f;

    /// <summary>The priors for a <paramref name="width"/> × <paramref name="height"/> picture: centre and size, as
    /// fractions of the picture, in the order the network gives its regressions.</summary>
    public static float[] Priors(int width, int height)
    {
        var priors = new List<float>();
        for (int k = 0; k < Steps.Length; k++)
        {
            int step = Steps[k], rows = (height + step - 1) / step, cols = (width + step - 1) / step;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    foreach (int size in AnchorSizes[k])
                    {
                        priors.Add((j + 0.5f) * step / width);
                        priors.Add((i + 0.5f) * step / height);
                        priors.Add((float)size / width);
                        priors.Add((float)size / height);
                    }
        }
        return [.. priors];
    }

    /// <summary>The faces in the network's outputs for a <paramref name="width"/> × <paramref name="height"/> picture.</summary>
    public static IReadOnlyList<FoundFace> Decode(float[] boxes, float[] scores, float[] landmarks, int width, int height)
    {
        var priors = Priors(width, height);
        int count = priors.Length / 4;
        if (scores.Length != count || boxes.Length != count * 4 || landmarks.Length != count * 10)
            throw new ArgumentException($"The detector gave {scores.Length} scores where a {width}x{height} picture has {count} priors.");

        var found = new List<FoundFace>();
        for (int n = 0; n < count; n++)
        {
            if (!(scores[n] > Threshold)) continue;
            float px = priors[n * 4], py = priors[n * 4 + 1], pw = priors[n * 4 + 2], ph = priors[n * 4 + 3];
            float cx = px + boxes[n * 4] * CentreVariance * pw, cy = py + boxes[n * 4 + 1] * CentreVariance * ph;
            float w = pw * MathF.Exp(boxes[n * 4 + 2] * SizeVariance), h = ph * MathF.Exp(boxes[n * 4 + 3] * SizeVariance);
            float left = cx - w / 2, top = cy - h / 2;
            var points = new (double, double)[5];
            for (int p = 0; p < 5; p++)
                points[p] = ((px + landmarks[n * 10 + 2 * p] * CentreVariance * pw) * width,
                             (py + landmarks[n * 10 + 2 * p + 1] * CentreVariance * ph) * height);
            found.Add(new(left * width, top * height, (left + w) * width, (top + h) * height, scores[n], points));
        }
        return Suppressed([.. found.OrderByDescending(f => f.Score)]);
    }

    /// <summary>py_cpu_nms: surest first, each box dropped that overlaps one kept by <see cref="Overlap"/> or more, a box's
    /// area counted inclusively of its last pixel.</summary>
    private static IReadOnlyList<FoundFace> Suppressed(IReadOnlyList<FoundFace> bySureness)
    {
        var kept = new List<FoundFace>();
        foreach (var face in bySureness)
        {
            float area = (face.Right - face.Left + 1) * (face.Bottom - face.Top + 1);
            bool same = kept.Any(k =>
            {
                float w = MathF.Max(0, MathF.Min(face.Right, k.Right) - MathF.Max(face.Left, k.Left) + 1);
                float h = MathF.Max(0, MathF.Min(face.Bottom, k.Bottom) - MathF.Max(face.Top, k.Top) + 1);
                float inter = w * h, other = (k.Right - k.Left + 1) * (k.Bottom - k.Top + 1);
                return inter / (area + other - inter) > Overlap;
            });
            if (!same) kept.Add(face);
        }
        return kept;
    }
}

/// <summary>
/// How GFPGAN's helper (facexlib's <c>FaceRestoreHelper</c>) takes a face out of a picture and puts the remade one back:
/// aligned to its 512-pixel template by its five points, and pasted over the picture through the remade face's own mask,
/// blurred so it fades into what surrounds it.
/// </summary>
internal static class FaceAlignment
{
    /// <summary>Where a face's five points sit in its 512-pixel crop: eyes, nose, mouth corners.</summary>
    public static readonly IReadOnlyList<(double X, double Y)> Template =
        [(192.98138, 239.94708), (318.90277, 240.1936), (256.63416, 314.01935), (201.26117, 371.41043), (313.08905, 371.15118)];

    /// <summary>A face whose eyes are closer than this, in pixels, is seen side-on or too small to remake.</summary>
    public const double MinimumEyeDistance = 5;

    /// <summary>The grey a crop is filled with past the picture's edge — OpenCV's (135, 133, 132) BGR, in RGB.</summary>
    public static ReadOnlySpan<byte> CropBorder => [132, 133, 135];

    /// <summary>How the face's points map onto the template's.</summary>
    public static Affine2D Alignment(FoundFace face) => Affine2D.Similarity(face.Points, Template);

    /// <summary>The map from the template back onto the picture <paramref name="scale"/> times the size, nudged by half
    /// a scaled pixel as the helper nudges it to land more exactly.</summary>
    public static Affine2D PasteBack(Affine2D alignment, int scale)
    {
        var back = alignment.Inverse().Scaled(scale);
        return scale > 1 ? back.Moved(0.5 * scale, 0.5 * scale) : back;
    }

    /// <summary>
    /// The mask a remade face is pasted back through, from the parser's 0/1 face mask: scaled to 255, blurred twice with
    /// a 101-pixel Gaussian of sigma 11, its outer 10 pixels cleared, back to 0..1 — as the helper makes it.
    /// </summary>
    public static float[] SoftMask(float[] parsed)
    {
        const int size = FaceRestorer.Size, edge = 10;
        var mask = new float[parsed.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = parsed[i] * 255f;
        PictureResampling.GaussianBlur(mask, size, size, 101, 11);
        PictureResampling.GaussianBlur(mask, size, size, 101, 11);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                mask[y * size + x] = y < edge || y >= size - edge || x < edge || x >= size - edge ? 0 : mask[y * size + x] / 255f;
        return mask;
    }

    /// <summary>
    /// Pastes a remade face over interleaved RGBA <paramref name="picture"/> through its soft mask, where
    /// <paramref name="pasteBack"/> lands them: each pixel the mask's share of the face and the rest of the picture,
    /// truncated to a byte as the helper's last step does. Only the face's footprint is touched; alpha is kept.
    /// </summary>
    public static void Paste(byte[] picture, int width, int height, byte[] faceRgb, float[] softMask, Affine2D pasteBack)
    {
        const int size = FaceRestorer.Size;
        if (Footprint(pasteBack, width, height) is not { } region) return;
        var face = PictureResampling.WarpAffine(faceRgb, size, size, 3, pasteBack, region, [0, 0, 0]);
        var mask = PictureResampling.WarpAffine(softMask, size, size, pasteBack, region);
        Parallel.For(0, region.Height, ry =>
        {
            for (int rx = 0; rx < region.Width; rx++)
            {
                float m = mask[ry * region.Width + rx];
                if (m == 0) continue;
                int at = ((region.Y + ry) * width + region.X + rx) * 4, from = (ry * region.Width + rx) * 3;
                for (int c = 0; c < 3; c++)
                    picture[at + c] = (byte)Math.Clamp((int)(m * face[from + c] + (1 - m) * picture[at + c]), 0, 255);
            }
        });
    }

    /// <summary>The picture's pixels the template's square lands on, a pixel either side for the bilinear taps.</summary>
    private static (int X, int Y, int Width, int Height)? Footprint(Affine2D pasteBack, int width, int height)
    {
        const int size = FaceRestorer.Size;
        var corners = new[] { pasteBack.Map(0, 0), pasteBack.Map(size, 0), pasteBack.Map(0, size), pasteBack.Map(size, size) };
        int x0 = Math.Max(0, (int)Math.Floor(corners.Min(c => c.X)) - 1), y0 = Math.Max(0, (int)Math.Floor(corners.Min(c => c.Y)) - 1);
        int x1 = Math.Min(width, (int)Math.Ceiling(corners.Max(c => c.X)) + 2), y1 = Math.Min(height, (int)Math.Ceiling(corners.Max(c => c.Y)) + 2);
        return x1 > x0 && y1 > y0 ? (x0, y0, x1 - x0, y1 - y0) : null;
    }
}
