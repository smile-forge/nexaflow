using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Reads an 8-bit RGB PNG from its specification alone — signature, chunk checksums, IHDR, then IDAT inflated and each
/// row's filter undone — so tests can check a PNG something wrote without an imaging library.
/// </summary>
public static class PngReader
{
    public sealed record Picture(int Width, int Height, byte[] Rgb, IReadOnlyList<string> Chunks);

    /// <exception cref="InvalidDataException">It is not a well-formed 8-bit RGB PNG.</exception>
    public static Picture Read(byte[] png)
    {
        if (!png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            throw new InvalidDataException("no PNG signature");

        int at = 8, width = 0, height = 0;
        var idat = new MemoryStream();
        var chunks = new List<string>();
        while (at < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length);
            if (Crc(png.AsSpan(at + 4, 4 + length)) != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length)))
                throw new InvalidDataException($"{type} has a wrong checksum");
            chunks.Add(type);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (!data[8..].SequenceEqual(new byte[] { 8, 2, 0, 0, 0 }))
                    throw new InvalidDataException("not 8-bit RGB, deflate, adaptive filtering, without interlace");
            }
            if (type == "IDAT") idat.Write(data);
            at += 12 + length;
        }

        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress)) zlib.CopyTo(inflated);
        var raw = inflated.ToArray();
        int stride = width * 3;
        if (raw.Length != (stride + 1) * height) throw new InvalidDataException($"{raw.Length} bytes of rows for {width}x{height}");

        var rgb = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            byte filter = raw[y * (stride + 1)];
            for (int i = 0; i < stride; i++)
            {
                int left = i >= 3 ? rgb[y * stride + i - 3] : 0;
                int up = y > 0 ? rgb[(y - 1) * stride + i] : 0;
                int upLeft = i >= 3 && y > 0 ? rgb[(y - 1) * stride + i - 3] : 0;
                int p = left + up - upLeft;
                int predicted = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Math.Abs(p - left) <= Math.Abs(p - up) && Math.Abs(p - left) <= Math.Abs(p - upLeft) ? left
                         : Math.Abs(p - up) <= Math.Abs(p - upLeft) ? up : upLeft,
                    _ => throw new InvalidDataException($"row {y} has filter {filter}"),
                };
                rgb[y * stride + i] = (byte)(raw[y * (stride + 1) + 1 + i] + predicted);
            }
        }
        return new Picture(width, height, rgb, chunks);
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
