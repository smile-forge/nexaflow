using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Media.Photoshop;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// A Photoshop file as one picture — what the viewer, the carousel, the album and previews show, and the look a file opens on
/// when it holds layers Nexaflow cannot draw: its merged image, the picture as Photoshop drew it, at its depth. A file saved
/// without Maximize Compatibility has no real merged image, so its layers are composited instead. Where only a smaller picture
/// is wanted, rows and columns are skipped as they are read, so a large document is never held whole.
/// </summary>
public sealed class PhotoshopDecoder(MarkupPainter markup) : IImageDecoder
{
    public bool Decodes(ImageContainer container) => container == ImageContainer.Photoshop;

    public DecodedImage Decode(byte[] data, ImageContainer container, int? maxLongEdge)
    {
        using var stream = new MemoryStream(data, writable: false);
        PhotoshopDocument document;
        try
        {
            document = PhotoshopReader.Read(stream);
        }
        catch (InvalidDataException ex)
        {
            throw new ImageDecodeException($"This Photoshop picture does not read: {ex.Message}");
        }
        var header = document.Header;
        if (PhotoshopFormat.ColourOf(header) is not { } colour)
            throw new ImageDecodeException($"A {header.Depth}-bit {header.Mode} Photoshop picture can't be shown here — only RGB and grayscale.");

        var stored = new SKSizeI(header.Width, header.Height);
    SKBitmap bitmap;
        try
        {
            bitmap = PhotoshopFormat.HasRealMergedImage(document) ? Merged(stream, document, colour, maxLongEdge) : Composited(stream, colour, maxLongEdge);
        }
        catch (InvalidDataException ex)
        {
            throw new ImageDecodeException($"This Photoshop picture does not read: {ex.Message}");
        }
        if (maxLongEdge is { } edge) bitmap = ImageDecoders.Reduce(bitmap, edge);
        return new DecodedImage(ImageDecoders.Seal(bitmap), SKEncodedOrigin.TopLeft, stored, ImageContainer.Photoshop,
                                isAnimated: false, isHighBitDepth: colour.Depth != ChannelDepth.U8);
    }

    /// <summary>
    /// The merged image at every <c>step</c>th row and column — the step the most that still leaves the long edge at least
    /// <paramref name="maxLongEdge"/> — shown as every picture pipeline here takes it: 8-bit as it is, deeper as half floats,
    /// light past white kept.
    /// </summary>
    private static SKBitmap Merged(Stream stream, PhotoshopDocument document, PictureColour colour, int? maxLongEdge)
    {
        var header = document.Header;
        int step = maxLongEdge is { } edge && edge > 0 ? Math.Max(1, Math.Max(header.Width, header.Height) / edge) : 1;
        using var raw = MergedAtDepth(stream, document, colour, step);
        var shown = colour.Depth == ChannelDepth.U8
            ? new SKImageInfo(raw.Width, raw.Height, SKColorType.Rgba8888, SKAlphaType.Premul, colour.ColourSpace)
            : new SKImageInfo(raw.Width, raw.Height, SKColorType.RgbaF16, SKAlphaType.Premul, colour.ColourSpace);
        var bitmap = new SKBitmap(shown);
        if (!raw.PeekPixels().ReadPixels(shown, bitmap.GetPixels(), bitmap.RowBytes))
        {
            bitmap.Dispose();
            throw new ImageDecodeException("This Photoshop picture could not be made a picture.");
        }
        return bitmap;
    }

    /// <summary>
    /// The merged image at every <paramref name="step"/>th row and column, its rows laid straight into a bitmap at the file's own
    /// depth — colour and alpha not premultiplied, grey spread to red, green and blue — and Photoshop's white matte taken off where
    /// it is partly transparent: every value as the file holds it.
    /// </summary>
    internal static SKBitmap MergedAtDepth(Stream stream, PhotoshopDocument document, PictureColour colour, int step = 1)
    {
        var header = document.Header;
        int width = (header.Width + step - 1) / step, height = (header.Height + step - 1) / step;
        int colourChannels = colour.Model == ColourModel.Rgb ? 3 : 1, sampleBytes = header.SampleBytes, pixelBytes = 4 * sampleBytes;
        int alphaChannel = document.MergedHasTransparency && header.Channels > colourChannels ? colourChannels : -1;
        int channels = Math.Min(header.Channels, colourChannels + (alphaChannel >= 0 ? 1 : 0));

        var colourType = sampleBytes switch { 1 => SKColorType.Rgba8888, 2 => SKColorType.Rgba16161616, _ => SKColorType.RgbaF32 };
        var raw = new SKBitmap(new SKImageInfo(width, height, colourType, SKAlphaType.Unpremul, colour.ColourSpace));
        try
        {
            var pixels = raw.GetPixelSpan();
            int rowBytes = raw.RowBytes;
            if (alphaChannel < 0) Opaque(pixels, width, height, rowBytes, sampleBytes);
            PhotoshopChannels.DecodeMerged(stream, document.MergedOffset, header.Channels, header.Width, header.Height, sampleBytes, header.IsLarge, (channel, y, row) =>
            {
                if (channel >= channels || y % step != 0) return;
                var line = raw.GetPixelSpan().Slice((y / step) * rowBytes, width * pixelBytes);
                // Grey is spread to red, green and blue; transparency is the fourth sample.
                int first = channel == alphaChannel ? 3 : channel, last = channel == alphaChannel || colourChannels == 3 ? first : 2;
                for (int x = 0, outX = 0; x < header.Width; x += step, outX++)
                {
                    var sample = row.Slice(x * sampleBytes, sampleBytes);
                    for (int c = first; c <= last; c++) sample.CopyTo(line[(outX * pixelBytes + c * sampleBytes)..]);
                }
            });
            // Photoshop composites its merged image's colour over white where it is partly transparent: taken off again here.
            if (alphaChannel >= 0) UnmatteRows(pixels, width, height, rowBytes, sampleBytes);
            return raw;
        }
        catch
        {
            raw.Dispose();
            throw;
        }
    }

    /// <summary>Every pixel's alpha made wholly opaque, for a merged image that holds none.</summary>
    private static void Opaque(Span<byte> pixels, int width, int height, int rowBytes, int sampleBytes)
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var alpha = pixels.Slice(y * rowBytes + (x * 4 + 3) * sampleBytes, sampleBytes);
                if (sampleBytes == 4) BinaryPrimitives.WriteSingleLittleEndian(alpha, 1f);
                else alpha.Fill(0xFF);
            }
    }

    /// <summary>
    /// A merged image's colour, composited over white — <c>m = c·a + (1 − a)</c> — as the straight colour it was:
    /// <c>c = (m − 1 + a) / a</c>; in place, at the bitmap's own depth.
    /// </summary>
    private static void UnmatteRows(Span<byte> pixels, int width, int height, int rowBytes, int sampleBytes)
    {
        for (int y = 0; y < height; y++)
        {
            var line = pixels.Slice(y * rowBytes, width * 4 * sampleBytes);
            switch (sampleBytes)
            {
                case 1:
                    for (int p = 0; p < line.Length; p += 4)
                    {
                        int a = line[p + 3];
                        if (a == 255) continue;
                        for (int c = 0; c < 3; c++) line[p + c] = a == 0 ? (byte)0 : (byte)Math.Clamp((line[p + c] - 255 + a) * 255 / a, 0, 255);
                    }
                    break;
                case 2:
                {
                    var samples = MemoryMarshal.Cast<byte, ushort>(line);
                    for (int p = 0; p < samples.Length; p += 4)
                    {
                        int a = samples[p + 3];
                        if (a == 65535) continue;
                        for (int c = 0; c < 3; c++)
                            samples[p + c] = a == 0 ? (ushort)0 : (ushort)Math.Clamp((long)(samples[p + c] - 65535 + a) * 65535 / a, 0, 65535);
                    }
                    break;
                }
                default:
                {
                    var samples = MemoryMarshal.Cast<byte, float>(line);
                    for (int p = 0; p < samples.Length; p += 4)
                    {
                        float a = samples[p + 3];
                        if (a >= 1) continue;
                        for (int c = 0; c < 3; c++) samples[p + c] = a <= 0 ? 0 : Math.Max(0, (samples[p + c] - 1 + a) / a);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Straight colour composited over white, as Photoshop holds a merged image's colour where it is partly transparent.</summary>
    internal static float Matted(float colour, float alpha) => alpha >= 1 ? colour : colour * alpha + (1 - alpha);

    /// <summary>The file's layers composited, as Nexaflow draws them — for a file whose merged image is blank.</summary>
    private SKBitmap Composited(Stream stream, PictureColour colour, int? maxLongEdge)
    {
        LayeredPicture picture;
        try
        {
            // Nexaflow writes a real merged image, so a file without one holds none of its picture layers to extract.
            picture = PhotoshopFormat.ReadFrom(stream, Path.GetTempPath()).Read.Picture;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new ImageDecodeException($"This Photoshop picture does not read: {ex.Message}");
        }
        double scale = maxLongEdge is { } edge && edge > 0 ? Math.Min(1, (double)edge / Math.Max(picture.Width, picture.Height)) : 1;
        using var compositor = new LayerCompositor(markup, cache: false);
        using var composite = compositor.Render(picture, scale, new NoPictures());
        var info = new SKImageInfo(composite.Width, composite.Height, colour.Depth == ChannelDepth.U8 ? SKColorType.Rgba8888 : SKColorType.RgbaF16,
                                   SKAlphaType.Premul, colour.ColourSpace);
        var bitmap = new SKBitmap(info);
        composite.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        return bitmap;
    }

    /// <summary>A Photoshop file's layers hold no picture layers: Nexaflow's own are drawn from their pixels here.</summary>
    private sealed class NoPictures : IPictureLayerImages
    {
        public PictureLayerImage? ImageOf(PictureLayer layer, double scale) => null;
    }
}
