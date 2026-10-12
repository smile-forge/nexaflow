using Nexaflow.Features.Images.Editing.Codecs;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Nexaflow.Features.Images.Editing.Metadata;

/// <summary>
/// WebP metadata lives in RIFF chunks — <c>EXIF</c>, <c>XMP </c> and <c>ICCP</c> — which only the extended format
/// allows, announced by flags in a leading <c>VP8X</c> chunk.
/// </summary>
public sealed class WebpMetadata : IMetadataReader, IMetadataWriter
{
    private const byte IccFlag = 0x20, AlphaFlag = 0x10, ExifFlag = 0x08, XmpFlag = 0x04;

    public bool Reads(ImageContainer container) => container == ImageContainer.Webp;
    public bool Writes(ImageContainer container) => container == ImageContainer.Webp;

    public MetadataBlocks Read(byte[] file)
    {
        byte[]? exif = null, xmp = null, icc = null;
        foreach (var chunk in Chunks(file))
        {
            var data = file.AsSpan(chunk.DataStart, chunk.Length);
            switch (chunk.FourCc)
            {
                case "EXIF": exif ??= data.StartsWith("Exif\0\0"u8) ? data[6..].ToArray() : data.ToArray(); break;
                case "XMP ": xmp ??= data.ToArray(); break;
                case "ICCP": icc ??= data.ToArray(); break;
            }
        }
        return new MetadataBlocks { Exif = exif, Xmp = xmp, Icc = icc };
    }

    /// <summary>
    /// The encoded file in the extended format: a <c>VP8X</c> header with the right flags and canvas size, the
    /// encoder's own chunks in their order, then <c>EXIF</c> and <c>XMP </c> at the end where the format puts them.
    /// </summary>
    public byte[] Write(byte[] encoded, MetadataBlocks blocks)
    {
        if (blocks.Exif is null && blocks.Xmp is null) return encoded;

        var chunks = Chunks(encoded).Where(c => c.FourCc is not ("EXIF" or "XMP ")).ToList();
        var header = chunks.FirstOrDefault(c => c.FourCc == "VP8X");

        byte flags = 0;
        int width, height;
        if (header.FourCc == "VP8X")
        {
            flags = encoded[header.DataStart];
            width = 1 + ReadUInt24(encoded, header.DataStart + 4);
            height = 1 + ReadUInt24(encoded, header.DataStart + 7);
        }
        else if (!BitstreamSize(encoded, chunks, out width, out height, out bool alpha))
        {
            return encoded;   // not a bitstream this writer understands; the picture is kept, its metadata is not
        }
        else if (alpha || chunks.Any(c => c.FourCc == "ALPH"))
        {
            flags |= AlphaFlag;
        }

        if (chunks.Any(c => c.FourCc == "ICCP")) flags |= IccFlag;
        if (blocks.Exif is not null) flags |= ExifFlag;
        if (blocks.Xmp is not null) flags |= XmpFlag;

        using var body = new MemoryStream(encoded.Length + 64 * 1024);
        body.Write("WEBP"u8);

        Span<byte> vp8x = stackalloc byte[10];
        vp8x[0] = flags;
        WriteUInt24(vp8x[4..], width - 1);
        WriteUInt24(vp8x[7..], height - 1);
        WriteChunk(body, "VP8X", vp8x);

        foreach (var chunk in chunks.Where(c => c.FourCc != "VP8X"))
            WriteChunk(body, chunk.FourCc, encoded.AsSpan(chunk.DataStart, chunk.Length));
        if (blocks.Exif is { } exif) WriteChunk(body, "EXIF", exif);
        if (blocks.Xmp is { } xmp) WriteChunk(body, "XMP ", xmp);

        using var output = new MemoryStream((int)body.Length + 8);
        output.Write("RIFF"u8);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Length);
        output.Write(size);
        body.Position = 0;
        body.CopyTo(output);
        return output.ToArray();
    }

    private readonly record struct Chunk(string FourCc, int DataStart, int Length);

    private static IEnumerable<Chunk> Chunks(byte[] file)
    {
        if (file.Length < 12 || !file.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !file.AsSpan(8, 4).SequenceEqual("WEBP"u8)) yield break;
        int at = 12;
        while (at + 8 <= file.Length)
        {
            var fourCc = Encoding.ASCII.GetString(file, at, 4);
            long length = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4));
            if (at + 8 + length > file.Length) yield break;
            yield return new Chunk(fourCc, at + 8, (int)length);
            at += 8 + (int)length + (int)(length & 1);
        }
    }

    /// <summary>The canvas size, and whether it has alpha, from a simple file's VP8 or VP8L bitstream header.</summary>
    private static bool BitstreamSize(byte[] file, List<Chunk> chunks, out int width, out int height, out bool alpha)
    {
        width = height = 0;
        alpha = false;
        foreach (var chunk in chunks)
        {
            var data = file.AsSpan(chunk.DataStart, chunk.Length);
            if (chunk.FourCc == "VP8 " && data.Length >= 10 && data[3] == 0x9D && data[4] == 0x01 && data[5] == 0x2A)
            {
                width  = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 0x3FFF;
                height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 0x3FFF;
                return true;
            }
            if (chunk.FourCc == "VP8L" && data.Length >= 5 && data[0] == 0x2F)
            {
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
                width  = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
                alpha  = ((bits >> 28) & 1) != 0;
                return true;
            }
        }
        return false;
    }

    private static void WriteChunk(Stream output, string fourCc, ReadOnlySpan<byte> data)
    {
        output.Write(Encoding.ASCII.GetBytes(fourCc));
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
        output.Write(size);
        output.Write(data);
        if ((data.Length & 1) == 1) output.WriteByte(0);
    }

    private static int ReadUInt24(byte[] data, int at) => data[at] | (data[at + 1] << 8) | (data[at + 2] << 16);

    private static void WriteUInt24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }
}
