using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// A minimal but fully valid DICOM Part-10 fixture for the DICOM viewer: a 4×4, 8-bit MONOCHROME2 image
/// (Secondary Capture) in Explicit VR Little Endian, with PixelSpacing and a default window so the loader,
/// renderer and measurement math have real values to work against. Hand-encoded so this fixtures library
/// stays dependency-free (per CLAUDE.md) — richer fixtures (DICOMDIR, multi-frame, compressed) are built in
/// the feature test project with fo-dicom.
/// </summary>
internal sealed class DicomSamples : ISampleSet
{
    public string SubDirectory => "dicom";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Raw("ct.dcm", BuildImage()),
    ];

    private const string SopClassSecondaryCapture = "1.2.840.10008.5.1.4.1.1.7";
    private const string ExplicitVrLittleEndian = "1.2.840.10008.1.2.1";

    private static byte[] BuildImage()
    {
        const int rows = 4, cols = 4;
        var pixels = new byte[rows * cols];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 255 / (pixels.Length - 1)); // 0→255 gradient

        // ── File Meta Information (group 0002), Explicit VR LE ──
        var meta = new List<byte>();
        Elem(meta, 0x0002, 0x0002, "UI", Uid(SopClassSecondaryCapture));
        Elem(meta, 0x0002, 0x0003, "UI", Uid("1.2.3.4.5.6.1"));
        Elem(meta, 0x0002, 0x0010, "UI", Uid(ExplicitVrLittleEndian));

        // ── Dataset (Explicit VR LE), elements in ascending tag order ──
        var ds = new List<byte>();
        Elem(ds, 0x0008, 0x0016, "UI", Uid(SopClassSecondaryCapture));
        Elem(ds, 0x0008, 0x0018, "UI", Uid("1.2.3.4.5.6.1"));
        Elem(ds, 0x0008, 0x0060, "CS", Text("OT"));
        Elem(ds, 0x0010, 0x0010, "PN", Text("Doe^John"));
        Elem(ds, 0x0010, 0x0020, "LO", Text("TEST001"));
        Elem(ds, 0x0020, 0x000D, "UI", Uid("1.2.3.4.5.1"));
        Elem(ds, 0x0020, 0x000E, "UI", Uid("1.2.3.4.5.2"));
        Elem(ds, 0x0020, 0x0011, "IS", Text("1"));
        Elem(ds, 0x0020, 0x0013, "IS", Text("1"));
        Elem(ds, 0x0028, 0x0002, "US", U16(1));                 // SamplesPerPixel
        Elem(ds, 0x0028, 0x0004, "CS", Text("MONOCHROME2"));
        Elem(ds, 0x0028, 0x0010, "US", U16(rows));              // Rows
        Elem(ds, 0x0028, 0x0011, "US", U16(cols));              // Columns
        Elem(ds, 0x0028, 0x0100, "US", U16(8));                 // BitsAllocated
        Elem(ds, 0x0028, 0x0101, "US", U16(8));                 // BitsStored
        Elem(ds, 0x0028, 0x0102, "US", U16(7));                 // HighBit
        Elem(ds, 0x0028, 0x0103, "US", U16(0));                 // PixelRepresentation
        Elem(ds, 0x0028, 0x0030, "DS", Text("1.0\\1.0"));       // PixelSpacing (mm)
        Elem(ds, 0x0028, 0x1050, "DS", Text("128"));            // WindowCenter
        Elem(ds, 0x0028, 0x1051, "DS", Text("256"));            // WindowWidth
        Elem(ds, 0x7FE0, 0x0010, "OB", pixels);                 // PixelData

        var file = new List<byte>();
        file.AddRange(new byte[128]);                           // preamble
        file.AddRange(Encoding.ASCII.GetBytes("DICM"));
        Elem(file, 0x0002, 0x0000, "UL", U32((uint)meta.Count));// group length = bytes of the rest of group 0002
        file.AddRange(meta);
        file.AddRange(ds);
        return file.ToArray();
    }

    // ── Explicit VR Little Endian element encoding ──
    private static readonly HashSet<string> LongVrs = ["OB", "OW", "OF", "SQ", "UT", "UN"];

    private static void Elem(List<byte> buf, ushort group, ushort element, string vr, byte[] value)
    {
        buf.AddRange(U16(group));
        buf.AddRange(U16(element));
        buf.AddRange(Encoding.ASCII.GetBytes(vr));
        if (LongVrs.Contains(vr))
        {
            buf.AddRange(new byte[2]);                          // reserved
            buf.AddRange(U32((uint)value.Length));
        }
        else
        {
            buf.AddRange(U16((ushort)value.Length));
        }
        buf.AddRange(value);
    }

    private static byte[] U16(int v) => [(byte)(v & 0xFF), (byte)((v >> 8) & 0xFF)];
    private static byte[] U32(uint v) => [(byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF), (byte)((v >> 24) & 0xFF)];

    /// <summary>UID value: ASCII, null-padded to an even length.</summary>
    private static byte[] Uid(string s) => Pad(Encoding.ASCII.GetBytes(s), 0x00);

    /// <summary>Text value (CS/PN/LO/IS/DS): ASCII, space-padded to an even length.</summary>
    private static byte[] Text(string s) => Pad(Encoding.ASCII.GetBytes(s), 0x20);

    private static byte[] Pad(byte[] bytes, byte pad)
    {
        if (bytes.Length % 2 == 0) return bytes;
        var padded = new byte[bytes.Length + 1];
        System.Array.Copy(bytes, padded, bytes.Length);
        padded[^1] = pad;
        return padded;
    }
}
