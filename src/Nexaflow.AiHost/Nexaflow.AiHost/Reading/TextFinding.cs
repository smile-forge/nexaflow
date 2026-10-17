using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Reading;

/// <summary>
/// PaddleOCR's preparation of a picture for its text detector, and the regions read from the detector's map — ported
/// from the recipe's <c>text_finding.py</c>, which the build and the reference share.
/// <para>The picture: BGR, its long side brought to 960 with each side a multiple of 32 (at least 32) by a bilinear
/// resize — OpenCV's, which reads each output pixel from its source position without averaging a reduction — kept as
/// 8-bit, then scaled to 0–1 and normalised by ImageNet's mean and deviation — given in RGB order, applied by PaddleOCR
/// in channel order to its BGR picture, so blue is taken from red's — planar.</para>
/// <para>The regions, as DBPostProcess finds them a box at a time: pixels above 0.3 joined into regions, 8-connected;
/// each scored by the mean probability over its bounding box and kept from 0.6; each grown back out by the shrink the
/// detector was trained with — unclip ratio 1.5, by area × 1.5 / perimeter on every side.</para>
/// </summary>
internal static class TextFinding
{
    public const int LongSide = 960;
    private const float Threshold = 0.3f, BoxThreshold = 0.6f, Unclip = 1.5f;
    private const int MinSide = 3;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f], Deviation = [0.229f, 0.224f, 0.225f];

    /// <summary>The detector's size for a <paramref name="width"/> × <paramref name="height"/> picture.</summary>
    public static (int Width, int Height) SizeFor(int width, int height)
    {
        double scale = (double)LongSide / Math.Max(width, height);
        static int Side(double side) => Math.Max(32, (int)Math.Round(side / 32, MidpointRounding.ToEven) * 32);
        return (Side(width * scale), Side(height * scale));
    }

    /// <summary>The detector's input for an RGBA picture: [1, 3, H, W], blue first.</summary>
    public static (float[] Input, int Width, int Height) Prepare(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var (w, h) = SizeFor(width, height);
        int plane = w * h;
        var input = new float[3 * plane];
        var channel = new float[width * height];
        for (int c = 0; c < 3; c++)
        {
            int rgb = 2 - c;   // BGR
            for (int i = 0; i < channel.Length; i++) channel[i] = rgba[i * 4 + rgb];
            var resized = PictureResampling.ResizePlaneBilinear(channel, width, height, w, h);
            var into = input.AsSpan(c * plane, plane);
            for (int i = 0; i < plane; i++)
            {
                float level = MathF.Round(Math.Clamp(resized[i], 0f, 255f), MidpointRounding.ToEven);   // OpenCV keeps it 8-bit
                into[i] = (level / 255f - Mean[c]) / Deviation[c];
            }
        }
        return (input, w, h);
    }

    /// <summary>The regions of text on the detector's map, <paramref name="width"/> × <paramref name="height"/>, in
    /// fractions of it.</summary>
    public static IReadOnlyList<TextRegion> Regions(ReadOnlySpan<float> probability, int width, int height)
    {
        var label = new int[width * height];
        var stack = new Stack<int>();
        var found = new List<TextRegion>();
        int next = 0;
        for (int start = 0; start < label.Length; start++)
        {
            if (label[start] != 0 || probability[start] <= Threshold) continue;
            label[start] = ++next;
            stack.Push(start);
            int x0 = width, y0 = height, x1 = -1, y1 = -1;
            while (stack.Count > 0)
            {
                int at = stack.Pop(), x = at % width, y = at / width;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int n = ny * width + nx;
                    if (label[n] != 0 || probability[n] <= Threshold) continue;
                    label[n] = next;
                    stack.Push(n);
                }
            }
            int boxWidth = x1 - x0 + 1, boxHeight = y1 - y0 + 1;
            if (Math.Min(boxWidth, boxHeight) < MinSide) continue;
            double sum = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) sum += probability[y * width + x];
            float score = (float)(sum / (boxWidth * boxHeight));
            if (score < BoxThreshold) continue;
            float grow = boxWidth * boxHeight * Unclip / (2f * (boxWidth + boxHeight));
            float left = Math.Max(0, x0 - grow), top = Math.Max(0, y0 - grow);
            float right = Math.Min(width, x0 + boxWidth + grow), bottom = Math.Min(height, y0 + boxHeight + grow);
            found.Add(new TextRegion(left / width, top / height, (right - left) / width, (bottom - top) / height, score));
        }
        return found;
    }
}
