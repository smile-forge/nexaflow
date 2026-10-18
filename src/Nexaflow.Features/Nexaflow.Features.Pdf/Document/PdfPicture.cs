using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Images;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// An image from a document, decoded into a frozen bitmap that any thread can draw.
/// <para>
/// An ordinary image is opaque <see cref="PixelFormats.Gray8"/> or <see cref="PixelFormats.Rgb24"/>, or
/// <see cref="PixelFormats.Bgra32"/> when a mask gives it alpha. An image mask (a stencil) is
/// <see cref="PixelFormats.Indexed1"/> over a palette of transparent and black — one bit a sample, which is all it
/// ever holds — and is drawn in whatever paint the page sets it with.
/// </para>
/// </summary>
internal sealed class PdfPicture
{
    private PdfPicture(BitmapSource bitmap, bool isStencil)
    {
        Bitmap    = bitmap;
        IsStencil = isStencil;
    }

    /// <summary>The decoded image.</summary>
    public BitmapSource Bitmap { get; }

    /// <summary>Whether this is an image mask: its set samples show the paint it is drawn with, the rest nothing.</summary>
    public bool IsStencil { get; }

    /// <summary>
    /// Decodes <paramref name="image"/>, or returns null with why when it cannot be — a colour space or filter this
    /// reading has no way through, or samples that end before the image does.
    /// </summary>
    public static PdfPicture? TryDecode(IPdfImage image, out string? failure)
    {
        failure = null;
        var width  = image.WidthInSamples;
        var height = image.HeightInSamples;
        if (width <= 0 || height <= 0)
        {
            failure = $"{width}x{height} samples";
            return null;
        }

        try
        {
            if (image.IsImageMask)
            {
                if (!image.TryGetBytesAsMemory(out var bits))
                {
                    failure = "image mask samples could not be read";
                    return null;
                }

                return new PdfPicture(Stencil(bits.Span, width, height, Inverted(image.Decode)), isStencil: true);
            }

            var details = image.ColorSpaceDetails;
            if (details is null || details is UnsupportedColorSpaceDetails || details.BaseType == ColorSpace.Pattern)
            {
                failure = $"colour space {details?.Type.ToString() ?? "none"}";
                return null;
            }

            Span<byte> samples;
            int components;
            if (Jpeg(image, details, width, height) is { } jpeg)
            {
                // Decoded from its JPEG, each sample is as the colour space has it, and the space converts it as it
                // converts any other image's: through its profile, or its calibration.
                samples = ColorSpaceDetailsByteConverter.Convert(details, jpeg, 8, width, height, null, image.RenderingIntent);
                components = details.BaseNumberOfColorComponents;
            }
            else
            {
                if (!image.TryGetBytesAsMemory(out var memory))
                {
                    failure = "samples could not be decoded";
                    return null;
                }

                samples = ColorSpaceDetailsByteConverter.Convert(
                    details, memory.Span, image.BitsPerComponent, width, height, image.Decode, image.RenderingIntent);
                components = details.BaseNumberOfColorComponents;
            }
            if (components is not (1 or 3 or 4))
            {
                failure = $"{components} colour components";
                return null;
            }

            if (samples.Length < width * height * components)
            {
                failure = $"{samples.Length} bytes for {width}x{height}x{components}";
                return null;
            }

            var alpha = Alpha(image, width, height, components, samples);

            // CMYK goes through Windows' colour management a whole image at a time, once its colour key has been read
            // from the inks it was keyed in.
            if (components == 4)
            {
                samples = PdfCmyk.Convert(samples, width, height, image.RenderingIntent);
                components = 3;
            }

            // A soft mask may be finer than the image it masks — a word set as one flat colour with its letters in the
            // mask — and both are stretched over the same square: the picture is as fine as the finer of the two.
            byte[]? levels = null;
            if (alpha is { } mask)
            {
                levels = mask.Levels;
                if (mask.Width != width || mask.Height != height)
                {
                    var toWidth  = Math.Max(width, mask.Width);
                    var toHeight = Math.Max(height, mask.Height);
                    samples = Resampled(samples, width, height, components, toWidth, toHeight);
                    levels  = Resampled(mask.Levels, mask.Width, mask.Height, 1, toWidth, toHeight);
                    width   = toWidth;
                    height  = toHeight;
                }
            }

            return new PdfPicture(Colour(samples, width, height, components, levels), isStencil: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// A plain JPEG's samples, before its colour space converts them, decoded by Windows' own codec: an image whose only
    /// filter is <c>DCTDecode</c>, in grey or RGB,
    /// with the default decode. That is most photographs in most documents, and the native codec reads them many times
    /// faster than a managed one; anything else (CMYK, a Decode array, a filter chain) is left to PdfPig's decoding, which
    /// knows the PDF-specific cases. Null when the image is not such a JPEG, or Windows will not read it.
    /// </summary>
    private static byte[]? Jpeg(IPdfImage image, ColorSpaceDetails details, int width, int height)
    {
        var filter = image.ImageDictionary.TryGet(NameToken.Filter, out var named) ? named : null;
        var dct = filter switch
        {
            NameToken name                           => name.Equals(NameToken.DctDecode),
            ArrayToken { Length: 1 } array           => array.Data[0] is NameToken only && only.Equals(NameToken.DctDecode),
            _                                        => false,
        };
        if (!dct || image.BitsPerComponent != 8 || !IsDefault(image.Decode)) return null;

        var components = details.NumberOfColorComponents;
        var plain = details.Type switch
        {
            ColorSpace.DeviceGray or ColorSpace.CalGray => components == 1,
            ColorSpace.DeviceRGB or ColorSpace.CalRGB   => components == 3,
            ColorSpace.ICCBased                         => components is 1 or 3,
            _                                           => false,
        };
        if (!plain) return null;

        try
        {
            using var stream = new System.IO.MemoryStream(image.RawMemory.ToArray(), writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth != width || frame.PixelHeight != height) return null;

            var format = components == 1 ? PixelFormats.Gray8 : PixelFormats.Rgb24;
            var converted = new FormatConvertedBitmap(frame, format, null, 0);
            var stride = width * components;
            var samples = new byte[stride * height];
            converted.CopyPixels(samples, stride, 0);
            return samples;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Whether a <c>Decode</c> array maps samples to themselves: absent, or <c>[0 1 0 1 …]</c>.</summary>
    private static bool IsDefault(IReadOnlyList<double>? decode)
    {
        if (decode is null) return true;
        for (var i = 0; i < decode.Count; i++)
            if (decode[i] != (i % 2 == 0 ? 0 : 1)) return false;
        return true;
    }

    /// <summary>Whether a mask's <c>Decode</c> array turns its samples over, so a set bit means "show".</summary>
    private static bool Inverted(IReadOnlyList<double>? decode) => decode is { Count: >= 2 } && decode[0] > decode[1];

    /// <summary>
    /// One bit a sample, set where the paint shows. A PDF image mask paints where a sample is 0 (or 1, with an
    /// inverted <c>Decode</c>); the palette puts that on entry 1, black, and the rest on entry 0, transparent.
    /// </summary>
    private static BitmapSource Stencil(ReadOnlySpan<byte> bits, int width, int height, bool inverted)
    {
        var stride = (width + 7) / 8;
        var pixels = new byte[stride * height];
        var length = Math.Min(bits.Length, pixels.Length);
        for (var i = 0; i < length; i++)
            pixels[i] = inverted ? bits[i] : (byte)~bits[i];

        var palette = new BitmapPalette([Colors.Transparent, Colors.Black]);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Indexed1, palette, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// How much of each sample shows, at the resolution it was given in: a soft mask's own, which need not be the image's,
    /// or the image's for a colour key. Null where every sample shows.
    /// </summary>
    private static (byte[] Levels, int Width, int Height)? Alpha(IPdfImage image, int width, int height, int components,
        ReadOnlySpan<byte> samples)
    {
        if (image.MaskImage is { } mask)
            return MaskAlpha(mask);

        if (image.ImageDictionary.TryGet(NameToken.Mask, out ArrayToken range) && range is not null &&
            KeyedAlpha(image, range, width, height, components, samples) is { } keyed)
            return (keyed, width, height);

        return null;
    }

    /// <summary>A mask image's levels, resampled to the image it masks by nearest sample.</summary>
    private static (byte[] Levels, int Width, int Height)? MaskAlpha(IPdfImage mask)
    {
        var maskWidth  = mask.WidthInSamples;
        var maskHeight = mask.HeightInSamples;
        if (maskWidth <= 0 || maskHeight <= 0 || !mask.TryGetBytesAsMemory(out var memory)) return null;

        byte[] levels;
        if (mask.IsImageMask)
        {
            // A stencil used as a mask: show where the sample would paint.
            var stride = (maskWidth + 7) / 8;
            var bits = memory.Span;
            var inverted = Inverted(mask.Decode);
            levels = new byte[maskWidth * maskHeight];
            for (var y = 0; y < maskHeight; y++)
            for (var x = 0; x < maskWidth; x++)
            {
                var at = y * stride + x / 8;
                if (at >= bits.Length) break;
                var set = (bits[at] & (0x80 >> (x % 8))) != 0;
                levels[y * maskWidth + x] = set == inverted ? (byte)255 : (byte)0;
            }
        }
        else
        {
            var details = mask.ColorSpaceDetails ?? DeviceGrayColorSpaceDetails.Instance;
            var grey = ColorSpaceDetailsByteConverter.Convert(
                details, memory.Span, mask.BitsPerComponent, maskWidth, maskHeight, mask.Decode, mask.RenderingIntent);
            if (grey.Length < maskWidth * maskHeight) return null;
            levels = grey[..(maskWidth * maskHeight)].ToArray();
        }

        return (levels, maskWidth, maskHeight);
    }

    /// <summary>
    /// Samples of <paramref name="components"/> bytes each, <paramref name="width"/> by <paramref name="height"/>, over a
    /// grid of <paramref name="toWidth"/> by <paramref name="toHeight"/> covering the same square: each cell takes the sample
    /// it falls in, as the image's own grid would place it.
    /// </summary>
    private static byte[] Resampled(ReadOnlySpan<byte> samples, int width, int height, int components, int toWidth, int toHeight)
    {
        var resampled = new byte[toWidth * toHeight * components];
        for (var y = 0; y < toHeight; y++)
        {
            var from = (int)((long)y * height / toHeight) * width;
            for (var x = 0; x < toWidth; x++)
                samples.Slice((from + (int)((long)x * width / toWidth)) * components, components)
                    .CopyTo(resampled.AsSpan((y * toWidth + x) * components, components));
        }

        return resampled;
    }

    /// <summary>
    /// A colour-key mask: samples whose every component falls within the given ranges are transparent. The ranges
    /// are in the image's sample values, so they are taken through the same conversion as the samples.
    /// </summary>
    private static byte[]? KeyedAlpha(
        IPdfImage image, ArrayToken range, int width, int height, int components, ReadOnlySpan<byte> samples)
    {
        var details = image.ColorSpaceDetails!;
        var count = details.NumberOfColorComponents;
        if (range.Length < count * 2) return null;

        var low  = new byte[count];
        var high = new byte[count];
        for (var c = 0; c < count; c++)
        {
            if (range[c * 2] is not NumericToken min || range[c * 2 + 1] is not NumericToken max) return null;
            low[c]  = (byte)Math.Clamp(min.Int, 0, 255);
            high[c] = (byte)Math.Clamp(max.Int, 0, 255);
        }

        var lowConverted  = ColorSpaceDetailsByteConverter.Convert(details, low, 8, 1, 1).ToArray();
        var highConverted = ColorSpaceDetailsByteConverter.Convert(details, high, 8, 1, 1).ToArray();
        if (lowConverted.Length < components || highConverted.Length < components) return null;

        var alpha = new byte[width * height];
        for (var p = 0; p < alpha.Length; p++)
        {
            var keyed = true;
            for (var c = 0; c < components && keyed; c++)
            {
                var v = samples[p * components + c];
                var lo = Math.Min(lowConverted[c], highConverted[c]);
                var hi = Math.Max(lowConverted[c], highConverted[c]);
                keyed = v >= lo && v <= hi;
            }

            alpha[p] = keyed ? (byte)0 : (byte)255;
        }

        return alpha;
    }

    /// <summary>
    /// The samples as a bitmap, in the smallest format that holds them: grey and RGB samples are taken as they are, and only
    /// an image with alpha is spread out to four bytes a pixel.
    /// </summary>
    private static BitmapSource Colour(ReadOnlySpan<byte> samples, int width, int height, int components, byte[]? alpha)
    {
        var count = width * height;
        BitmapSource bitmap;
        if (alpha is null)
        {
            var format = components == 1 ? PixelFormats.Gray8 : PixelFormats.Rgb24;
            bitmap = BitmapSource.Create(width, height, 96, 96, format, null, samples[..(count * components)].ToArray(), width * components);
        }
        else
        {
            var pixels = new byte[count * 4];
            if (components == 1)
            {
                for (int p = 0, at = 0; p < count; p++, at += 4)
                {
                    var grey = samples[p];
                    pixels[at] = grey;
                    pixels[at + 1] = grey;
                    pixels[at + 2] = grey;
                    pixels[at + 3] = alpha[p];
                }
            }
            else
            {
                for (int p = 0, from = 0, at = 0; p < count; p++, from += 3, at += 4)
                {
                    pixels[at] = samples[from + 2];
                    pixels[at + 1] = samples[from + 1];
                    pixels[at + 2] = samples[from];
                    pixels[at + 3] = alpha[p];
                }
            }

            bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        }

        bitmap.Freeze();
        return bitmap;
    }
}
