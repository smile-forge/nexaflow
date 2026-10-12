using Nexaflow.Features.Images.Editing.Codecs;
using System.Buffers.Binary;
using System.IO;

namespace Nexaflow.Features.Images.Editing.Metadata;

/// <summary>
/// JPEG metadata lives in application segments before the image data: EXIF and XMP in APP1, the ICC profile in
/// APP2 (in numbered pieces), IPTC inside the Photoshop resources of APP13.
/// </summary>
public sealed class JpegMetadata : IMetadataReader, IMetadataWriter
{
    private const byte App0 = 0xE0, App1 = 0xE1, App2 = 0xE2, App13 = 0xED, StartOfScan = 0xDA, EndOfImage = 0xD9;
    private const int MaxSegmentPayload = 65533;

    private static ReadOnlySpan<byte> ExifHeader => "Exif\0\0"u8;
    private static ReadOnlySpan<byte> XmpHeader => "http://ns.adobe.com/xap/1.0/\0"u8;
    private static ReadOnlySpan<byte> IccHeader => "ICC_PROFILE\0"u8;
    private static ReadOnlySpan<byte> PhotoshopHeader => "Photoshop 3.0\0"u8;

    public bool Reads(ImageContainer container) => container == ImageContainer.Jpeg;
    public bool Writes(ImageContainer container) => container == ImageContainer.Jpeg;

    public MetadataBlocks Read(byte[] file)
    {
        byte[]? exif = null, xmp = null, photoshop = null;
        var icc = new SortedDictionary<int, byte[]>();

        foreach (var (marker, start, length) in Segments(file))
        {
            var payload = file.AsSpan(start, length);
            if (marker == App1 && exif is null && payload.StartsWith(ExifHeader)) exif = payload[ExifHeader.Length..].ToArray();
            else if (marker == App1 && xmp is null && payload.StartsWith(XmpHeader)) xmp = payload[XmpHeader.Length..].ToArray();
            else if (marker == App2 && payload.Length > IccHeader.Length + 2 && payload.StartsWith(IccHeader))
                icc[payload[IccHeader.Length]] = payload[(IccHeader.Length + 2)..].ToArray();
            else if (marker == App13 && photoshop is null && payload.StartsWith(PhotoshopHeader))
                photoshop = payload[PhotoshopHeader.Length..].ToArray();
        }

        return new MetadataBlocks
        {
            Exif = exif,
            Xmp = xmp,
            Icc = icc.Count == 0 ? null : [.. icc.Values.SelectMany(b => b)],
            PhotoshopResources = photoshop,
        };
    }

    /// <summary>
    /// The encoded file with the blocks placed after its JFIF header (or straight after the start marker): EXIF,
    /// then XMP, then the Photoshop resources. The encoder's own colour profile is kept; a block too large for one
    /// segment is left out rather than split, which no reader would join.
    /// </summary>
    public byte[] Write(byte[] encoded, MetadataBlocks blocks)
    {
        var segments = Segments(encoded).ToList();
        using var output = new MemoryStream(encoded.Length + 64 * 1024);
        output.Write(encoded, 0, 2);   // start of image

        int index = 0;
        if (segments.Count > 0 && segments[0].Marker == App0)
        {
            CopySegment(output, encoded, segments[0]);
            index = 1;
        }

        if (blocks.Exif is { } exif) WriteSegment(output, App1, ExifHeader, exif);
        if (blocks.Xmp is { } xmp) WriteSegment(output, App1, XmpHeader, xmp);
        if (blocks.PhotoshopResources is { } photoshop) WriteSegment(output, App13, PhotoshopHeader, photoshop);

        for (; index < segments.Count; index++)
        {
            var segment = segments[index];
            var payload = encoded.AsSpan(segment.Start, segment.Length);
            bool replaced = (segment.Marker == App1 && (payload.StartsWith(ExifHeader) || payload.StartsWith(XmpHeader)))
                         || (segment.Marker == App13 && payload.StartsWith(PhotoshopHeader));
            if (!replaced) CopySegment(output, encoded, segment);
        }

        int rest = segments.Count == 0 ? 2 : segments[^1].Start + segments[^1].Length;
        output.Write(encoded, rest, encoded.Length - rest);
        return output.ToArray();
    }

    /// <summary>The marker segments from the start of the file up to the image data, as (marker, payload start, payload length).</summary>
    private static IEnumerable<(byte Marker, int Start, int Length)> Segments(byte[] file)
    {
        if (file.Length < 4 || file[0] != 0xFF || file[1] != 0xD8) yield break;
        int at = 2;
        while (at + 4 <= file.Length)
        {
            if (file[at] != 0xFF) yield break;
            byte marker = file[at + 1];
            if (marker == 0xFF) { at++; continue; }   // fill byte
            if (marker is StartOfScan or EndOfImage) yield break;
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) { at += 2; continue; }   // markers without a length

            int length = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at + 2));
            if (length < 2 || at + 2 + length > file.Length) yield break;
            yield return (marker, at + 4, length - 2);
            at += 2 + length;
        }
    }

    private static void CopySegment(Stream output, byte[] file, (byte Marker, int Start, int Length) segment) =>
        output.Write(file, segment.Start - 4, segment.Length + 4);

    private static void WriteSegment(Stream output, byte marker, ReadOnlySpan<byte> header, byte[] body)
    {
        int payload = header.Length + body.Length;
        if (payload > MaxSegmentPayload) return;
        Span<byte> prefix = [0xFF, marker, (byte)((payload + 2) >> 8), (byte)(payload + 2)];
        output.Write(prefix);
        output.Write(header);
        output.Write(body);
    }
}
