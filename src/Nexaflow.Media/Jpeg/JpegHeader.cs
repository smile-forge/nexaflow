namespace Nexaflow.Media.Jpeg;

/// <summary>What a JPEG's markers say, up to its first scan: <see cref="JpegDecoder.Identify"/>.</summary>
/// <param name="Width">The image's width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="Precision">Bits a sample, as the frame header gives it.</param>
/// <param name="Process">The coding process its SOF marker names.</param>
/// <param name="ArithmeticCoding">Whether the scans are arithmetic coded rather than Huffman coded.</param>
/// <param name="ComponentIds">Each component's identifier, in frame order.</param>
/// <param name="HasJfifMarker">Whether a JFIF APP0 marker came before the first scan.</param>
/// <param name="AdobeTransform">The colour transform code of an Adobe APP14 marker, or null without one.</param>
/// <param name="SourceColourSpace">
/// The colour space the samples are coded in, as libjpeg reads the markers: the JFIF marker means YCbCr; else the
/// Adobe transform decides; else three components numbered 1, 2, 3 or named 'R', 'G', 'B' are YCbCr or RGB.
/// </param>
public sealed record JpegHeader(
    int Width,
    int Height,
    int Precision,
    JpegProcess Process,
    bool ArithmeticCoding,
    IReadOnlyList<int> ComponentIds,
    bool HasJfifMarker,
    int? AdobeTransform,
    JpegColourSpace SourceColourSpace)
{
    /// <summary>The number of components.</summary>
    public int Components => ComponentIds.Count;
}
