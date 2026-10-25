namespace Nexaflow.AiHost.Drawing;

/// <summary>What a decoder that draws in planes gives — [3, h, w] in [0, 1] — as the picture a drawing method returns.</summary>
internal static class DecodedPixels
{
    /// <summary>Decoder pixels, [3, h, w] in [0, 1], as interleaved 8-bit RGB.</summary>
    public static byte[] Rgb(ReadOnlySpan<float> pixels, int height, int width)
    {
        int plane = height * width;
        var rgb = new byte[plane * 3];
        for (int i = 0; i < plane; i++)
            for (int c = 0; c < 3; c++)
                rgb[i * 3 + c] = (byte)Math.Clamp((int)MathF.Round(pixels[c * plane + i] * 255f), 0, 255);
        return rgb;
    }
}
