using Nexaflow.Media.Jpeg2000.Internal;

namespace Nexaflow.Media.Jpeg2000;

/// <summary>
/// A decoded JPEG 2000 image: its components, each a plane of samples at its own size and precision, and the
/// colour space they are in.
/// </summary>
public sealed class Jpeg2000Image
{
    private readonly JPXImage _image;

    internal Jpeg2000Image(JPXImage image) => _image = image;

    public int Width => _image.Width;

    public int Height => _image.Height;

    public Jpeg2000ColourSpace ColourSpace => (Jpeg2000ColourSpace)_image.ColorSpace;

    /// <summary>The components are indices into a palette that has not been applied.</summary>
    public bool HasPalette => _image.IsIndexed;

    /// <summary>The widest component's precision, in bits.</summary>
    public int MaxPrecision => _image.MaxBPC;

    /// <summary>The components as they stand now: applying a palette or converting colour replaces them.</summary>
    public IReadOnlyList<Jpeg2000Component> Components => [.. _image.Components.Select(c => new Jpeg2000Component(c))];

    /// <summary>Replaces palette indices with the palette's colours; nothing changes when there is no palette.</summary>
    public void ApplyPalette()
    {
        if (!_image.ApplyIndex()) throw new Jpeg2000Exception("the palette could not be applied");
    }

    /// <summary>Scales every component up to the widest one's precision.</summary>
    public void MakeUniformPrecision() => _image.MakeUniformBPC();

    /// <summary>Converts eYCC components to RGB.</summary>
    /// <exception cref="NotSupportedException">The image is not three eYCC components of one size.</exception>
    public void ConvertEsyccToRgb()
    {
        if (!_image.ESyccToRGB()) throw new Jpeg2000Exception("the eYCC components could not be converted to RGB");
    }

    /// <summary>
    /// The samples interleaved component by component, each at its precision, every row starting on a byte: the
    /// layout a PDF image's data has. Components smaller than the image are scaled to its size.
    /// </summary>
    public Memory<byte> ToPackedSamples()
    {
        var packed = _image.ToMemoryStream();
        return packed.TryGetBuffer(out var written) ? written.AsMemory() : packed.ToArray();
    }
}

/// <summary>One plane of a decoded image.</summary>
public sealed class Jpeg2000Component
{
    private readonly ImageComp _component;

    internal Jpeg2000Component(ImageComp component) => _component = component;

    public int Width => _component.Width;

    public int Height => _component.Height;

    /// <summary>Bits a sample.</summary>
    public int Precision => _component.Prec;

    public bool Signed => _component.Signed;

    public bool IsAlpha => _component.IsAlpha;

    /// <summary>The samples, row by row.</summary>
    public ReadOnlyMemory<int> Samples => _component.Data;
}
