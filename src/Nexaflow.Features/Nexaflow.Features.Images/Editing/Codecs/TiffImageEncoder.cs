using Nexaflow.Features.Images.Editing.Metadata;
using SkiaSharp;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// Writes TIFF itself — RGB or RGBA, 8 or 16 bits a channel, Deflate-compressed with the horizontal predictor — so a
/// TIFF's EXIF, GPS, XMP, Photoshop resources and colour profile go into its own directory, where TIFF keeps them.
/// </summary>
public sealed class TiffImageEncoder : IImageEncoder
{
    private const ushort ImageWidth = 256, ImageLength = 257, BitsPerSample = 258, Compression = 259, Photometric = 262,
                         SamplesPerPixel = 277, RowsPerStrip = 278, PlanarConfiguration = 284, Predictor = 317,
                         ExtraSamples = 338, XmpTag = 700, PhotoshopTag = 34377, IccTag = 34675;

    private const ushort Deflate = 8, Rgb = 2, Chunky = 1, HorizontalDifferencing = 2, UnassociatedAlpha = 2;

    /// <summary>Uncompressed bytes per strip — small enough to stream, large enough that Deflate has context.</summary>
    private const int StripBytes = 256 * 1024;

    public bool Encodes(ImageContainer container) => container == ImageContainer.Tiff;

    public bool CarriesMetadata(ImageContainer container) => true;

    public byte[] Encode(SKImage picture, ImageContainer container, EncodeOptions options, MetadataBlocks metadata)
    {
        bool wide = picture.ColorType is SKColorType.RgbaF16 or SKColorType.RgbaF32 or SKColorType.Rgba16161616;
        bool alpha = !ImageEncoders.IsOpaque(picture);

        // The file's byte order is its EXIF's, so the lifted EXIF values are written back exactly as they were read.
        var lifted = metadata.Exif is { } exif ? ExifBlock.Descriptive(exif) : null;
        var writer = new TiffWriter(lifted?.BigEndian ?? false);
        var directory = lifted?.Directory ?? new TiffDirectory();
        directory.Entries.RemoveAll(e => e.Tag == ExifBlock.Orientation);   // written upright, so the default

        int samples = alpha ? 4 : 3, bytesPerSample = wide ? 2 : 1;
        directory.Set(writer.Longs(ImageWidth, (uint)picture.Width));
        directory.Set(writer.Longs(ImageLength, (uint)picture.Height));
        directory.Set(writer.Shorts(BitsPerSample, [.. Enumerable.Repeat((ushort)(8 * bytesPerSample), samples)]));
        directory.Set(writer.Shorts(Compression, Deflate));
        directory.Set(writer.Shorts(Photometric, Rgb));
        directory.Set(writer.Shorts(SamplesPerPixel, (ushort)samples));
        directory.Set(writer.Shorts(PlanarConfiguration, Chunky));
        directory.Set(writer.Shorts(Predictor, HorizontalDifferencing));
        if (alpha) directory.Set(writer.Shorts(ExtraSamples, UnassociatedAlpha));

        if (metadata.Icc is { } icc) directory.Set(new TiffEntry(IccTag, TiffEntry.Undefined, (uint)icc.Length, icc));
        if (metadata.Xmp is { } xmp) directory.Set(new TiffEntry(XmpTag, TiffEntry.Byte, (uint)xmp.Length, xmp));
        if (metadata.PhotoshopResources is { } photoshop)
            directory.Set(new TiffEntry(PhotoshopTag, TiffEntry.Undefined, (uint)photoshop.Length, photoshop));

        int rowBytes = picture.Width * samples * bytesPerSample;
        int rowsPerStrip = Math.Max(1, StripBytes / rowBytes);
        directory.Set(writer.Longs(RowsPerStrip, (uint)rowsPerStrip));

        using var pixels = ImageEncoders.Pixels(picture, wide ? SKColorType.Rgba16161616 : SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var row = new byte[rowBytes];
        for (int top = 0; top < picture.Height; top += rowsPerStrip)
        {
            using var strip = new MemoryStream();
            using (var deflate = new ZLibStream(strip, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (int y = top; y < Math.Min(picture.Height, top + rowsPerStrip); y++)
                {
                    PackRow(pixels, y, row, samples, wide, writer.BigEndian);
                    Difference(row, samples, wide, writer.BigEndian);
                    deflate.Write(row);
                }
            }
            directory.Strips.Add(strip.ToArray());
        }

        return writer.Write(directory);
    }

    /// <summary>One row as interleaved samples in the file's byte order, dropping alpha when there is none.</summary>
    private static void PackRow(SKBitmap pixels, int y, byte[] row, int samples, bool wide, bool bigEndian)
    {
        var source = pixels.GetPixelSpan().Slice(y * pixels.RowBytes, pixels.Width * 4 * (wide ? 2 : 1));
        if (!wide)
        {
            for (int x = 0, o = 0; x < pixels.Width; x++)
                for (int s = 0; s < samples; s++) row[o++] = source[x * 4 + s];
            return;
        }
        for (int x = 0, o = 0; x < pixels.Width; x++)
        {
            for (int s = 0; s < samples; s++, o += 2)
            {
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source[((x * 4 + s) * 2)..]);
                if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(o), value);
                else BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(o), value);
            }
        }
    }

    /// <summary>
    /// TIFF predictor 2: each sample becomes its difference from the same sample of the pixel to its left, which
    /// Deflate compresses far better on photographs. Right to left, so every difference reads an unchanged neighbour.
    /// </summary>
    private static void Difference(byte[] row, int samples, bool wide, bool bigEndian)
    {
        if (!wide)
        {
            for (int i = row.Length - 1; i >= samples; i--) row[i] = (byte)(row[i] - row[i - samples]);
            return;
        }
        int stride = samples * 2;
        for (int i = row.Length - 2; i >= stride; i -= 2)
        {
            ushort here = Read(row, i), left = Read(row, i - stride);
            Write(row, i, (ushort)(here - left));
        }

        ushort Read(byte[] r, int at) => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(at)) : BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(at));
        void Write(byte[] r, int at, ushort v)
        {
            if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(at), v);
            else BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(at), v);
        }
    }
}
