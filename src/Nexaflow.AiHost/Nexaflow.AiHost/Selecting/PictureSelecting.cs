using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Selecting;

/// <summary>
/// SAM 3.1's still-picture paths around its graphs, as the makers run them: the picture prepared as their predictor
/// prepares a frame, clicks put in the encoder's square, the click decoder's mask chosen as <c>_forward_sam_heads</c>
/// chooses it, a phrase's finds kept and scored as their <c>Sam3Processor</c> keeps them, and each mask brought to the
/// size asked for as they bring it to the picture.
/// </summary>
internal static class PictureSelecting
{
    /// <summary>The phrase the makers give the finder when only example boxes say what is meant.</summary>
    public const string ExamplesOnly = "visual";

    /// <summary>How sure a find must be to be kept: its score joined with the presence score, as the makers keep it.</summary>
    public const float KeptAbove = 0.5f;

    /// <summary>The picture squashed to the encoder's square by PIL's bilinear resize — its alpha dropped, as PIL's
    /// <c>convert("RGB")</c> drops it — then planar RGB from -1 to 1.</summary>
    public static float[] Prepare(byte[] rgba, int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3)
        {
            rgb[j] = rgba[i];
            rgb[j + 1] = rgba[i + 1];
            rgb[j + 2] = rgba[i + 2];
        }
        const int side = PictureEncoder.Side;
        var squashed = PictureResampling.ResizeBilinearAsPil(rgb, width, height, 3, side, side);
        var planar = new float[3 * side * side];
        Parallel.For(0, side * side, i =>
        {
            for (int c = 0; c < 3; c++) planar[c * side * side + i] = (squashed[i * 3 + c] / 255f - 0.5f) / 0.5f;
        });
        return planar;
    }

    /// <summary>The points in the encoder's square, and their labels: 1 on the thing, 0 off it, 2 and 3 a box's corners.</summary>
    public static (float[] Points, float[] Labels) Prompt(IReadOnlyList<SelectionPoint> points, int width, int height)
    {
        var coordinates = new float[points.Count * 2];
        var labels = new float[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            coordinates[2 * i] = (float)(points[i].X * (double)PictureEncoder.Side / width);
            coordinates[2 * i + 1] = (float)(points[i].Y * (double)PictureEncoder.Side / height);
            labels[i] = points[i].Kind switch
            {
                SelectionPointKind.Include        => 1,
                SelectionPointKind.Exclude        => 0,
                SelectionPointKind.BoxTopLeft     => 2,
                SelectionPointKind.BoxBottomRight => 3,
                _ => throw new HostRequestException(HostErrorKind.Invalid, $"{points[i].Kind} is not a point a selection takes."),
            };
        }
        return (coordinates, labels);
    }

    /// <summary>The decoder's mask the makers take: for one point, the best by predicted IoU of the three it makes for
    /// one; for several, the one it makes for several; none where its object score says nothing is there.</summary>
    public static float[]? Chosen(float[] masks, float[] ious, float objectScore, int points)
    {
        if (objectScore <= 0) return null;
        int pick = 0;
        if (points <= 1)
        {
            pick = 1;
            for (int i = 2; i <= 3; i++)
                if (ious[i] > ious[pick]) pick = i;
        }
        const int plane = ClickDecoder.Side * ClickDecoder.Side;
        return masks.AsSpan(pick * plane, plane).ToArray();
    }

    /// <summary>A click's mask logits at <paramref name="width"/> × <paramref name="height"/>, as the makers bring them to
    /// the picture: to the encoder's square, then to the size.</summary>
    public static float[] ClickMaskAt(float[] low, int width, int height)
    {
        var square = PictureResampling.ResizePlaneBilinear(low, ClickDecoder.Side, ClickDecoder.Side, PictureEncoder.Side, PictureEncoder.Side);
        return PictureResampling.ResizePlaneBilinear(square, PictureEncoder.Side, PictureEncoder.Side, width, height);
    }

    /// <summary>A find's mask logits at <paramref name="width"/> × <paramref name="height"/>, as the makers' processor
    /// brings them to the picture: in one step.</summary>
    public static float[] FoundMaskAt(float[] low, int width, int height)
        => PictureResampling.ResizePlaneBilinear(low, ClickDecoder.Side, ClickDecoder.Side, width, height);

    /// <summary>Logits as a mask: 255 where above 0 — the makers' sigmoid above a half — and 0 elsewhere.</summary>
    public static void Threshold(ReadOnlySpan<float> logits, Span<byte> into)
    {
        for (int i = 0; i < logits.Length; i++) into[i] = logits[i] > 0 ? (byte)255 : (byte)0;
    }

    /// <summary>The box round a mask of <paramref name="width"/> × <paramref name="height"/>, in the pixels of a picture of
    /// <paramref name="pictureWidth"/> × <paramref name="pictureHeight"/>; null for a mask with nothing in it.</summary>
    public static SelectionBox? Bounds(ReadOnlySpan<byte> mask, int width, int height, int pictureWidth, int pictureHeight)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            var row = mask.Slice(y * width, width);
            int first = row.IndexOfAnyExcept((byte)0);
            if (first < 0) continue;
            int last = row.LastIndexOfAnyExcept((byte)0);
            left = Math.Min(left, first);
            right = Math.Max(right, last);
            top = Math.Min(top, y);
            bottom = y;
        }
        if (right < 0) return null;
        float sx = (float)pictureWidth / width, sy = (float)pictureHeight / height;
        return new SelectionBox(left * sx, top * sy, (right + 1) * sx, (bottom + 1) * sy);
    }

    /// <summary>Example boxes as the finder takes them — centre, width and height from 0 to 1 of the picture — and their
    /// labels: 1 like, 0 unlike.</summary>
    public static (float[] Boxes, long[] Labels) Examples(IReadOnlyList<SelectionExample> examples, int width, int height)
    {
        var boxes = new float[examples.Count * 4];
        var labels = new long[examples.Count];
        for (int i = 0; i < examples.Count; i++)
        {
            var b = examples[i].Box;
            boxes[4 * i] = (b.Left + b.Right) / 2 / width;
            boxes[4 * i + 1] = (b.Top + b.Bottom) / 2 / height;
            boxes[4 * i + 2] = (b.Right - b.Left) / width;
            boxes[4 * i + 3] = (b.Bottom - b.Top) / height;
            labels[i] = examples[i].Like ? 1 : 0;
        }
        return (boxes, labels);
    }

    /// <summary>The finds the makers keep, surest first: each query's score joined with the presence score, kept above
    /// <see cref="KeptAbove"/>, with its box in the picture's pixels.</summary>
    public static IReadOnlyList<(int Query, FoundInPicture Found)> Kept(float[] logits, float presence, float[] boxes, int width, int height)
    {
        float present = Sigmoid(presence);
        var kept = new List<(int, FoundInPicture)>();
        for (int q = 0; q < logits.Length; q++)
        {
            float score = Sigmoid(logits[q]) * present;
            if (score <= KeptAbove) continue;
            float cx = boxes[4 * q], cy = boxes[4 * q + 1], w = boxes[4 * q + 2], h = boxes[4 * q + 3];
            kept.Add((q, new FoundInPicture(score, new SelectionBox((cx - w / 2) * width, (cy - h / 2) * height, (cx + w / 2) * width, (cy + h / 2) * height))));
        }
        return [.. kept.OrderByDescending(k => k.Item2.Score)];
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));
}
