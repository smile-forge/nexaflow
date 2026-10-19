using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// A 2×2 JPEG 2000 image with a fourth component of opacity, losslessly coded, for the readers of the opacity a JPEG 2000
/// image carries: as a bare codestream, which says nothing of what its components hold, or in a JP2 file whose channel
/// definition box says the fourth is opacity. Row by row:
/// <code>
/// red   (255,   0,   0) opacity 255    blue (  0,  0, 255) opacity   0
/// green (  0, 100,   0) opacity 128    grey ( 64, 64,  64) opacity 255
/// </code>
/// Coded by OpenJPEG 2.5.4's <c>opj_compress -F 2,2,4,8,u -n 1</c> from those samples as raw planes, and decoded by its
/// <c>opj_decompress</c> back to them.
/// </summary>
public static class Jpeg2000OpacitySample
{
    public const int Width = 2;

    public const int Height = 2;

    private const string CodestreamHex =
        "FF4FFF510032000000000002000000020000000000000000000000020000000200000000000000000004070101070101070101070101" +
        "FF52000C00000001010004040001FF5C00044040FF640025000143726561746564206279204F70656E4A5045472076657273696F6E20" +
        "322E352E34FF90000A00000000002B0001FF93CFB410088FED28DF80200D6CB45FDF802004C336BFDF80280E180C8A8BFFD9";

    /// <summary>The colour, red, green and blue a pixel, row by row.</summary>
    public static byte[] Colour => [255, 0, 0, 0, 0, 255, 0, 100, 0, 64, 64, 64];

    /// <summary>The opacity, a byte a pixel, row by row.</summary>
    public static byte[] Opacity => [255, 0, 128, 255];

    /// <summary>The bare codestream.</summary>
    public static byte[] Codestream() => Convert.FromHexString(CodestreamHex);

    /// <summary>
    /// The codestream in a JP2 file, sRGB, whose channel definition box makes the first three components colour and the
    /// fourth <paramref name="opacityType"/> over the whole image: 1 opacity, 2 premultiplied opacity (ISO/IEC 15444-1, I.5.3.6).
    /// </summary>
    public static byte[] Jp2(ushort opacityType)
    {
        var codestream = Codestream();
        using var file = new MemoryStream();
        file.Write([0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A]);
        Box(file, "ftyp", [.. Ascii("jp2 "), 0, 0, 0, 0, .. Ascii("jp2 ")]);

        using var header = new MemoryStream();
        // Height, width, components, bits a component less one, compression type 7, colour space known, no IPR.
        Box(header, "ihdr", [.. Big32(Height), .. Big32(Width), .. Big16(4), 7, 7, 0, 0]);
        // An enumerated colour space: sRGB.
        Box(header, "colr", [1, 0, 0, .. Big32(16)]);
        // Each channel's index, type and association: the three colours their own, the opacity the whole image's.
        Box(header, "cdef", [.. Big16(4),
            .. Big16(0), .. Big16(0), .. Big16(1),
            .. Big16(1), .. Big16(0), .. Big16(2),
            .. Big16(2), .. Big16(0), .. Big16(3),
            .. Big16(3), .. Big16(opacityType), .. Big16(0)]);
        Box(file, "jp2h", header.ToArray());

        Box(file, "jp2c", codestream);
        return file.ToArray();
    }

    private static void Box(Stream to, string type, byte[] content)
    {
        to.Write(Big32(8 + content.Length));
        to.Write(Ascii(type));
        to.Write(content);
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Big32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Big16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }
}
