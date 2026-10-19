namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// A 2×2 JPEG 2000 codestream of three 12-bit components, losslessly coded — the depth of medical and archival scans, which
/// a PDF image's own data cannot be packed at. Row by row, at 12 bits and at the 8 they draw at:
/// <code>
/// red   (4095,    0,    0)  (255,   0,   0)    blue (   0,    0, 4095)  ( 0,  0, 255)
/// green (   0, 1600,    0)  (  0, 100,   0)    grey (1024, 1024, 1024)  (64, 64,  64)
/// </code>
/// Coded by OpenJPEG 2.5.4's <c>opj_compress -F 2,2,3,12,u -n 1</c> from those samples as raw planes, and decoded by its
/// <c>opj_decompress</c> back to them.
/// </summary>
public static class Jpeg2000TwelveBitSample
{
    public const int Width = 2;

    public const int Height = 2;

    private const string CodestreamHex =
        "FF4FFF51002F0000000000020000000200000000000000000000000200000002000000000000000000030B01010B01010B0101FF52000C" +
        "00000001010004040001FF5C00044060FF640025000143726561746564206279204F70656E4A5045472076657273696F6E20322E352E34" +
        "FF90000A0000000000260001FF93CFE414088FED3C07DFE0140D6CB460EFDFE01404C336B2BFFFD9";

    /// <summary>The colour at 8 bits, red, green and blue a pixel, row by row.</summary>
    public static byte[] Colour => [255, 0, 0, 0, 0, 255, 0, 100, 0, 64, 64, 64];

    /// <summary>The bare codestream.</summary>
    public static byte[] Codestream() => Convert.FromHexString(CodestreamHex);
}
