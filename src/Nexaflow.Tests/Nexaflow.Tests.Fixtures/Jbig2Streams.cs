namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// A JBIG2 file split as a PDF embeds it (ISO 32000-2, 7.4.7): no file header, the segments of no page in a
/// <c>JBIG2Globals</c> stream, the rest — but the end of page and end of file segments, which a PDF leaves out — in the
/// image's own stream, each segment's header followed by its data. A file organised for random access is laid out
/// sequentially on the way.
/// </summary>
public static class Jbig2Streams
{
    private static readonly byte[] FileHeaderId = [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A];

    public static (byte[] Globals, byte[] Page) Split(byte[] file)
    {
        if (file.Length < 9 || !file.AsSpan(0, 8).SequenceEqual(FileHeaderId)) throw new ArgumentException("not a JBIG2 file", nameof(file));
        var flags = file[8];
        var sequential = (flags & 1) == 1;
        var at = (flags & 2) == 0 ? 13 : 9;

        var headers = new List<(int Start, int Length, int Type, int Page, long DataLength)>();
        while (at + 11 <= file.Length)
        {
            var (length, type, association, dataLength) = Header(file, at);
            headers.Add((at, length, type, association, dataLength));
            at += length;
            if (sequential) at += (int)dataLength;
            if (type == 51) break;
        }

        // Random access: the data parts follow the headers, in the same order.
        var data = new List<int>();
        foreach (var header in headers)
        {
            data.Add(sequential ? header.Start + header.Length : at);
            if (!sequential) at += (int)header.DataLength;
        }

        using var globals = new MemoryStream();
        using var page = new MemoryStream();
        for (var i = 0; i < headers.Count; i++)
        {
            var (start, length, type, association, dataLength) = headers[i];
            if (type is 49 or 51) continue;
            var target = association == 0 ? globals : page;
            target.Write(file, start, length);
            target.Write(file, data[i], (int)dataLength);
        }
        return (globals.ToArray(), page.ToArray());
    }

    /// <summary>A segment header's length, type, page association and data length, 7.2.</summary>
    private static (int Length, int Type, int Page, long DataLength) Header(byte[] file, int at)
    {
        var start = at;
        var number = BigEndian(file, at);
        var flags = file[at + 4];
        at += 5;

        var referred = file[at] >> 5;
        if (referred == 7)
        {
            referred = (int)(BigEndian(file, at) & 0x1FFFFFFF);
            at += 4 + (referred + 8) / 8;
        }
        else
        {
            at++;
        }
        at += referred * (number > 65536 ? 4 : number > 256 ? 2 : 1);

        int page;
        if ((flags & 0x40) != 0)
        {
            page = (int)BigEndian(file, at);
            at += 4;
        }
        else
        {
            page = file[at++];
        }

        var dataLength = BigEndian(file, at);
        at += 4;
        return (at - start, flags & 0x3F, page, dataLength);
    }

    private static uint BigEndian(byte[] bytes, int at) =>
        (uint)(bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3]);
}
