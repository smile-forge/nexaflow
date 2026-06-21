namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Image fixtures for the image viewer (carousel / album / explore / collage). Twenty-four small,
/// solid-colour 24-bit BMPs in varied aspect ratios — enough images to exercise the carousel's
/// paged position indicator (which windows at 20) and varied shapes for the fit/scale and collage
/// layouts. BMP is used so the bytes are generated with no compression or CRC, keeping every file a
/// reproducible byte-exact <see cref="SampleFile.Raw"/> sample (no <c>Random</c>, no System.Drawing).
/// </summary>
internal sealed class ImageSamples : ISampleSet
{
    public string SubDirectory => "images";

    public IReadOnlyList<SampleFile> Files { get; } = Build();

    private const int Count = 24;

    // Cycle a few aspect ratios so fit/scale and the collage scatter have varied shapes.
    private static readonly (int W, int H)[] _sizes =
        [(200, 150), (150, 200), (180, 180), (240, 135)];

    private static IReadOnlyList<SampleFile> Build()
    {
        var files = new List<SampleFile>(Count);
        for (int i = 0; i < Count; i++)
        {
            var (w, h) = _sizes[i % _sizes.Length];
            var (r, g, b) = HsvToRgb(i * (360.0 / Count), 0.65, 0.92);   // evenly-spaced hues
            files.Add(SampleFile.Raw($"photo_{i + 1:00}.bmp", Bmp(w, h, r, g, b)));
        }
        return files;
    }

    /// <summary>A solid-colour 24-bit (BGR, bottom-up) BMP — the simplest losslessly-encodable image.</summary>
    private static byte[] Bmp(int w, int h, byte r, byte g, byte b)
    {
        int rowSize = (w * 3 + 3) / 4 * 4;          // rows padded to a 4-byte boundary
        int pixels  = rowSize * h;
        var buf     = new byte[54 + pixels];

        buf[0] = (byte)'B'; buf[1] = (byte)'M';
        WriteI32(buf, 2, buf.Length);               // file size
        WriteI32(buf, 10, 54);                       // pixel-data offset
        WriteI32(buf, 14, 40);                       // DIB header size
        WriteI32(buf, 18, w);
        WriteI32(buf, 22, h);
        buf[26] = 1;                                 // planes
        buf[28] = 24;                                // bits per pixel
        WriteI32(buf, 34, pixels);                   // image size
        WriteI32(buf, 38, 2835);                      // 72 DPI in pixels/metre
        WriteI32(buf, 42, 2835);

        for (int y = 0; y < h; y++)
        {
            int row = 54 + y * rowSize;
            for (int x = 0; x < w; x++)
            {
                buf[row + x * 3 + 0] = b;
                buf[row + x * 3 + 1] = g;
                buf[row + x * 3 + 2] = r;
            }
        }
        return buf;
    }

    private static void WriteI32(byte[] buf, int off, int v)
    {
        buf[off]     = (byte)v;
        buf[off + 1] = (byte)(v >> 8);
        buf[off + 2] = (byte)(v >> 16);
        buf[off + 3] = (byte)(v >> 24);
    }

    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60  => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _     => (c, 0.0, x),
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
