using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;
using System.Runtime.Intrinsics;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>
/// How a tile of pixels lays out its bytes for one <see cref="PictureColour"/>: the channels — red, green, blue and alpha,
/// or grey and alpha — each <see cref="SampleBytes"/> wide, pixel after pixel, row by row. 8-bit colour is premultiplied, as
/// every tile was before deeper ones; every other format is not, so a value read from a file is written back the same.
/// </summary>
/// <remarks>
/// Every change to pixels goes through <see cref="ToPremul"/> and <see cref="FromPremul"/>: premultiplied red, green, blue
/// and alpha as floats in the picture's own values — a grey picture's red, green and blue alike — so one piece of code
/// changes pixels of any format.
/// </remarks>
public sealed class TileFormat
{
    public const int TileSize = 256;

    private const int TileArea = TileSize * TileSize;

    private static readonly TileFormat[] Formats =
    [
        new(new PictureColour(ColourModel.Rgb, ChannelDepth.U8)),
        new(new PictureColour(ColourModel.Rgb, ChannelDepth.U16)),
        new(new PictureColour(ColourModel.Rgb, ChannelDepth.F32)),
        new(new PictureColour(ColourModel.Grey, ChannelDepth.U8)),
        new(new PictureColour(ColourModel.Grey, ChannelDepth.U16)),
        new(new PictureColour(ColourModel.Grey, ChannelDepth.F32)),
    ];

    private static readonly float[] ByteToUnit = Enumerable.Range(0, 256).Select(v => v / 255f).ToArray();

    /// <summary>Every 8-bit colour premultiplied by every alpha, rounded — <c>(c·a + 127) / 255</c> — at <c>a · 256 + c</c>.</summary>
    private static readonly byte[] PremulBytes = BytesOf((c, a) => (c * a + 127) / 255);

    /// <summary>Every 8-bit premultiplied colour divided by every alpha, rounded and held to 255 — 0 where the alpha is — at
    /// <c>a · 256 + c</c>.</summary>
    private static readonly byte[] StraightBytes = BytesOf((c, a) => a == 0 ? 0 : Math.Min(255, (c * 255 + a / 2) / a));

    private static byte[] BytesOf(Func<int, int, int> colourOf)
    {
        var bytes = new byte[256 * 256];
        for (int a = 0; a < 256; a++)
            for (int c = 0; c < 256; c++) bytes[a << 8 | c] = (byte)colourOf(c, a);
        return bytes;
    }

    /// <summary>An 8-bit straight colour as an 8-bit premultiplied tile holds it beside <paramref name="alpha"/>.</summary>
    public static byte PremulByte(int colour, int alpha) => PremulBytes[alpha << 8 | colour];

    /// <summary>An 8-bit premultiplied colour as it is straight, beside <paramref name="alpha"/>.</summary>
    public static byte StraightByte(int colour, int alpha) => StraightBytes[alpha << 8 | colour];

    // Grey and alpha read from a two-channel tile, premultiplied, as the colour every surface draws.
    private const string GreySwizzle = "uniform shader tile; half4 main(float2 p) { half4 c = tile.eval(p); return half4(c.r * c.g, c.r * c.g, c.r * c.g, c.g); }";

    private static readonly Lazy<SKRuntimeEffect> Swizzle = new(() =>
        SKRuntimeEffect.CreateShader(GreySwizzle, out var error) ?? throw new InvalidOperationException($"The grey tile shader did not build: {error}"));

    private readonly ConditionalWeakTable<byte[], SKImage> _images = new();

    private TileFormat(PictureColour colour)
    {
        Colour = colour;
        Channels = colour.Model == ColourModel.Rgb ? 4 : 2;
        SampleBytes = colour.Depth switch { ChannelDepth.U8 => 1, ChannelDepth.U16 => 2, _ => 4 };
        PixelBytes = Channels * SampleBytes;
        TileBytes = TileArea * PixelBytes;
        Premultiplied = colour == PictureColour.Rgb8;
    }

    /// <summary>The format holding <paramref name="colour"/>'s pixels.</summary>
    public static TileFormat Of(PictureColour colour) => Formats[(int)colour.Model * 3 + (int)colour.Depth];

    /// <summary>8-bit colour, premultiplied.</summary>
    public static TileFormat Rgb8 => Formats[0];

    public PictureColour Colour { get; }

    /// <summary>4 for colour and alpha, 2 for grey and alpha.</summary>
    public int Channels { get; }

    public int SampleBytes { get; }

    public int PixelBytes { get; }

    /// <summary>The bytes of one whole tile.</summary>
    public int TileBytes { get; }

    /// <summary>Whether colour is held multiplied by alpha — only 8-bit colour.</summary>
    public bool Premultiplied { get; }

    /// <summary>
    /// A new tile, wholly transparent, on the pinned object heap: it never moves, so Skia draws straight from it
    /// (<see cref="Draw"/>) and no tile is ever held twice. A tile is a large object either way, which the collector does not
    /// compact, so pinning costs nothing.
    /// </summary>
    public byte[] NewTile() => GC.AllocateArray<byte>(TileBytes, pinned: true);

    /// <summary>A copy of <paramref name="tile"/> to change, pinned as every tile is (<see cref="NewTile"/>).</summary>
    public byte[] CopyOf(byte[] tile)
    {
        var copy = GC.AllocateUninitializedArray<byte>(TileBytes, pinned: true);
        tile.CopyTo(copy, 0);
        return copy;
    }

    /// <summary>
    /// What a bitmap of these pixels is, colour and alpha, deep enough to hold them: 8-bit premultiplied colour, or 16-bit or
    /// float colour not premultiplied — a grey picture's grey in all three colour channels.
    /// </summary>
    public SKImageInfo BitmapInfo(int width, int height) => Colour.Depth switch
    {
        ChannelDepth.U8 => new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, Colour.ColourSpace),
        ChannelDepth.U16 => new(width, height, SKColorType.Rgba16161616, SKAlphaType.Unpremul, Colour.ColourSpace),
        _ => new(width, height, SKColorType.RgbaF32, SKAlphaType.Unpremul, Colour.ColourSpace),
    };

    // ── Reading and writing values ──────────────────────────────────────────

    /// <summary>Whether the pixel at <paramref name="index"/> is wholly transparent.</summary>
    public bool IsClearAt(ReadOnlySpan<byte> pixels, int index)
    {
        int at = index * PixelBytes + (Channels - 1) * SampleBytes;
        return SampleBytes switch
        {
            1 => pixels[at] == 0,
            2 => pixels[at] == 0 && pixels[at + 1] == 0,
            _ => BinaryPrimitives.ReadSingleLittleEndian(pixels[at..]) <= 0,
        };
    }

    /// <summary>Whether any pixel of <paramref name="pixels"/> shows anything — read alpha by alpha, stepping over the colour.</summary>
    public bool AnyPainted(ReadOnlySpan<byte> pixels)
    {
        int alpha = (Channels - 1) * SampleBytes;
        switch (SampleBytes)
        {
            case 1:
                for (int i = alpha; i < pixels.Length; i += PixelBytes)
                    if (pixels[i] != 0) return true;
                return false;
            case 2:
                for (int i = alpha; i < pixels.Length; i += PixelBytes)
                    if ((pixels[i] | pixels[i + 1]) != 0) return true;
                return false;
            default:
                for (int i = alpha; i < pixels.Length; i += PixelBytes)
                    if (BinaryPrimitives.ReadSingleLittleEndian(pixels[i..]) > 0) return true;
                return false;
        }
    }

    /// <summary>
    /// <paramref name="pixels"/> as premultiplied red, green, blue and alpha, four floats a pixel, in the picture's values;
    /// alpha between 0 and 1, colour as deep as it is held.
    /// </summary>
    public void ToPremul(ReadOnlySpan<byte> pixels, Span<float> rgba)
    {
        int count = pixels.Length / PixelBytes;
        if (rgba.Length < count * 4) throw new ArgumentException("There is not room for the values.", nameof(rgba));
        switch (Colour.Model, Colour.Depth)
        {
            case (ColourModel.Rgb, ChannelDepth.U8):
            {
                // Four pixels at a time: sixteen bytes widened to floats.
                int i = 0, end = count * 4;
                for (; i + 16 <= end; i += 16)
                {
                    var (low, high) = Vector128.Widen(Vector128.Create(pixels.Slice(i, 16)));
                    var (first, second) = Vector128.Widen(low);
                    var (third, fourth) = Vector128.Widen(high);
                    (Vector128.ConvertToSingle(first) / ByteScale).CopyTo(rgba[i..]);
                    (Vector128.ConvertToSingle(second) / ByteScale).CopyTo(rgba[(i + 4)..]);
                    (Vector128.ConvertToSingle(third) / ByteScale).CopyTo(rgba[(i + 8)..]);
                    (Vector128.ConvertToSingle(fourth) / ByteScale).CopyTo(rgba[(i + 12)..]);
                }
                for (; i < end; i++) rgba[i] = ByteToUnit[pixels[i]];
                break;
            }
            case (ColourModel.Rgb, ChannelDepth.U16):
            {
                var samples = MemoryMarshal.Cast<byte, ushort>(pixels);
                // Two pixels at a time; an odd one left over below.
                int pairs = count & ~1;
                for (int p = 0; p < pairs * 4; p += 8)
                {
                    var (first, second) = Vector128.Widen(Vector128.Create(samples.Slice(p, 8)));
                    PremulPixel(Vector128.ConvertToSingle(first) / UShortScale).CopyTo(rgba[p..]);
                    PremulPixel(Vector128.ConvertToSingle(second) / UShortScale).CopyTo(rgba[(p + 4)..]);
                }
                for (int i = pairs, p = pairs * 4; i < count; i++, p += 4)
                {
                    float a = samples[p + 3] / 65535f;
                    rgba[p] = samples[p] / 65535f * a;
                    rgba[p + 1] = samples[p + 1] / 65535f * a;
                    rgba[p + 2] = samples[p + 2] / 65535f * a;
                    rgba[p + 3] = a;
                }
                break;
            }
            case (ColourModel.Rgb, ChannelDepth.F32):
            {
                var samples = MemoryMarshal.Cast<byte, float>(pixels);
                for (int p = 0; p < count * 4; p += 4) PremulPixel(Vector128.Create(samples.Slice(p, 4))).CopyTo(rgba[p..]);
                break;
            }
            case (ColourModel.Grey, ChannelDepth.U8):
                for (int i = 0, p = 0; i < count; i++, p += 4) Grey(rgba, p, ByteToUnit[pixels[i * 2]], ByteToUnit[pixels[i * 2 + 1]]);
                break;
            case (ColourModel.Grey, ChannelDepth.U16):
            {
                var samples = MemoryMarshal.Cast<byte, ushort>(pixels);
                for (int i = 0, p = 0; i < count; i++, p += 4) Grey(rgba, p, samples[i * 2] / 65535f, samples[i * 2 + 1] / 65535f);
                break;
            }
            default:
            {
                var samples = MemoryMarshal.Cast<byte, float>(pixels);
                for (int i = 0, p = 0; i < count; i++, p += 4) Grey(rgba, p, samples[i * 2], Math.Clamp(samples[i * 2 + 1], 0, 1));
                break;
            }
        }
    }

    private static void Grey(Span<float> rgba, int p, float grey, float alpha)
    {
        float premul = grey * alpha;
        rgba[p] = rgba[p + 1] = rgba[p + 2] = premul;
        rgba[p + 3] = alpha;
    }

    /// <summary>
    /// Premultiplied red, green, blue and alpha, four floats a pixel, written as <paramref name="pixels"/> in this format — a
    /// grey picture taking the colour's grey; 8 and 16 bits clamped to their range, float kept as it is.
    /// </summary>
    public void FromPremul(ReadOnlySpan<float> rgba, Span<byte> pixels)
    {
        int count = pixels.Length / PixelBytes;
        if (rgba.Length < count * 4) throw new ArgumentException("The values do not fill the pixels.", nameof(rgba));
        switch (Colour.Model, Colour.Depth)
        {
            case (ColourModel.Rgb, ChannelDepth.U8):
            {
                // Four pixels at a time, narrowed to sixteen bytes; the rest one by one below.
                int quads = count & ~3;
                for (int p = 0; p < quads * 4; p += 16)
                {
                    var first = Quantised(ClampedPixel(Vector128.Create(rgba.Slice(p, 4))), ByteScale);
                    var second = Quantised(ClampedPixel(Vector128.Create(rgba.Slice(p + 4, 4))), ByteScale);
                    var third = Quantised(ClampedPixel(Vector128.Create(rgba.Slice(p + 8, 4))), ByteScale);
                    var fourth = Quantised(ClampedPixel(Vector128.Create(rgba.Slice(p + 12, 4))), ByteScale);
                    Vector128.Narrow(Vector128.Narrow(first, second), Vector128.Narrow(third, fourth)).CopyTo(pixels[p..]);
                }
                for (int i = quads, p = quads * 4; i < count; i++, p += 4)
                {
                    float a = Math.Clamp(rgba[p + 3], 0, 1);
                    pixels[p + 3] = ToByte(a);
                    pixels[p] = ToByte(Math.Min(rgba[p], a));
                    pixels[p + 1] = ToByte(Math.Min(rgba[p + 1], a));
                    pixels[p + 2] = ToByte(Math.Min(rgba[p + 2], a));
                }
                break;
            }
            case (ColourModel.Rgb, ChannelDepth.U16):
            {
                var samples = MemoryMarshal.Cast<byte, ushort>(pixels);
                // Two pixels at a time, narrowed to eight samples; an odd one left over below.
                int pairs = count & ~1;
                for (int p = 0; p < pairs * 4; p += 8)
                {
                    var first = Quantised(StraightPixel(Vector128.Create(rgba.Slice(p, 4))), UShortScale);
                    var second = Quantised(StraightPixel(Vector128.Create(rgba.Slice(p + 4, 4))), UShortScale);
                    Vector128.Narrow(first, second).CopyTo(samples[p..]);
                }
                for (int i = pairs, p = pairs * 4; i < count; i++, p += 4)
                {
                    float a = Math.Clamp(rgba[p + 3], 0, 1);
                    samples[p + 3] = ToUShort(a);
                    if (a <= 0) { samples[p] = samples[p + 1] = samples[p + 2] = 0; continue; }
                    samples[p] = ToUShort(rgba[p] / a);
                    samples[p + 1] = ToUShort(rgba[p + 1] / a);
                    samples[p + 2] = ToUShort(rgba[p + 2] / a);
                }
                break;
            }
            case (ColourModel.Rgb, ChannelDepth.F32):
            {
                var samples = MemoryMarshal.Cast<byte, float>(pixels);
                for (int p = 0; p < count * 4; p += 4) StraightPixel(Vector128.Create(rgba.Slice(p, 4))).CopyTo(samples[p..]);
                break;
            }
            case (ColourModel.Grey, ChannelDepth.U8):
                for (int i = 0, p = 0; i < count; i++, p += 4)
                {
                    var (grey, a) = GreyOf(rgba, p);
                    pixels[i * 2] = ToByte(grey);
                    pixels[i * 2 + 1] = ToByte(a);
                }
                break;
            case (ColourModel.Grey, ChannelDepth.U16):
            {
                var samples = MemoryMarshal.Cast<byte, ushort>(pixels);
                for (int i = 0, p = 0; i < count; i++, p += 4)
                {
                    var (grey, a) = GreyOf(rgba, p);
                    samples[i * 2] = ToUShort(grey);
                    samples[i * 2 + 1] = ToUShort(a);
                }
                break;
            }
            default:
            {
                var samples = MemoryMarshal.Cast<byte, float>(pixels);
                for (int i = 0, p = 0; i < count; i++, p += 4)
                {
                    var (grey, a) = GreyOf(rgba, p);
                    samples[i * 2] = grey;
                    samples[i * 2 + 1] = a;
                }
                break;
            }
        }
    }

    /// <summary>A premultiplied pixel's grey, no longer premultiplied, and its alpha.</summary>
    private static (float Grey, float Alpha) GreyOf(ReadOnlySpan<float> rgba, int p)
    {
        float a = Math.Clamp(rgba[p + 3], 0, 1);
        if (a <= 0) return (0, 0);
        bool neutral = rgba[p] == rgba[p + 1] && rgba[p + 1] == rgba[p + 2];
        float premul = neutral ? rgba[p] : PictureColour.GreyOf(rgba[p], rgba[p + 1], rgba[p + 2]);
        return (premul / a, a);
    }

    private static byte ToByte(float unit) => (byte)Math.Clamp((int)MathF.Round(unit * 255), 0, 255);

    private static ushort ToUShort(float unit) => (ushort)Math.Clamp((int)MathF.Round(unit * 65535), 0, 65535);

    private static readonly Vector128<float> ByteScale = Vector128.Create(255f), UShortScale = Vector128.Create(65535f);

    /// <summary>The alpha lane of a pixel's four.</summary>
    private static readonly Vector128<float> AlphaLane = Vector128.Create(0u, 0u, 0u, uint.MaxValue).AsSingle();

    /// <summary>A pixel's alpha, held between 0 and 1, in all four lanes.</summary>
    private static Vector128<float> AlphaOf(Vector128<float> pixel)
        => Vector128.Clamp(Vector128.Shuffle(pixel, Vector128.Create(3, 3, 3, 3)), Vector128<float>.Zero, Vector128<float>.One);

    /// <summary>A straight pixel premultiplied, its alpha held between 0 and 1.</summary>
    private static Vector128<float> PremulPixel(Vector128<float> pixel)
    {
        var alpha = AlphaOf(pixel);
        return Vector128.ConditionalSelect(AlphaLane, alpha, pixel * alpha);
    }

    /// <summary>A premultiplied pixel no longer premultiplied — wholly zero where it is wholly transparent.</summary>
    private static Vector128<float> StraightPixel(Vector128<float> pixel)
    {
        var alpha = AlphaOf(pixel);
        var straight = Vector128.ConditionalSelect(AlphaLane, alpha, pixel / alpha);
        return Vector128.ConditionalSelect(Vector128.GreaterThan(alpha, Vector128<float>.Zero), straight, Vector128<float>.Zero);
    }

    /// <summary>A premultiplied pixel with no colour brighter than its alpha, as an 8-bit premultiplied tile holds it.</summary>
    private static Vector128<float> ClampedPixel(Vector128<float> pixel)
    {
        var alpha = AlphaOf(pixel);
        return Vector128.ConditionalSelect(AlphaLane, alpha, Vector128.Min(pixel, alpha));
    }

    /// <summary>Units scaled to whole samples — rounded half to even, as <see cref="ToByte"/> and <see cref="ToUShort"/> are —
    /// and held between 0 and <paramref name="scale"/>.</summary>
    private static Vector128<uint> Quantised(Vector128<float> units, Vector128<float> scale)
        => Vector128.ConvertToUInt32(Vector128.Clamp(Vector128.Round(units * scale), Vector128<float>.Zero, scale));

    /// <summary>
    /// <paramref name="bitmap"/>'s pixels in this format, row by row: drawn into this format's own bitmap — so converted from
    /// whatever colour, depth and space it is in — then laid out as its channels.
    /// </summary>
    public byte[] PixelsOf(SKBitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var info = Colour.Model == ColourModel.Rgb ? BitmapInfo(width, height)
            : new SKImageInfo(width, height, SKColorType.RgbaF32, SKAlphaType.Premul, Colour.ColourSpace);
        using var converted = new SKBitmap(info);
        using (var image = SKImage.FromPixelCopy(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes))
            if (image is null || !image.ReadPixels(info, converted.GetPixels(), converted.RowBytes, 0, 0))
                throw new InvalidOperationException("The picture could not be read as pixels.");
        var pixels = new byte[width * height * PixelBytes];
        var origin = converted.GetPixels();
        if (Colour.Model == ColourModel.Rgb)
        {
            for (int row = 0; row < height; row++)
                Marshal.Copy(origin + row * converted.RowBytes, pixels, row * width * PixelBytes, width * PixelBytes);
            return pixels;
        }
        var premul = new float[width * 4];
        for (int row = 0; row < height; row++)
        {
            Marshal.Copy(origin + row * converted.RowBytes, premul, 0, width * 4);
            FromPremul(premul, pixels.AsSpan(row * width * PixelBytes, width * PixelBytes));
        }
        return pixels;
    }

    /// <summary><paramref name="pixels"/> — <paramref name="width"/> wide, in this format, row by row — as a bitmap of
    /// <see cref="BitmapInfo"/> the caller owns.</summary>
    public SKBitmap BitmapOf(ReadOnlySpan<byte> pixels, int width, int height)
    {
        var bitmap = new SKBitmap(BitmapInfo(width, height));
        if (Colour.Model == ColourModel.Rgb)
        {
            for (int row = 0; row < height; row++)
                pixels.Slice(row * width * PixelBytes, width * PixelBytes)
                      .CopyTo(bitmap.GetPixelSpan().Slice(row * bitmap.RowBytes, width * PixelBytes));
            return bitmap;
        }
        // Grey spread to colour, kept as deep as it is: each grey written to red, green and blue.
        var colour = TileFormat.Of(Colour with { Model = ColourModel.Rgb });
        var premul = new float[width * 4];
        var line = new byte[width * colour.PixelBytes];
        for (int row = 0; row < height; row++)
        {
            ToPremul(pixels.Slice(row * width * PixelBytes, width * PixelBytes), premul);
            colour.FromPremul(premul, line);
            line.CopyTo(bitmap.GetPixelSpan().Slice(row * bitmap.RowBytes, line.Length));
        }
        return bitmap;
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="tile"/> drawn with its top-left at (<paramref name="x"/>, <paramref name="y"/>) in
    /// <paramref name="canvas"/>'s current matrix. A colour tile is made a Skia image once, the first time it is drawn; a grey
    /// one is read through a shader that spreads its grey to colour, so it is never held twice — but a float grey, which Skia
    /// has no two-channel image for, is spread once into a colour image.
    /// </summary>
    public void Draw(SKCanvas canvas, byte[] tile, int x, int y, SKSamplingOptions sampling, SKPaint? paint)
    {
        if (Colour.Model == ColourModel.Rgb || Colour.Depth == ChannelDepth.F32)
        {
            canvas.DrawImage(_images.GetValue(tile, ImageOf), x, y, sampling, paint);
            return;
        }
        var image = _images.GetValue(tile, ImageOf);
        using var raw = image.ToRawShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, SKMatrix.CreateTranslation(x, y));
        var children = new SKRuntimeEffectChildren(Swizzle.Value) { ["tile"] = raw };
        using var shader = Swizzle.Value.ToShader(new SKRuntimeEffectUniforms(Swizzle.Value), children);
        using var draw = paint?.Clone() ?? new SKPaint();
        draw.Shader = shader;
        canvas.DrawRect(SKRect.Create(x, y, TileSize, TileSize), draw);
    }

    /// <summary>
    /// A tile as a Skia image over its own bytes, not a copy of them: every tile is pinned (<see cref="NewTile"/>) and never
    /// changes once made, and the image is reached only through the tile, so it is never drawn once the tile is gone. A float
    /// grey tile, which Skia has no two-channel image for, is the one spread into an image of its own.
    /// </summary>
    private SKImage ImageOf(byte[] tile)
    {
        SKImage? image = (Colour.Model, Colour.Depth) switch
        {
            (ColourModel.Rgb, _) => Over(BitmapInfo(TileSize, TileSize), tile),
            (ColourModel.Grey, ChannelDepth.U8) => Over(new SKImageInfo(TileSize, TileSize, SKColorType.Rg88, SKAlphaType.Opaque), tile),
            (ColourModel.Grey, ChannelDepth.U16) => Over(new SKImageInfo(TileSize, TileSize, SKColorType.Rg1616, SKAlphaType.Opaque), tile),
            _ => SpreadGrey(tile),
        };
        return image ?? throw new InvalidOperationException("A tile could not be made an image.");
    }

    private static SKImage? Over(SKImageInfo info, byte[] tile)
        => SKImage.FromPixels(info, Marshal.UnsafeAddrOfPinnedArrayElement(tile, 0), info.RowBytes);

    private SKImage? SpreadGrey(byte[] tile)
    {
        using var bitmap = BitmapOf(tile, TileSize, TileSize);
        return SKImage.FromPixelCopy(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes);
    }
}
