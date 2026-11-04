namespace Nexaflow.IO.Common.Imaging;

/// <summary>
/// A picture brought to whole patches within a square's area, as HiDream-O1's <c>resize_pilimage</c> does: halved with a
/// box filter while it is still twice the square's side, then scaled — up or down — to the largest size of whole patches
/// that fits within the square's area, each side its scaled length rounded or cut to the patch, and resized bicubically
/// to cover that size, the rest cropped from the middle. PIL's filters do the resizing (<see cref="PilFilter"/>).
/// </summary>
public static class PatchFitting
{
    /// <summary>The size a <paramref name="width"/> × <paramref name="height"/> picture is brought to, whole
    /// <paramref name="patch"/>-pixel patches within <paramref name="side"/>².</summary>
    public static (int Width, int Height) SizeOf(int width, int height, int side, int patch)
    {
        Validate(width, height, side, patch);
        (width, height) = Halved(width, height, side);
        return Within(width, height, side, patch);
    }

    /// <summary>Interleaved 8-bit RGB brought to <see cref="SizeOf"/>.</summary>
    public static (byte[] Rgb, int Width, int Height) Fit(ReadOnlySpan<byte> rgb, int width, int height, int side, int patch)
    {
        Validate(width, height, side, patch);
        if (rgb.Length != width * height * 3) throw new ArgumentException("The pixels are not the size given.", nameof(rgb));
        var pixels = rgb.ToArray();
        while (Math.Min(width, height) >= 2 * side)
        {
            pixels = PictureResampling.ResizeAsPil(pixels, width, height, 3, width / 2, height / 2, PilFilter.Box);
            width /= 2;
            height /= 2;
        }

        var (newWidth, newHeight) = Within(width, height, side, patch);
        double s1 = (double)width / newWidth, s2 = (double)height / newHeight;
        if (s1 < s2)
        {
            int covered = (int)Math.Round(height / s1);
            var resized = PictureResampling.ResizeAsPil(pixels, width, height, 3, newWidth, covered, PilFilter.Bicubic);
            int top = (covered - newHeight) / 2;
            return (resized.AsSpan(top * newWidth * 3, newHeight * newWidth * 3).ToArray(), newWidth, newHeight);
        }
        else
        {
            int covered = (int)Math.Round(width / s2);
            var resized = PictureResampling.ResizeAsPil(pixels, width, height, 3, covered, newHeight, PilFilter.Bicubic);
            int left = (covered - newWidth) / 2;
            var cropped = new byte[newWidth * newHeight * 3];
            for (int y = 0; y < newHeight; y++)
                resized.AsSpan((y * covered + left) * 3, newWidth * 3).CopyTo(cropped.AsSpan(y * newWidth * 3));
            return (cropped, newWidth, newHeight);
        }
    }

    private static void Validate(int width, int height, int side, int patch)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "A picture has a size.");
        if (patch <= 0 || side < patch) throw new ArgumentOutOfRangeException(nameof(side), "The square holds at least one patch.");
    }

    private static (int Width, int Height) Halved(int width, int height, int side)
    {
        while (Math.Min(width, height) >= 2 * side) (width, height) = (width / 2, height / 2);
        return (width, height);
    }

    /// <summary>The four sizes a side rounded or cut to whole patches gives, the largest within the area — or the
    /// smallest, where none is.</summary>
    private static (int Width, int Height) Within(int width, int height, int side, int patch)
    {
        double area = (double)side * side;
        double scale = Math.Sqrt(area / ((double)width * height));
        int Rounded(double length) => (int)Math.Round(length) / patch * patch;
        int Cut(double length) => (int)Math.Floor(length) / patch * patch;
        (int Width, int Height)[] sizes =
        [
            (Rounded(width * scale), Rounded(height * scale)),
            (Rounded(width * scale), Cut(height * scale)),
            (Cut(width * scale), Rounded(height * scale)),
            (Cut(width * scale), Cut(height * scale)),
        ];
        var ordered = sizes.OrderByDescending(s => (long)s.Width * s.Height).ToArray();   // stable, as Python's sort is
        foreach (var size in ordered)
            if ((long)size.Width * size.Height <= area) return size;
        return ordered[^1];
    }
}
