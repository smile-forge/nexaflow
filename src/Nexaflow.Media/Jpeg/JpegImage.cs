namespace Nexaflow.Media.Jpeg;

/// <summary>A decoded JPEG: its pixels, each a sample per output component, row after row.</summary>
public sealed class JpegImage
{
    internal JpegImage(int width, int height, JpegColourSpace colourSpace, JpegColourSpace sourceColourSpace,
        int components, int precision, byte[] samples, IReadOnlyList<string> warnings)
    {
        Width = width;
        Height = height;
        ColourSpace = colourSpace;
        SourceColourSpace = sourceColourSpace;
        Components = components;
        Precision = precision;
        Samples = samples;
        Warnings = warnings;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// The colour space of <see cref="Samples"/>: grey for one component, RGB for three, CMYK for four — as libjpeg
    /// delivers CMYK, Adobe-inverted when the file is — and otherwise <see cref="JpegColourSpace.Unknown"/>, each
    /// component passed through.
    /// </summary>
    public JpegColourSpace ColourSpace { get; }

    /// <summary>The colour space the samples were coded in, converted from to give <see cref="ColourSpace"/>.</summary>
    public JpegColourSpace SourceColourSpace { get; }

    /// <summary>The samples in each pixel.</summary>
    public int Components { get; }

    /// <summary>Bits a sample: 8, or a lossless image's precision, its samples then below 2^Precision.</summary>
    public int Precision { get; }

    /// <summary>The pixels: <see cref="Components"/> samples each, <see cref="Width"/> a row, the rows unpadded.</summary>
    public Memory<byte> Samples { get; }

    /// <summary>
    /// What libjpeg-turbo would have warned of while decoding — damaged data it decoded past, chiefly — worded as it
    /// words it. Empty for a sound file.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
}
