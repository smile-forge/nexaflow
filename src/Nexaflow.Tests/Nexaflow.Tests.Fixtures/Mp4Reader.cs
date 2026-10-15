using System.Buffers.Binary;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Reads what an MP4 says of its video from ISO/IEC 14496-12's boxes alone — the file type, then each track's sample
/// entry, sample count and duration — so tests can check a clip something wrote without a media stack.
/// </summary>
public static class Mp4Reader
{
    /// <param name="Brand">The file type box's major brand.</param>
    /// <param name="Codec">The video track's sample entry: <c>avc1</c> for H.264.</param>
    /// <param name="Frames">The video track's samples.</param>
    public sealed record Clip(string Brand, int Tracks, string Codec, int Width, int Height, int Frames, double Seconds);

    /// <exception cref="InvalidDataException">It does not open with a file type, or has no one video track.</exception>
    public static Clip Read(byte[] mp4)
    {
        var boxes = Boxes(mp4);
        if (boxes.Count == 0 || boxes[0].Type != "ftyp") throw new InvalidDataException("an MP4 opens with its file type");
        string brand = Encoding.ASCII.GetString(boxes[0].Body, 0, 4);

        var tracks = Boxes(boxes.Single(b => b.Type == "moov").Body).Where(b => b.Type == "trak").ToList();
        var video = tracks.Single(t => Encoding.ASCII.GetString(Find(t.Body, "mdia", "hdlr"), 8, 4) == "vide");

        var stbl = Find(video.Body, "mdia", "minf", "stbl");
        var entry = Boxes(Find(stbl, "stsd")[8..]).Single();
        int width = BinaryPrimitives.ReadUInt16BigEndian(entry.Body.AsSpan(24)), height = BinaryPrimitives.ReadUInt16BigEndian(entry.Body.AsSpan(26));
        int frames = (int)BinaryPrimitives.ReadUInt32BigEndian(Find(stbl, "stsz").AsSpan(8));

        var mdhd = Find(video.Body, "mdia", "mdhd");
        bool wide = mdhd[0] == 1;   // version 1 keeps 64-bit times
        uint timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhd.AsSpan(wide ? 20 : 12));
        ulong duration = wide ? BinaryPrimitives.ReadUInt64BigEndian(mdhd.AsSpan(24)) : BinaryPrimitives.ReadUInt32BigEndian(mdhd.AsSpan(16));
        return new Clip(brand, tracks.Count, entry.Type, width, height, frames, (double)duration / timescale);
    }

    private sealed record Box(string Type, byte[] Body);

    private static List<Box> Boxes(byte[] data)
    {
        var boxes = new List<Box>();
        for (int at = 0; at + 8 <= data.Length;)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
            string type = Encoding.ASCII.GetString(data, at + 4, 4);
            int header = 8;
            if (size == 1) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(at + 8)); header = 16; }
            else if (size == 0) size = data.Length - at;
            boxes.Add(new Box(type, data[(at + header)..(int)(at + size)]));
            at += (int)size;
        }
        return boxes;
    }

    /// <summary>The body of the box at the end of <paramref name="path"/>, each step a child of the last.</summary>
    private static byte[] Find(byte[] body, params string[] path)
    {
        foreach (var type in path) body = Boxes(body).Single(b => b.Type == type).Body;
        return body;
    }
}
