using Nexaflow.Features.Images.Editing.Codecs;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Nexaflow.Features.Images.Editing.Metadata;

/// <summary>
/// PNG metadata lives in ancillary chunks: EXIF in <c>eXIf</c>, XMP in an <c>iTXt</c> keyed <c>XML:com.adobe.xmp</c>,
/// the colour profile in <c>iCCP</c>, and free text in <c>tEXt</c>, <c>zTXt</c> and <c>iTXt</c>.
/// </summary>
public sealed class PngMetadata : IMetadataReader, IMetadataWriter
{
    private const string XmpKeyword = "XML:com.adobe.xmp";

    /// <summary>ImageMagick's text-encoded copies of EXIF and the like. They would go stale with the pixels, and
    /// <c>eXIf</c> carries the real thing, so they are not kept.</summary>
    private const string RawProfilePrefix = "Raw profile type";

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public bool Reads(ImageContainer container) => container == ImageContainer.Png;
    public bool Writes(ImageContainer container) => container == ImageContainer.Png;

    public MetadataBlocks Read(byte[] file)
    {
        byte[]? exif = null, xmp = null, icc = null;
        var text = new List<byte[]>();

        foreach (var chunk in Chunks(file))
        {
            var data = file.AsSpan(chunk.DataStart, chunk.Length);
            switch (chunk.Type)
            {
                case "eXIf":
                    exif ??= data.StartsWith("Exif\0\0"u8) ? data[6..].ToArray() : data.ToArray();
                    break;
                case "iCCP":
                    icc ??= ReadIcc(data);
                    break;
                case "iTXt" when Keyword(data) == XmpKeyword:
                    xmp ??= ReadInternationalText(data);
                    break;
                case "tEXt" or "zTXt" or "iTXt" when !Keyword(data).StartsWith(RawProfilePrefix, StringComparison.Ordinal):
                    text.Add(file.AsSpan(chunk.DataStart - 4, chunk.Length + 4).ToArray());   // type + data
                    break;
            }
        }
        return new MetadataBlocks { Exif = exif, Xmp = xmp, Icc = icc, PngText = text };
    }

    /// <summary>
    /// The encoded file with <c>eXIf</c>, the XMP <c>iTXt</c> and the text chunks placed before the image data, where
    /// every reader looks. The encoder's own <c>iCCP</c> is kept.
    /// </summary>
    public byte[] Write(byte[] encoded, MetadataBlocks blocks)
    {
        using var output = new MemoryStream(encoded.Length + 64 * 1024);
        output.Write(Signature);
        bool placed = false;

        foreach (var chunk in Chunks(encoded))
        {
            var data = encoded.AsSpan(chunk.DataStart, chunk.Length);
            if (chunk.Type is "eXIf" or "tEXt" or "zTXt" or "iTXt") continue;   // replaced by the blocks

            if (!placed && chunk.Type is "IDAT")
            {
                if (blocks.Exif is { } exif) WriteChunk(output, "eXIf"u8, exif);
                if (blocks.Xmp is { } xmp) WriteChunk(output, "iTXt"u8, InternationalText(XmpKeyword, xmp));
                foreach (var raw in blocks.PngText) WriteChunk(output, raw.AsSpan(0, 4), raw.AsSpan(4));
                placed = true;
            }
            WriteChunk(output, Encoding.ASCII.GetBytes(chunk.Type), data);
        }
        return output.ToArray();
    }

    private readonly record struct Chunk(string Type, int DataStart, int Length);

    private static IEnumerable<Chunk> Chunks(byte[] file)
    {
        if (file.Length < 8 || !file.AsSpan(0, 8).SequenceEqual(Signature)) yield break;
        int at = 8;
        while (at + 12 <= file.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            if (length < 0 || at + 12L + length > file.Length) yield break;
            var type = Encoding.ASCII.GetString(file, at + 4, 4);
            yield return new Chunk(type, at + 8, length);
            at += 12 + length;
            if (type == "IEND") yield break;
        }
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length);
        output.Write(word);
        output.Write(type);
        output.Write(data);

        uint crc = Crc32.Append(Crc32.Append(Crc32.Seed, type), data);
        BinaryPrimitives.WriteUInt32BigEndian(word, crc ^ 0xFFFFFFFF);
        output.Write(word);
    }

    private static string Keyword(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        return end < 0 ? string.Empty : Encoding.Latin1.GetString(data[..end]);
    }

    /// <summary>The text of an iTXt chunk: keyword, compression flag and method, language, translated keyword, text.</summary>
    private static byte[]? ReadInternationalText(ReadOnlySpan<byte> data)
    {
        int at = data.IndexOf((byte)0);
        if (at < 0 || at + 3 > data.Length) return null;
        bool compressed = data[at + 1] == 1;
        var rest = data[(at + 3)..];
        int language = rest.IndexOf((byte)0);
        if (language < 0) return null;
        rest = rest[(language + 1)..];
        int translated = rest.IndexOf((byte)0);
        if (translated < 0) return null;
        var text = rest[(translated + 1)..];
        return compressed ? Inflate(text) : text.ToArray();
    }

    private static byte[] InternationalText(string keyword, byte[] text)
    {
        var header = Encoding.Latin1.GetBytes(keyword);
        var body = new byte[header.Length + 5 + text.Length];
        header.CopyTo(body, 0);
        // keyword NUL, compression flag 0, method 0, empty language NUL, empty translated keyword NUL
        text.CopyTo(body, header.Length + 5);
        return body;
    }

    private static byte[]? ReadIcc(ReadOnlySpan<byte> data)
    {
        int name = data.IndexOf((byte)0);
        return name < 0 || name + 2 > data.Length ? null : Inflate(data[(name + 2)..]);
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}

/// <summary>The CRC-32 PNG checks its chunks with (IEEE 802.3, reflected).</summary>
internal static class Crc32
{
    public const uint Seed = 0xFFFFFFFF;

    private static readonly uint[] Table = Build();

    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] Build()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
