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

    /// <summary>The widest component's precision, in bits.</summary>
    public int MaxPrecision => _image.MaxBPC;

    /// <summary>The components as they stand now: converting colour replaces them.</summary>
    public IReadOnlyList<Jpeg2000Component> Components => [.. _image.Components.Select(c => new Jpeg2000Component(c))];

    /// <summary>Scales every component up to the widest one's precision.</summary>
    public void MakeUniformPrecision() => _image.MakeUniformBPC();

    /// <summary>
    /// Converts sYCC or e-sYCC components to RGB, as opj_decompress does, and says whether it did. Any other colour
    /// space is left as it is, and so is YCC the conversions do not cover: sYCC subsampled other than 4:2:0, 4:2:2 or
    /// not at all, or e-sYCC whose components differ in size.
    /// </summary>
    public bool ConvertYccToRgb()
    {
        var c = _image.Components;
        switch (ColourSpace)
        {
            case Jpeg2000ColourSpace.Sycc when c.Length >= 3 && c[0].DX == 1 && c[0].DY == 1
                                               && c[1].DX == c[2].DX && c[1].DY == c[2].DY
                                               && (c[1].DX, c[1].DY) is (2, 2) or (2, 1) or (1, 1):
                return _image.SyccToRGB();
            case Jpeg2000ColourSpace.Eycc when c.Length >= 3 && c[0].DX == c[1].DX && c[0].DX == c[2].DX
                                               && c[0].DY == c[1].DY && c[0].DY == c[2].DY:
                return _image.ESyccToRGB();
            default:
                return false;
        }
    }

    /// <summary>
    /// The samples interleaved component by component, each at its precision, every row starting on a byte: the
    /// layout a PDF image's data has. Components smaller than the image are scaled to its size.
    /// </summary>
    public Memory<byte> ToPackedSamples() => SamplePacker.Pack(_image);

    /// <summary>
    /// <paramref name="components"/> packed as <see cref="ToPackedSamples()"/> packs them all, in the order given. With
    /// <paramref name="premultipliedBy"/>, each sample is first divided by that opacity where it stands, undoing a
    /// premultiplication: a sample under no opacity at all is 0.
    /// </summary>
    /// <exception cref="ArgumentException">A component is not one of this image's as it now stands.</exception>
    public Memory<byte> ToPackedSamples(IReadOnlyList<Jpeg2000Component> components, Jpeg2000Component? premultipliedBy = null) =>
        SamplePacker.Pack(_image, [.. components.Select(Own)], premultipliedBy is null ? null : Own(premultipliedBy));

    /// <summary>
    /// <paramref name="component"/> as levels, a byte a sample at the image's size, row by row: stretched as
    /// <see cref="ToPackedSamples()"/> stretches a smaller component, and scaled from its precision so its largest value is
    /// 255.
    /// </summary>
    /// <exception cref="ArgumentException">The component is not one of this image's as it now stands.</exception>
    public byte[] ToLevels(Jpeg2000Component component) => SamplePacker.Levels(_image, Own(component));

    /// <summary>The decoder's component behind <paramref name="component"/>, when it is still one of this image's.</summary>
    private ImageComp Own(Jpeg2000Component component) =>
        Array.IndexOf(_image.comps, component.Source) >= 0
            ? component.Source
            : throw new ArgumentException("The component is not one of this image's as it now stands.", nameof(component));
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

    /// <summary>What the component holds: colour, unless the file's channel definitions say otherwise.</summary>
    public Jpeg2000ChannelType ChannelType => (Jpeg2000ChannelType)_component.alpha;

    /// <summary>The decoder's own component, for the image it belongs to.</summary>
    internal ImageComp Source => _component;
}
