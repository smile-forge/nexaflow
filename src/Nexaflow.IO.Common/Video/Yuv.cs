namespace Nexaflow.IO.Common.Video;

/// <summary>
/// RGB to the YUV video encoders take: BT.709, limited range (luma 16–235, chroma 16–240), as NV12 — a full-size luma
/// plane, then the chroma of each 2×2 block, its average, U and V interleaved.
/// </summary>
internal static class Yuv
{
    // BT.709: Y = 0.2126 R + 0.7152 G + 0.0722 B; Cb = (B − Y) / 1.8556; Cr = (R − Y) / 1.5748; scaled to 219 and 224 steps.
    private const float Kr = 0.2126f, Kb = 0.0722f, Kg = 1f - Kr - Kb;
    private const float LumaScale = 219f / 255f, ChromaScale = 224f / 255f;

    /// <param name="rgb">The frame, row by row, three bytes a pixel.</param>
    /// <param name="nv12">width × height × 3/2 bytes.</param>
    public static void FromRgb(ReadOnlySpan<byte> rgb, int width, int height, Span<byte> nv12)
    {
        var luma = nv12[..(width * height)];
        var chroma = nv12[(width * height)..];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 3;
                luma[y * width + x] = Clamp(16f + LumaScale * (Kr * rgb[at] + Kg * rgb[at + 1] + Kb * rgb[at + 2]));
            }

        for (int y = 0; y < height; y += 2)
            for (int x = 0; x < width; x += 2)
            {
                float r = 0, g = 0, b = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int at = ((y + dy) * width + x + dx) * 3;
                        r += rgb[at];
                        g += rgb[at + 1];
                        b += rgb[at + 2];
                    }
                r /= 4; g /= 4; b /= 4;
                float luminance = Kr * r + Kg * g + Kb * b;
                int at2 = (y / 2) * width + x;
                chroma[at2]     = Clamp(128f + ChromaScale * (b - luminance) / (2 * (1 - Kb)));
                chroma[at2 + 1] = Clamp(128f + ChromaScale * (r - luminance) / (2 * (1 - Kr)));
            }
    }

    private static byte Clamp(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
}
