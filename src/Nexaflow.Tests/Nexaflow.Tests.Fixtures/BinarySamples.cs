using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Binary fixtures for the hex viewer: deterministic pseudo-random bytes, an all-zero block, a
/// "mixed" blob that interleaves printable ASCII with high bytes (so the hex view has both a
/// readable text gutter and non-printable runs), and a minimal PNG file header.
///
/// All entries are byte-exact <see cref="SampleFile.Raw"/> samples generated without
/// <c>Random</c>, so regeneration is reproducible.
/// </summary>
internal sealed class BinarySamples : ISampleSet
{
    public string SubDirectory => "binary";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Raw("random.bin",     PseudoRandom(4096)),
        SampleFile.Raw("zeros.bin",      new byte[1024]),
        SampleFile.Raw("mixed.bin",      Mixed()),
        SampleFile.Raw("png_header.bin", PngHeader()),
    ];

    /// <summary>Deterministic bytes from a fixed-seed LCG (no <c>Random</c> → reproducible).</summary>
    private static byte[] PseudoRandom(int count)
    {
        var bytes = new byte[count];
        uint state = 0x12345678u;
        for (int i = 0; i < count; i++)
        {
            state = state * 1664525u + 1013904223u;   // Numerical Recipes LCG
            bytes[i] = (byte)(state >> 24);
        }
        return bytes;
    }

    /// <summary>Alternating runs of printable ASCII and a sweep of all 256 byte values.</summary>
    private static byte[] Mixed()
    {
        var buf = new List<byte>(1024);
        var label = Encoding.ASCII.GetBytes("NEXA-HEX-SAMPLE\0");
        for (int block = 0; block < 4; block++)
        {
            buf.AddRange(label);
            for (int b = 0; b < 256; b++) buf.Add((byte)b);
        }
        return buf.ToArray();
    }

    /// <summary>An 8-byte PNG signature followed by a minimal IHDR chunk (1×1, 8-bit RGBA).</summary>
    private static byte[] PngHeader() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,             // PNG signature
        0x00, 0x00, 0x00, 0x0D,                                     // IHDR length = 13
        0x49, 0x48, 0x44, 0x52,                                     // "IHDR"
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,             // width=1, height=1
        0x08, 0x06, 0x00, 0x00, 0x00,                               // bit depth 8, colour type 6 (RGBA)
        0x1F, 0x15, 0xC4, 0x89,                                     // IHDR CRC
    ];
}
