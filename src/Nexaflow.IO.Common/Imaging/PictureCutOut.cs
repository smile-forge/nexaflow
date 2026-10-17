namespace Nexaflow.IO.Common.Imaging;

/// <summary>A picture cut out of its background by masks: what any of them picks, transparent around it, cropped to it.</summary>
public static class PictureCutOut
{
    /// <summary>
    /// The pixels of <paramref name="rgba"/> — interleaved 8-bit straight-alpha RGBA, <paramref name="width"/> ×
    /// <paramref name="height"/> — where any of <paramref name="masks"/> (a byte a pixel, picked where not 0) picks them,
    /// clear elsewhere, cropped to the box round what is picked; null where nothing is.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height)? Of(ReadOnlySpan<byte> rgba, int width, int height, IReadOnlyList<byte[]> masks)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("The pixels are not the size given.", nameof(rgba));
        if (masks.Any(m => m.Length != width * height)) throw new ArgumentException("A mask is not the picture's size.", nameof(masks));

        var keep = new byte[width * height];
        foreach (var mask in masks)
            for (int i = 0; i < keep.Length; i++) keep[i] |= mask[i];

        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            var row = keep.AsSpan(y * width, width);
            int first = row.IndexOfAnyExcept((byte)0);
            if (first < 0) continue;
            left = Math.Min(left, first);
            right = Math.Max(right, row.LastIndexOfAnyExcept((byte)0));
            top = Math.Min(top, y);
            bottom = y;
        }
        if (right < 0) return null;

        int w = right - left + 1, h = bottom - top + 1;
        var cut = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int from = (y + top) * width + x + left;
                if (keep[from] != 0) rgba.Slice(from * 4, 4).CopyTo(cut.AsSpan((y * w + x) * 4, 4));
            }
        return (cut, w, h);
    }
}
