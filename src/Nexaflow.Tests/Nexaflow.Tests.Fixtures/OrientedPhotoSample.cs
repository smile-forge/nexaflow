using System.Buffers.Binary;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// The image samples' one photograph with metadata: a JPEG stored on its side with EXIF orientation 6, a camera
/// make, a GPS directory, a thumbnail directory and an XMP packet — what the image editor has to stand upright,
/// carry over, patch and cut loose when it saves. Generated into <c>images/</c> with the rest of the set; its
/// constants say what is in it, so a test asserts against the sample rather than against bytes of its own.
/// </summary>
/// <remarks>
/// The pixels are four flat quadrants, so where each one lands says where that part of the picture went. They are a
/// 64×32 JPEG encoded once (quality 90, no chroma subsampling, so the quadrant edges stay put) and embedded verbatim;
/// the EXIF and XMP segments are built here, byte by byte, after its JFIF header.
/// </remarks>
public static class OrientedPhotoSample
{
    public const string FileName = "photo_25_exif.jpg";

    /// <summary>The stored pixel size — on its side.</summary>
    public const int StoredWidth = 64, StoredHeight = 32;

    /// <summary>EXIF orientation 6: shown turned a quarter clockwise, so upright it is 32×64.</summary>
    public const ushort Orientation = 6;

    public const string Make = "Nexaflow Test Camera";
    /// <summary>A real XMP packet — well-formed, since WIC parses XMP as it opens a file and refuses one that is not.</summary>
    public const string Xmp =
        "<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
      + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
      + "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
      + "<dc:creator><rdf:Seq><rdf:li>Nexaflow</rdf:li></rdf:Seq></dc:creator>"
      + "</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";

    /// <summary>The quadrant colours as stored (before the orientation is applied), as 0xRRGGBB.</summary>
    public const uint TopLeft = 0xDC2828, TopRight = 0x28C828, BottomLeft = 0x2828DC, BottomRight = 0xE6E628;

    public static byte[] Bytes()
    {
        var jpeg = Convert.FromBase64String(QuadrantJpegBase64);
        int afterJfif = 4 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4));   // SOI, then APP0's marker + length + body

        using var file = new MemoryStream();
        file.Write(jpeg, 0, afterJfif);
        Segment(file, [.. "Exif\0\0"u8, .. Exif()]);
        Segment(file, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. Encoding.UTF8.GetBytes(Xmp)]);
        file.Write(jpeg, afterJfif, jpeg.Length - afterJfif);
        return file.ToArray();
    }

    /// <summary>
    /// A little-endian EXIF block: IFD0 with Make, Orientation and pointers to an EXIF directory (PixelX/YDimension)
    /// and a GPS directory (latitude reference "N"), chained to a thumbnail directory IFD1.
    /// </summary>
    private static byte[] Exif()
    {
        var make = Encoding.ASCII.GetBytes(Make + "\0");
        const int ifd0 = 8, exif = ifd0 + 2 + 4 * 12 + 4, gps = exif + 2 + 2 * 12 + 4, ifd1 = gps + 2 + 12 + 4, makeAt = ifd1 + 2 + 12 + 4;
        var block = new byte[makeAt + make.Length];

        "II"u8.CopyTo(block);
        U16(block, 2, 42);
        U32(block, 4, ifd0);

        U16(block, ifd0, 4);                                            // entries in tag order
        Entry(block, ifd0 + 2, 0x010F, 2, (uint)make.Length, makeAt);   // Make (ASCII, out of line)
        Entry(block, ifd0 + 14, 0x0112, 3, 1, Orientation);
        Entry(block, ifd0 + 26, 0x8769, 4, 1, exif);                    // EXIF directory
        Entry(block, ifd0 + 38, 0x8825, 4, 1, gps);                     // GPS directory
        U32(block, ifd0 + 50, ifd1);                                    // next: the thumbnail's directory

        U16(block, exif, 2);
        Entry(block, exif + 2, 0xA002, 4, 1, StoredWidth);
        Entry(block, exif + 14, 0xA003, 4, 1, StoredHeight);

        U16(block, gps, 1);
        Entry(block, gps + 2, 0x0001, 2, 2, 'N');                       // GPSLatitudeRef "N\0", inline

        U16(block, ifd1, 1);
        Entry(block, ifd1 + 2, 0x0103, 3, 1, 6);                        // Compression 6: a JPEG thumbnail

        make.CopyTo(block, makeAt);
        return block;
    }

    private static void Segment(Stream file, byte[] payload)
    {
        file.Write([0xFF, 0xE1, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2)]);
        file.Write(payload);
    }

    private static void Entry(byte[] b, int at, ushort tag, ushort type, uint count, uint value)
    {
        U16(b, at, tag);
        U16(b, at + 2, type);
        U32(b, at + 4, count);
        if (type == 3) U16(b, at + 8, (ushort)value); else U32(b, at + 8, value);
    }

    private static void U16(byte[] b, int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v);
    private static void U32(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);

    private const string QuadrantJpegBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAgAEADAREAAhEBAxEB/8QAFwABAQEBAAAAAAAAAAAAAAAAAAcJCP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/xAAYAQEBAQEBAAAAAAAAAAAAAAAACQcGCP/EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAMAwEAAhEDEQA/AJIy97wAAAWtwSLgAACKO9WjAAAWtwSLgAADjxX16LAAAbEIJNQAAAY7r2svAAAbEIJNQAAAf//Z";
}
