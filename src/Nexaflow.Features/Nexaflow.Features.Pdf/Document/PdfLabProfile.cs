using System.Buffers.Binary;
using System.Text;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// A CIELAB profile that leaves CIELAB as it is: a colour converted to it is its colour in the profile connection
/// space, and one converted from it goes on from there. Windows installs none, so it is written here — a header, a
/// description, a copyright, the D50 white point and one sixteen-bit table from CIELAB to CIELAB serving both ways
/// (ICC.1:2001, 6.5.8) — and it is what a colour passes through where its black point is compensated
/// (<see cref="PdfColourTransform"/>).
/// </summary>
internal static class PdfLabProfile
{
    private const int HeaderLength = 128;

    /// <summary>The profile's bytes.</summary>
    public static byte[] Identity { get; } = Write();

    private static byte[] Write()
    {
        (string Tag, byte[] Data)[] data =
        [
            ("desc", Description("CIELAB")),
            ("cprt", Text("No copyright")),
            ("wtpt", WhitePoint()),
            ("A2B0", IdentityTable()),
        ];
        string[] tags = ["desc", "cprt", "wtpt", "A2B0", "B2A0"];

        var places = new Dictionary<string, (int Offset, int Length)>();
        var at = HeaderLength + 4 + 12 * tags.Length;
        foreach (var (tag, bytes) in data)
        {
            places[tag] = (at, bytes.Length);
            at += (bytes.Length + 3) & ~3;
        }
        places["B2A0"] = places["A2B0"];

        var profile = new byte[at];
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)at);
        profile[8] = 2;
        profile[9] = 0x10;
        Encoding.ASCII.GetBytes("spacLab Lab ").CopyTo(profile, 12);
        Encoding.ASCII.GetBytes("acsp").CopyTo(profile, 36);
        D50(profile.AsSpan(68));

        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(HeaderLength), (uint)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            var entry = profile.AsSpan(HeaderLength + 4 + 12 * i);
            Encoding.ASCII.GetBytes(tags[i]).CopyTo(entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)places[tags[i]].Offset);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)places[tags[i]].Length);
        }
        foreach (var (tag, bytes) in data) bytes.CopyTo(profile, places[tag].Offset);
        return profile;
    }

    /// <summary>
    /// <c>lut16Type</c> taking three components to the same three: an identity matrix, tables of two entries from 0 to
    /// 65535, and a grid of two points a side whose corners are themselves.
    /// </summary>
    private static byte[] IdentityTable()
    {
        const int tables = 52, grid = tables + 3 * 2 * 2, output = grid + 8 * 3 * 2;
        var lut = new byte[output + 3 * 2 * 2];
        Encoding.ASCII.GetBytes("mft2").CopyTo(lut, 0);
        lut[8] = 3;
        lut[9] = 3;
        lut[10] = 2;
        for (var i = 0; i < 3; i++) BinaryPrimitives.WriteInt32BigEndian(lut.AsSpan(12 + 16 * i), 0x10000);
        BinaryPrimitives.WriteUInt16BigEndian(lut.AsSpan(48), 2);
        BinaryPrimitives.WriteUInt16BigEndian(lut.AsSpan(50), 2);
        for (var c = 0; c < 3; c++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(lut.AsSpan(tables + c * 4 + 2), ushort.MaxValue);
            BinaryPrimitives.WriteUInt16BigEndian(lut.AsSpan(output + c * 4 + 2), ushort.MaxValue);
        }

        // The grid's first component varies slowest: corner 0b100 is the first component at its top.
        for (var corner = 0; corner < 8; corner++)
        for (var c = 0; c < 3; c++)
            BinaryPrimitives.WriteUInt16BigEndian(lut.AsSpan(grid + (corner * 3 + c) * 2),
                (ushort)((corner >> (2 - c) & 1) * ushort.MaxValue));
        return lut;
    }

    /// <summary><c>textDescriptionType</c>: the name in ASCII, with no Unicode or ScriptCode name.</summary>
    private static byte[] Description(string name)
    {
        var description = new byte[12 + name.Length + 1 + 4 + 4 + 2 + 1 + 67];
        Encoding.ASCII.GetBytes("desc").CopyTo(description, 0);
        BinaryPrimitives.WriteUInt32BigEndian(description.AsSpan(8), (uint)name.Length + 1);
        Encoding.ASCII.GetBytes(name).CopyTo(description, 12);
        return description;
    }

    private static byte[] Text(string text)
    {
        var bytes = new byte[8 + text.Length + 1];
        Encoding.ASCII.GetBytes("text").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(text).CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] WhitePoint()
    {
        var white = new byte[20];
        Encoding.ASCII.GetBytes("XYZ ").CopyTo(white, 0);
        D50(white.AsSpan(8));
        return white;
    }

    /// <summary>The D50 illuminant, the profile connection space's white, as three s15Fixed16 numbers.</summary>
    private static void D50(Span<byte> xyz)
    {
        BinaryPrimitives.WriteInt32BigEndian(xyz, 63190);
        BinaryPrimitives.WriteInt32BigEndian(xyz[4..], 65536);
        BinaryPrimitives.WriteInt32BigEndian(xyz[8..], 54061);
    }
}
