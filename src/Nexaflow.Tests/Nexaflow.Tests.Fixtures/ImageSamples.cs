namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Image fixtures for the image viewer (carousel / album / explore / collage). A cross-section of the
/// formats the viewer opens — BMP, PNG, GIF and JPEG — in varied aspect ratios and hues: enough images
/// to exercise the carousel's paged position indicator (which windows at 20) and varied shapes for the
/// fit/scale and collage layouts.
///
/// BMP and PNG are generated procedurally and losslessly (BMP: raw 24-bit; PNG: a stored/uncompressed
/// zlib stream), so every byte is a reproducible byte-exact <see cref="SampleFile.Raw"/> sample — no
/// <c>Random</c>, no System.Drawing, no compressor state to seed. GIF and JPEG can't be hand-encoded
/// that simply, so one valid solid-colour sample of each is embedded as a fixed byte constant (produced
/// once via WIC) — still byte-exact and dependency-free at runtime. Every file decodes as a real image.
/// </summary>
internal sealed class ImageSamples : ISampleSet
{
    public string SubDirectory => "images";

    public IReadOnlyList<SampleFile> Files { get; } = Build();

    private const int BmpCount = 14;
    private const int PngCount = 7;

    // Cycle a few aspect ratios so fit/scale and the collage scatter have varied shapes.
    private static readonly (int W, int H)[] _sizes =
        [(200, 150), (150, 200), (180, 180), (240, 135)];

    private static IReadOnlyList<SampleFile> Build()
    {
        var files = new List<SampleFile>(BmpCount + PngCount + 3);
        int hueSteps = BmpCount + PngCount;
        int n = 0;

        (byte r, byte g, byte b) Hue() => HsvToRgb(n * (360.0 / hueSteps), 0.65, 0.92);

        for (int i = 0; i < BmpCount; i++, n++)
        {
            var (w, h) = _sizes[n % _sizes.Length];
            var (r, g, b) = Hue();
            files.Add(SampleFile.Raw($"photo_{n + 1:00}.bmp", Bmp(w, h, r, g, b)));
        }
        for (int i = 0; i < PngCount; i++, n++)
        {
            var (w, h) = _sizes[n % _sizes.Length];
            var (r, g, b) = Hue();
            files.Add(SampleFile.Raw($"photo_{n + 1:00}.png", Png(w, h, r, g, b)));
        }

        // The compressed formats the viewer must also open, as fixed valid samples (.jpg and .jpeg
        // share the same bytes — they are the same format, distinct only by extension).
        files.Add(SampleFile.Raw("photo_22.gif",  Convert.FromBase64String(GifBase64)));
        files.Add(SampleFile.Raw("photo_23.jpg",  Convert.FromBase64String(JpegBase64)));
        files.Add(SampleFile.Raw("photo_24.jpeg", Convert.FromBase64String(JpegBase64)));

        return files;   // 24 total (>20), formats: bmp / png / gif / jpg / jpeg
    }

    // ── BMP: solid-colour 24-bit (BGR, bottom-up) — the simplest losslessly-encodable image ──────────
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

    // ── PNG: solid-colour 8-bit truecolour, hand-encoded with a stored (uncompressed) zlib stream, so
    //    the bytes are deterministic and need no compressor. ───────────────────────────────────────────
    private static byte[] Png(int w, int h, byte r, byte g, byte b)
    {
        // Filtered image data: each scanline is a filter byte (0 = None) followed by w RGB triples.
        int rowLen = 1 + w * 3;
        var raw = new byte[rowLen * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * rowLen;                    // raw[row] stays 0 → filter None
            for (int x = 0; x < w; x++)
            {
                int p = row + 1 + x * 3;
                raw[p] = r; raw[p + 1] = g; raw[p + 2] = b;
            }
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }); // PNG signature

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, w);
        WriteBE(ihdr, 4, h);
        ihdr[8]  = 8;                                // bit depth
        ihdr[9]  = 2;                                // colour type: truecolour RGB
        ihdr[10] = 0;                                // compression: deflate
        ihdr[11] = 0;                                // filter method
        ihdr[12] = 0;                                // interlace: none
        WriteChunk(ms, "IHDR", ihdr);
        WriteChunk(ms, "IDAT", ZlibStore(raw));
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        Span<byte> len = stackalloc byte[4];
        WriteBE(len, 0, data.Length);
        s.Write(len);
        s.Write(typeBytes);
        s.Write(data);

        uint crc = 0xFFFFFFFFu;
        crc = Crc32(crc, typeBytes);
        crc = Crc32(crc, data);
        Span<byte> crcb = stackalloc byte[4];
        WriteBE(crcb, 0, (int)(crc ^ 0xFFFFFFFFu));
        s.Write(crcb);
    }

    /// <summary>Wraps <paramref name="data"/> in a zlib stream that uses only stored (BTYPE=00,
    /// uncompressed) deflate blocks — valid zlib, no compressor required.</summary>
    private static byte[] ZlibStore(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);                          // CMF: deflate, 32K window
        ms.WriteByte(0x01);                          // FLG: (0x78 0x01) satisfies the zlib FCHECK

        int offset = 0;
        do
        {
            int chunk = Math.Min(0xFFFF, data.Length - offset);
            bool last = offset + chunk >= data.Length;
            ms.WriteByte((byte)(last ? 1 : 0));      // BFINAL + BTYPE=00 (stored)
            ms.WriteByte((byte)(chunk & 0xFF));      // LEN  (little-endian)
            ms.WriteByte((byte)(chunk >> 8));
            ms.WriteByte((byte)(~chunk & 0xFF));     // NLEN (one's complement of LEN)
            ms.WriteByte((byte)(~chunk >> 8 & 0xFF));
            ms.Write(data, offset, chunk);
            offset += chunk;
        }
        while (offset < data.Length);

        WriteBEStream(ms, Adler32(data));            // zlib trailer: Adler-32 of the uncompressed data
        return ms.ToArray();
    }

    private static uint Crc32(uint crc, byte[] bytes)
    {
        foreach (var by in bytes)
        {
            crc ^= by;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc;
    }

    private static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        foreach (var by in data)
        {
            a = (a + by) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }

    private static void WriteI32(byte[] buf, int off, int v)
    {
        buf[off]     = (byte)v;
        buf[off + 1] = (byte)(v >> 8);
        buf[off + 2] = (byte)(v >> 16);
        buf[off + 3] = (byte)(v >> 24);
    }

    private static void WriteBE(Span<byte> buf, int off, int v)
    {
        buf[off]     = (byte)(v >> 24);
        buf[off + 1] = (byte)(v >> 16);
        buf[off + 2] = (byte)(v >> 8);
        buf[off + 3] = (byte)v;
    }

    private static void WriteBEStream(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
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

    // One valid solid-colour sample each of GIF and JPEG (produced once via WIC), embedded verbatim so
    // the fixtures stay dependency-free yet the set covers the lossy/indexed formats the viewer opens.
    private const string GifBase64 =
        "R0lGODlhtAC0AHAAACwAAAAAtAC0AIHSgigAAAAAAAAAAAACfYSPqcvtD6OctNqLs968+w+G4kiW5omm6sq27gvH8kzX9o3n+s73/g8MCofEovGITCqXzKbzCY1Kp9Sq9YrNarfcrvcLDovH5LL5jE6r1+y2+w2Py+f0uv2Oz+v3/L7/DxgoOEhYaHiImKi4yNjo+AgZKTlJWWl5iZmpucnZQOn5CRoqOkpaanqKmqq6ytrq+gobKztLW2t7i5uru8vb6/sLHCw8TFxsfIycrLzM3Oz8DB0tPU1dbX2Nna29zd0p7f0NHi4+Tl5ufo6err7O3u7+Dh8vP09fb3+Pn6+/z9/v/w8woMAJBQAAOw==";

    private const string JpegBase64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAQDAwMDAgQDAwMEBAQFBgoGBgUFBgwICQcKDgwPDg4MDQ0PERYTDxAVEQ0NExoTFRcYGRkZDxIbHRsYHRYYGRj/2wBDAQQEBAYFBgsGBgsYEA0QGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBj/wAARCACWAMgDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDLooor7A/DwooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigD//Z";
}
