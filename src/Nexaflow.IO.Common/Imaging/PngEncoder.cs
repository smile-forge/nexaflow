using System.Buffers.Binary;
using System.IO.Compression;

namespace Nexaflow.IO.Common.Imaging;

/// <summary>
/// Writes 8-bit RGB pixels as a PNG, with no imaging library: for code that makes pictures but has no UI stack, such
/// as the AI host. Each row is filtered the way that is likeliest to compress best — the filter whose output has the
/// smallest sum of absolute values, as libpng chooses — then the rows are deflated into one IDAT chunk.
/// </summary>
public static class PngEncoder
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private const int Channels = 3;

    /// <summary>The picture <paramref name="rgb"/> holds, row by row, three bytes a pixel.</summary>
    public static byte[] EncodeRgb(ReadOnlySpan<byte> rgb, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int stride = width * Channels;
        if (rgb.Length != stride * height)
            throw new ArgumentException($"{width}x{height} RGB takes {stride * height} bytes, not {rgb.Length}.", nameof(rgb));

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;   // bits per channel
        header[9] = 2;   // truecolour
        header[10] = 0;  // deflate
        header[11] = 0;  // adaptive filtering
        header[12] = 0;  // no interlace
        WriteChunk(output, "IHDR"u8, header);

        using (var deflated = new MemoryStream())
        {
            using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            {
                var row = new byte[stride + 1];
                var trial = new byte[stride];
                for (int y = 0; y < height; y++)
                {
                    var current = rgb.Slice(y * stride, stride);
                    var above = y == 0 ? ReadOnlySpan<byte>.Empty : rgb.Slice((y - 1) * stride, stride);
                    long best = long.MaxValue;
                    for (byte filter = 0; filter <= 4; filter++)
                    {
                        long cost = Filter(filter, current, above, trial);
                        if (cost >= best) continue;
                        best = cost;
                        row[0] = filter;
                        trial.CopyTo(row.AsSpan(1));
                    }
                    zlib.Write(row);
                }
            }
            WriteChunk(output, "IDAT"u8, deflated.GetBuffer().AsSpan(0, (int)deflated.Length));
        }

        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    /// <summary>Applies <paramref name="filter"/> to a row into <paramref name="into"/>; returns how well it is likely
    /// to compress, the sum of the filtered bytes read as signed.</summary>
    private static long Filter(byte filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, Span<byte> into)
    {
        long cost = 0;
        for (int i = 0; i < row.Length; i++)
        {
            int left = i >= Channels ? row[i - Channels] : 0;
            int up = above.IsEmpty ? 0 : above[i];
            int upLeft = i >= Channels && !above.IsEmpty ? above[i - Channels] : 0;
            int predicted = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) >> 1,
                _ => Paeth(left, up, upLeft),
            };
            byte value = (byte)(row[i] - predicted);
            into[i] = value;
            cost += Math.Abs((int)(sbyte)value);   // as int: -128 has no sbyte opposite
        }
        return cost;
    }

    /// <summary>The neighbour closest to left + up - upLeft (PNG specification, 9.4).</summary>
    internal static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int toLeft = Math.Abs(estimate - left), toUp = Math.Abs(estimate - up), toUpLeft = Math.Abs(estimate - upLeft);
        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu;
        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, crc);
        output.Write(check);
    }

    /// <summary>The CRC-32 PNG chunks carry (ISO 3309, reflected, polynomial 0xEDB88320).</summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = Build();

        private static uint[] Build()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
