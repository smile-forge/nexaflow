using System.Buffers.Binary;
using System.Text;

namespace Nexaflow.Features.Images.Details;

/// <summary>
/// The name an ICC colour profile gives itself ("sRGB IEC61966-2.1", "Display P3"): its description tag, written as a
/// version 2 text description or a version 4 multi-localised string — the English one where it has several.
/// </summary>
public static class IccDescription
{
    private const int HeaderLength = 128, TagEntryLength = 12;
    private const uint DescriptionTag = 0x64657363;     // 'desc'
    private const uint TextDescription = 0x64657363;    // 'desc', the version 2 type
    private const uint Localised = 0x6D6C7563;          // 'mluc', the version 4 type
    private const ushort English = 0x656E;              // 'en'

    public static string? Read(byte[]? profile)
    {
        if (profile is null || profile.Length < HeaderLength + 4) return null;
        var data = profile.AsSpan();
        uint count = U32(data[HeaderLength..]);

        for (long i = 0; i < count; i++)
        {
            long at = HeaderLength + 4 + i * TagEntryLength;
            if (at + TagEntryLength > data.Length) return null;
            var entry = data.Slice((int)at, TagEntryLength);
            if (U32(entry) != DescriptionTag) continue;

            uint offset = U32(entry[4..]), size = U32(entry[8..]);
            return (long)offset + size <= data.Length && size >= 12 ? Text(data.Slice((int)offset, (int)size)) : null;
        }
        return null;
    }

    private static string? Text(ReadOnlySpan<byte> tag) => U32(tag) switch
    {
        TextDescription => Ascii(tag),
        Localised       => Unicode(tag),
        _               => null,
    };

    private static string? Ascii(ReadOnlySpan<byte> tag)
    {
        uint length = U32(tag[8..]);
        if (12 + (long)length > tag.Length) return null;
        return Clean(Encoding.ASCII.GetString(tag.Slice(12, (int)length)));
    }

    private static string? Unicode(ReadOnlySpan<byte> tag)
    {
        if (tag.Length < 16) return null;
        uint records = U32(tag[8..]), recordSize = U32(tag[12..]);
        if (records == 0 || recordSize < 12) return null;

        int chosen = -1;
        for (int i = 0; i < records; i++)
        {
            long at = 16 + (long)i * recordSize;
            if (at + 12 > tag.Length) break;
            bool english = BinaryPrimitives.ReadUInt16BigEndian(tag[(int)at..]) == English;
            if (chosen < 0 || english) chosen = (int)at;
            if (english) break;
        }
        if (chosen < 0) return null;

        uint length = U32(tag[(chosen + 4)..]), offset = U32(tag[(chosen + 8)..]);
        if ((long)offset + length > tag.Length) return null;
        return Clean(Encoding.BigEndianUnicode.GetString(tag.Slice((int)offset, (int)length)));
    }

    private static string? Clean(string text)
    {
        int end = text.IndexOf('\0');
        var name = (end >= 0 ? text[..end] : text).Trim();
        return name.Length == 0 ? null : name;
    }

    private static uint U32(ReadOnlySpan<byte> at) => BinaryPrimitives.ReadUInt32BigEndian(at);
}
