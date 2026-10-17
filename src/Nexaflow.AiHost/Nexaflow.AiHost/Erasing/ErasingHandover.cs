using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Erasing;

/// <summary>
/// A region of a picture to and from the inpainter as LaMa's predict hands its pictures over: RGB over 255 and the mask
/// taken where it is not 0, padded at the right and bottom to a multiple of 8 by symmetric mirroring; the result made 8
/// bits by clipping its product with 255 and truncating. A region worked smaller (<see cref="InpaintRegion.Shrunk"/>) goes
/// down by PIL's bilinear resize, which averages over the reduction, its mask marking every pixel a hole reaches, and its
/// fill comes back up by Lanczos. Only the mask's pixels are written back.
/// </summary>
internal static class ErasingHandover
{
    /// <summary>A region ready for the inpainter: planar RGB and the mask, <see cref="Width"/> × <see cref="Height"/>
    /// after padding.</summary>
    public sealed record Prepared(float[] Rgb, float[] Mask, int Width, int Height);

    public static Prepared Prepare(ReadOnlySpan<byte> rgba, int width, ReadOnlySpan<byte> mask, InpaintRegion region)
    {
        int w = region.Width, h = region.Height;
        var rgb = new byte[w * h * 3];
        var hole = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int from = (region.Top + y) * width + region.Left + x, to = y * w + x;
                (rgb[to * 3], rgb[to * 3 + 1], rgb[to * 3 + 2]) = (rgba[from * 4], rgba[from * 4 + 1], rgba[from * 4 + 2]);
                hole[to] = mask[from] != 0 ? (byte)255 : (byte)0;
            }
        if (region.Shrunk)
        {
            rgb = PictureResampling.ResizeBilinearAsPil(rgb, w, h, 3, region.WorkWidth, region.WorkHeight);
            hole = PictureResampling.ResizeBilinearAsPil(hole, w, h, 1, region.WorkWidth, region.WorkHeight);
            (w, h) = (region.WorkWidth, region.WorkHeight);
        }

        int pw = (w + 7) / 8 * 8, ph = (h + 7) / 8 * 8, plane = pw * ph;
        var planar = new float[3 * plane];
        var padded = new float[plane];
        for (int y = 0; y < ph; y++)
        {
            int sy = Symmetric(y, h);
            for (int x = 0; x < pw; x++)
            {
                int from = sy * w + Symmetric(x, w), to = y * pw + x;
                for (int c = 0; c < 3; c++) planar[c * plane + to] = rgb[from * 3 + c] / 255f;
                padded[to] = hole[from] != 0 ? 1f : 0f;
            }
        }
        return new Prepared(planar, padded, pw, ph);
    }

    /// <summary>Writes the inpainter's result for <paramref name="region"/> into <paramref name="rgba"/> where
    /// <paramref name="mask"/> is filled; every other pixel, and the alpha of all, is left as it is.</summary>
    public static void LayBack(Span<byte> rgba, int width, ReadOnlySpan<byte> mask, InpaintRegion region, float[] inpainted, Prepared prepared)
    {
        int w = region.WorkWidth, h = region.WorkHeight, plane = prepared.Width * prepared.Height;
        var fill = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 3; c++)
                    fill[(y * w + x) * 3 + c] = (byte)Math.Clamp(inpainted[c * plane + y * prepared.Width + x] * 255f, 0f, 255f);
        if (region.Shrunk) fill = PictureResampling.ResizeLanczos(fill, w, h, 3, region.Width, region.Height);

        for (int y = 0; y < region.Height; y++)
            for (int x = 0; x < region.Width; x++)
            {
                int to = (region.Top + y) * width + region.Left + x;
                if (mask[to] == 0) continue;
                int from = (y * region.Width + x) * 3;
                (rgba[to * 4], rgba[to * 4 + 1], rgba[to * 4 + 2]) = (fill[from], fill[from + 1], fill[from + 2]);
            }
    }

    /// <summary>NumPy's symmetric padding: past either end the row mirrored about its edge, its edge pixel repeated.</summary>
    private static int Symmetric(int i, int size)
    {
        int period = 2 * size, j = ((i % period) + period) % period;
        return j < size ? j : period - 1 - j;
    }
}
