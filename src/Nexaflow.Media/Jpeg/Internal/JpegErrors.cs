/*
 * Message texts from libjpeg-turbo 3.1.1, src/jerror.h:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1994-1997, Thomas G. Lane.
 * Modified 1997-2009 by Guido Vollbeding.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2014, 2017, 2021-2023, D. R. Commander.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// libjpeg's messages, worded as it words them: the fatal ones become a <see cref="JpegException"/> (its ERREXIT),
/// the others a warning on the image (its WARNMS).
/// </summary>
internal static class JpegErrors
{
    public const string InputEmpty = "Empty input file";
    public const string NoImage = "JPEG datastream contains no image";
    public const string SoiDuplicate = "Invalid JPEG file structure: two SOI markers";
    public const string SofDuplicate = "Invalid JPEG file structure: two SOF markers";
    public const string SofNoSos = "Invalid JPEG file structure: missing SOS marker";
    public const string SosNoSof = "Invalid JPEG file structure: SOS before SOF";
    public const string EmptyImage = "Empty JPEG image (DNL not supported)";
    public const string BadLength = "Bogus marker length";
    public const string BadHuffTable = "Bogus Huffman table definition";
    public const string BadSampling = "Bogus sampling factors";
    public const string BadMcuSize = "Sampling factors too large for interleaved scan";
    public const string BadJColorspace = "Bogus JPEG colorspace";
    public const string ConversionNotImplemented = "Unsupported color conversion request";
    public const string FractionalSampling = "Fractional sampling not implemented yet";
    public const string ArithNotImplemented = "Sorry, arithmetic coding is not implemented";
    public const string EoiExpected = "Didn't expect more than one scan";
    public const string BadDctCoefficient = "DCT coefficient (lossy) or spatial difference (lossless) out of range";
    public const string Incompatible = "Requested features are incompatible";

    public const string JpegEof = "Premature end of JPEG file";
    public const string HitMarker = "Corrupt JPEG data: premature end of data segment";
    public const string HuffBadCode = "Corrupt JPEG data: bad Huffman code";
    public const string ArithBadCode = "Corrupt JPEG data: bad arithmetic code";
    public const string NotSequential = "Invalid SOS parameters for sequential JPEG";

    public static JpegException Fatal(string message) => new(message);

    public static string NoSoi(int c1, int c2) => $"Not a JPEG file: starts with 0x{c1:x2} 0x{c2:x2}";
    public static string BadComponentId(int id) => $"Invalid component ID {id} in SOS";
    public static string DacIndex(int index) => $"Bogus DAC index {index}";
    public static string DacValue(int value) => $"Bogus DAC value 0x{value:x}";
    public static string DhtIndex(int index) => $"Bogus DHT index {index}";
    public static string DqtIndex(int index) => $"Bogus DQT index {index}";
    public static string SofUnsupported(int marker) => $"Unsupported JPEG process: SOF type 0x{marker:x2}";
    public static string UnknownMarker(int marker) => $"Unsupported marker type 0x{marker:x2}";
    public static string ImageTooBig(int limit) => $"Maximum supported image dimension is {limit} pixels";
    public static string BadPrecision(int precision) => $"Unsupported JPEG data precision {precision}";
    public static string ComponentCount(int count, int max) => $"Too many color components: {count}, max {max}";
    public static string NoQuantTable(int table) => $"Quantization table 0x{table:x2} was not defined";
    public static string NoHuffTable(int table) => $"Huffman table 0x{table:x2} was not defined";
    public static string NoArithTable(int table) => $"Arithmetic table 0x{table:x2} was not defined";
    public static string BadRestart(int interval, int mcusPerRow) =>
        $"Invalid restart interval {interval}; must be an integer multiple of the number of MCUs in an MCU row ({mcusPerRow})";
    public static string BadProgression(int ss, int se, int ah, int al) =>
        $"Invalid progressive/lossless parameters Ss={ss} Se={se} Ah={ah} Al={al}";

    public static string AdobeTransform(int transform) => $"Unknown Adobe color transform code {transform}";
    public static string JfifMajor(int major, int minor) => $"Warning: unknown JFIF revision number {major}.{minor:d2}";
    public static string ExtraneousData(int discarded, int marker) =>
        $"Corrupt JPEG data: {discarded} extraneous bytes before marker 0x{marker:x2}";
    public static string MustResync(int marker, int desired) =>
        $"Corrupt JPEG data: found marker 0x{marker:x2} instead of RST{desired}";
    public static string BogusProgression(int component, int coefficient) =>
        $"Inconsistent progression sequence for component {component} coefficient {coefficient}";
}

/// <summary>
/// The warnings a decode met, in order. A damaged file can raise the same one thousands of times — a skipped marker
/// running off the end refills the fake EOI two bytes at a time — so past a hundred only the count grows.
/// </summary>
internal sealed class JpegWarnings
{
    private const int Kept = 100;
    private readonly List<string> _messages = [];

    public int Count { get; private set; }

    public void Add(string message)
    {
        Count++;
        if (_messages.Count < Kept) _messages.Add(message);
    }

    public IReadOnlyList<string> Messages =>
        Count <= _messages.Count ? _messages : [.. _messages, $"and {Count - _messages.Count} more"];
}
