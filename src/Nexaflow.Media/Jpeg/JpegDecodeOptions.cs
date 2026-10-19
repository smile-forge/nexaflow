namespace Nexaflow.Media.Jpeg;

/// <summary>How <see cref="JpegDecoder"/> decodes.</summary>
public sealed record JpegDecodeOptions
{
    /// <summary>Every option at its default.</summary>
    public static JpegDecodeOptions Default { get; } = new();

    /// <summary>
    /// The colour space the samples are coded in, when the caller knows better than the markers — as libjpeg lets an
    /// application set <c>jpeg_color_space</c> once the header is read. A PDF image's <c>/ColorTransform</c> is
    /// applied this way. Null keeps the markers' reading, <see cref="JpegHeader.SourceColourSpace"/>; the output colour
    /// space follows from the component count either way, and a pairing libjpeg cannot convert is refused.
    /// </summary>
    public JpegColourSpace? SourceColourSpace { get; init; }

    /// <summary>Whether the decode spreads its work over the processors.</summary>
    public bool Parallel { get; init; } = true;
}
