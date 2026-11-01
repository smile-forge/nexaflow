using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nexaflow.Features.Compressed.Handlers;

/// <summary>
/// The compression method each entry of a zip was written with, read from its central directory — which the framework's
/// zip reader does not say — so a rewrite can keep an entry stored that was stored.
/// </summary>
internal static class ZipCentralDirectory
{
    private const uint EndSignature = 0x06054b50, Zip64LocatorSignature = 0x07064b50, Zip64EndSignature = 0x06064b50, EntrySignature = 0x02014b50;
    private const int EndSize = 22, MaxComment = 0xFFFF;

    /// <summary>Each entry's method by its name as written; null where the directory cannot be read.</summary>
    public static IReadOnlyDictionary<string, ushort>? Methods(Stream zip)
    {
        try
        {
            long length = zip.Length;
            int tail = (int)Math.Min(length, EndSize + MaxComment);
            var buffer = new byte[tail];
            zip.Position = length - tail;
            zip.ReadExactly(buffer);

            int end = -1;
            for (int i = tail - EndSize; i >= 0; i--)
                if (BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i)) == EndSignature) { end = i; break; }
            if (end < 0) return null;

            long count = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(end + 10));
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(end + 16));
            if (offset == 0xFFFFFFFF || count == 0xFFFF)
            {
                // Zip64: the locator just before the end record names the Zip64 end record, which holds the real figures.
                int locator = end - 20;
                if (locator < 0 || BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(locator)) != Zip64LocatorSignature) return null;
                long zip64End = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(locator + 8));
                var record = new byte[56];
                zip.Position = zip64End;
                zip.ReadExactly(record);
                if (BinaryPrimitives.ReadUInt32LittleEndian(record) != Zip64EndSignature) return null;
                count = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(32));
                offset = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(48));
            }

            var methods = new Dictionary<string, ushort>(System.StringComparer.Ordinal);
            zip.Position = offset;
            var header = new byte[46];
            for (long i = 0; i < count; i++)
            {
                zip.ReadExactly(header);
                if (BinaryPrimitives.ReadUInt32LittleEndian(header) != EntrySignature) return null;
                ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
                ushort method = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10));
                int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
                int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
                int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
                var name = new byte[nameLength];
                zip.ReadExactly(name);
                zip.Position += extraLength + commentLength;
                // Bit 11: the name is UTF-8; else the old IBM code page, which for package names is plain ASCII.
                var text = (flags & 0x800) != 0 ? Encoding.UTF8.GetString(name) : Encoding.Latin1.GetString(name);
                methods[text] = method;
            }
            return methods;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>A method as the file system names it.</summary>
    public static string NameOf(ushort method) => method switch
    {
        0 => "Stored",
        8 => "Deflate",
        9 => "Deflate64",
        12 => "BZip2",
        14 => "LZMA",
        93 => "Zstandard",
        _ => $"Method{method}",
    };
}
