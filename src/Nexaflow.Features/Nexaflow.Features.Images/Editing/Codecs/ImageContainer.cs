using System.Buffers.Binary;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>The file format a picture is stored in, known from its first bytes rather than its name.</summary>
public enum ImageContainer
{
    Unknown,
    Jpeg,
    Png,
    Webp,
    Gif,
    Bmp,
    Ico,
    Tiff,
}

public static class ImageContainers
{
    /// <summary>The container <paramref name="header"/> (the file's first bytes; 12 are enough) belongs to.</summary>
    public static ImageContainer Sniff(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ImageContainer.Jpeg;
        if (header.Length >= 8 && header[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return ImageContainer.Png;
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return ImageContainer.Webp;
        if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return ImageContainer.Gif;
        if (header.Length >= 2 && header[0] == 'B' && header[1] == 'M') return ImageContainer.Bmp;
        if (header.Length >= 4 && header[0] == 0 && header[1] == 0 && header[2] == 1 && header[3] == 0) return ImageContainer.Ico;
        if (header.Length >= 4 && ((header[0] == 'I' && header[1] == 'I' && header[2] == 42 && header[3] == 0)
                                || (header[0] == 'M' && header[1] == 'M' && header[2] == 0 && header[3] == 42))) return ImageContainer.Tiff;
        return ImageContainer.Unknown;
    }

    /// <summary>True for a WebP whose picture is stored losslessly (a <c>VP8L</c> bitstream), which a save keeps lossless.</summary>
    public static bool IsLosslessWebp(ReadOnlySpan<byte> file)
    {
        if (Sniff(file[..Math.Min(file.Length, 16)]) != ImageContainer.Webp) return false;
        int at = 12;
        while (at + 8 <= file.Length)
        {
            var fourCc = file.Slice(at, 4);
            if (fourCc.SequenceEqual("VP8L"u8)) return true;
            if (fourCc.SequenceEqual("VP8 "u8)) return false;
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 4)..]);
            if (length > int.MaxValue - at) return false;
            at += 8 + (int)length + (int)(length & 1);
        }
        return false;
    }

    /// <summary>The usual file extension, with its dot.</summary>
    public static string Extension(this ImageContainer container) => container switch
    {
        ImageContainer.Jpeg => ".jpg",
        ImageContainer.Png  => ".png",
        ImageContainer.Webp => ".webp",
        ImageContainer.Gif  => ".gif",
        ImageContainer.Bmp  => ".bmp",
        ImageContainer.Ico  => ".ico",
        ImageContainer.Tiff => ".tif",
        _                   => string.Empty,
    };

    /// <summary>The container a file name asks for, from its extension.</summary>
    public static ImageContainer FromExtension(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg"  => ImageContainer.Jpeg,
        ".png"             => ImageContainer.Png,
        ".webp"            => ImageContainer.Webp,
        ".gif"             => ImageContainer.Gif,
        ".bmp"             => ImageContainer.Bmp,
        ".ico"             => ImageContainer.Ico,
        ".tif" or ".tiff"  => ImageContainer.Tiff,
        _                  => ImageContainer.Unknown,
    };
}
