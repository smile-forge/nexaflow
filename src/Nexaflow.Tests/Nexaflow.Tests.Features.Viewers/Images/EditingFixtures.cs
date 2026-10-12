using System.Buffers.Binary;
using System.IO;
using System.Text;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Pictures for the editing tests: in-memory quadrant pictures whose colours say where each part went, for the
/// pixel checks; and, for anything that reads or writes files, the generated image samples — chiefly
/// <see cref="OrientedPhotoSample"/>, the photo with EXIF, GPS and XMP — copied out before anything is written.
/// </summary>
internal static class EditingFixtures
{
    public static readonly SKColor TopLeft     = new(220, 40, 40);
    public static readonly SKColor TopRight    = new(40, 200, 40);
    public static readonly SKColor BottomLeft  = new(40, 40, 220);
    public static readonly SKColor BottomRight = new(230, 230, 40);

    /// <summary>A <paramref name="width"/>×<paramref name="height"/> picture in four flat quadrants.</summary>
    public static SKBitmap Quadrants(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        void Fill(SKColor c, float l, float t, float r, float b) { paint.Color = c; canvas.DrawRect(new SKRect(l, t, r, b), paint); }
        Fill(TopLeft, 0, 0, width / 2f, height / 2f);
        Fill(TopRight, width / 2f, 0, width, height / 2f);
        Fill(BottomLeft, 0, height / 2f, width / 2f, height);
        Fill(BottomRight, width / 2f, height / 2f, width, height);
        return bitmap;
    }

    /// <summary>One flat colour.</summary>
    public static SKBitmap Flat(int width, int height, SKColor colour)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        bitmap.Erase(colour);
        return bitmap;
    }

    /// <summary>A decoded picture over <paramref name="bitmap"/> at full resolution, stored with <paramref name="origin"/>.</summary>
    public static DecodedImage Decoded(SKBitmap bitmap, SKEncodedOrigin origin = SKEncodedOrigin.TopLeft) =>
        new(SKImage.FromBitmap(bitmap), origin, new SKSizeI(bitmap.Width, bitmap.Height), ImageContainer.Png,
            isAnimated: false, isHighBitDepth: false);

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality = 95)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    // ── The generated samples ───────────────────────────────────────────────

    /// <summary>The generated photo stored on its side with EXIF, GPS and XMP (<see cref="OrientedPhotoSample"/>).</summary>
    public static byte[] OrientedPhotoBytes() => File.ReadAllBytes(TestSampleData.Path("images", OrientedPhotoSample.FileName));

    /// <summary>
    /// A copy of a generated sample in <paramref name="dir"/> — the tests that write a sidecar or a save beside a photo
    /// do it to a copy, never in the shared dataset.
    /// </summary>
    public static string CopyOfSample(string dir, string sampleName)
    {
        var path = Path.Combine(dir, sampleName);
        File.Copy(TestSampleData.Path("images", sampleName), path);
        return path;
    }

    /// <summary>The first generated PNG sample's name — a plain photo with no metadata.</summary>
    public static string PlainSample => Path.GetFileName(TestSampleData.Files("images").First(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));

    public static SKColor Colour(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    // ── Reading EXIF back ───────────────────────────────────────────────────

    /// <summary>The inline value of <paramref name="tag"/> in IFD0 (or in the directory a pointer leads to).</summary>
    public static uint? Tag(byte[] exif, ushort tag, ushort? via = null)
    {
        bool big = exif[0] == 'M';
        uint directory = Read32(exif, 4, big);
        if (via is { } pointer)
        {
            if (Tag(exif, pointer) is not { } at) return null;
            directory = at;
        }
        int count = Read16(exif, (int)directory, big);
        for (int i = 0; i < count; i++)
        {
            int e = (int)directory + 2 + 12 * i;
            if (Read16(exif, e, big) != tag) continue;
            ushort type = Read16(exif, e + 2, big);
            return type == 3 ? Read16(exif, e + 8, big) : Read32(exif, e + 8, big);
        }
        return null;
    }

    /// <summary>The ASCII value of <paramref name="tag"/> in IFD0.</summary>
    public static string? AsciiTag(byte[] exif, ushort tag)
    {
        bool big = exif[0] == 'M';
        uint directory = Read32(exif, 4, big);
        int count = Read16(exif, (int)directory, big);
        for (int i = 0; i < count; i++)
        {
            int e = (int)directory + 2 + 12 * i;
            if (Read16(exif, e, big) != tag) continue;
            uint n = Read32(exif, e + 4, big);
            int at = n <= 4 ? e + 8 : (int)Read32(exif, e + 8, big);
            return Encoding.ASCII.GetString(exif, at, (int)n).TrimEnd('\0');
        }
        return null;
    }

    /// <summary>The offset of the directory after IFD0 — 0 once the thumbnail is cut loose.</summary>
    public static uint NextDirectory(byte[] exif)
    {
        bool big = exif[0] == 'M';
        uint directory = Read32(exif, 4, big);
        return Read32(exif, (int)directory + 2 + 12 * Read16(exif, (int)directory, big), big);
    }

    private static ushort Read16(byte[] b, int at, bool big) => big ? BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static uint Read32(byte[] b, int at, bool big) => big ? BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));

    /// <summary>The colour at (x, y) of a rendered picture, read as 8-bit sRGB.</summary>
    public static SKColor PixelAt(SKImage picture, int x, int y)
    {
        using var one = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb()));
        Assert.IsTrue(picture.ReadPixels(one.Info, one.GetPixels(), one.RowBytes, x, y), $"no pixel at {x},{y}");
        return one.GetPixel(0, 0);
    }

    public static void AssertNear(SKColor expected, SKColor actual, int tolerance = 6, string? because = null)
    {
        bool near = Math.Abs(expected.Red - actual.Red) <= tolerance
                 && Math.Abs(expected.Green - actual.Green) <= tolerance
                 && Math.Abs(expected.Blue - actual.Blue) <= tolerance
                 && Math.Abs(expected.Alpha - actual.Alpha) <= tolerance;
        Assert.IsTrue(near, $"expected ≈{expected}, got {actual}{(because is null ? "" : " — " + because)}");
    }

    public static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexedit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
