using System.Buffers.Binary;
using System.IO;
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
        var bitmap = PhotoshopFormat.HasRealMergedImage(document) ? Merged(stream, document, colour, maxLongEdge) : Composited(stream, colour, maxLongEdge);
        if (maxLongEdge is { } edge) bitmap = ImageDecoders.Reduce(bitmap, edge);
        return new DecodedImage(ImageDecoders.Seal(bitmap), SKEncodedOrigin.TopLeft, stored, ImageContainer.Photoshop,
                                isAnimated: false, isHighBitDepth: colour.Depth != ChannelDepth.U8);
    }

    /// <summary>
    /// The merged image at every <c>step</c>th row and column — the step the most that still leaves the long edge at least
    /// <paramref name="maxLongEdge"/> — as straight colour and alpha: 8-bit as it is, deeper as half floats, which every
    /// picture pipeline here takes, light past white kept.
    /// </summary>
    private static SKBitmap Merged(Stream stream, PhotoshopDocument document, PictureColour colour, int? maxLongEdge)
    {
        var header = document.Header;
        int step = maxLongEdge is { } edge && edge > 0 ? Math.Max(1, Math.Max(header.Width, header.Height) / edge) : 1;
        int width = (header.Width + step - 1) / step, height = (header.Height + step - 1) / step;
        int colourChannels = colour.Model == ColourModel.Rgb ? 3 : 1, sampleBytes = header.SampleBytes;
        int alphaChannel = document.MergedHasTransparency && header.Channels > colourChannels ? colourChannels : -1;
        int channels = Math.Min(header.Channels, colourChannels + (alphaChannel >= 0 ? 1 : 0));

        var values = new float[width * height * 4];
        if (alphaChannel < 0)
            for (int p = 3; p < values.Length; p += 4) values[p] = 1;
        PhotoshopChannels.DecodeMerged(stream, document.MergedOffset, header.Channels, header.Width, header.Height, sampleBytes, header.IsLarge, (channel, y, row) =>
        {
            if (channel >= channels || y % step != 0) return;
            int outY = y / step;
            for (int x = 0, outX = 0; x < header.Width; x += step, outX++)
            {
                float v = sampleBytes switch
                {
                    1 => row[x] / 255f,
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]) / 65535f,
                    _ => BinaryPrimitives.ReadSingleLittleEndian(row[(x * 4)..]),
                };
                int at = (outY * width + outX) * 4;
                if (channel == alphaChannel) values[at + 3] = v;
                else if (colourChannels == 1) values[at] = values[at + 1] = values[at + 2] = v;
                else values[at + channel] = v;
            }
        });

        // Photoshop composites its merged image's colour over white where it is partly transparent: taken off again here.
        if (alphaChannel >= 0) Unmatted(values);
        var straight = new SKImageInfo(width, height, SKColorType.RgbaF32, SKAlphaType.Unpremul, colour.ColourSpace);
        var shown = colour.Depth == ChannelDepth.U8
            ? new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colour.ColourSpace)
            : new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, colour.ColourSpace);
        var bitmap = new SKBitmap(shown);
        using var image = SKImage.FromPixelCopy(straight, System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()))
                          ?? throw new ImageDecodeException("This Photoshop picture could not be made a picture.");
        if (!image.ReadPixels(shown, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            bitmap.Dispose();
            throw new ImageDecodeException("This Photoshop picture could not be made a picture.");
        }
        return bitmap;
    }

    /// <summary>Colour composited over white — <c>m = c·a + (1 − a)</c> — as the straight colour it was: <c>c = (m − 1 + a) / a</c>.</summary>
    internal static void Unmatted(Span<float> rgba)
    {
        for (int p = 0; p < rgba.Length; p += 4)
        {
            float a = rgba[p + 3];
            if (a >= 1) continue;
            for (int c = 0; c < 3; c++) rgba[p + c] = a <= 0 ? 0 : Math.Max(0, (rgba[p + c] - 1 + a) / a);
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
