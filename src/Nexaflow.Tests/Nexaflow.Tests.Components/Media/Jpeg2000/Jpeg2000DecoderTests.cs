using Nexaflow.Media.Jpeg2000;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// The JPEG 2000 decoder against images libopenjp2 encoded (<see cref="Jpeg2000Fixture"/>): a lossless one must come
/// back as exactly the samples it was made from, whatever parts of the standard its codestream uses; a lossy one as
/// exactly what libopenjp2 decodes it to. Every image in the PDF corpus is held to libopenjp2 by
/// <c>JpxComparisonSweep</c> as well; these are the cases named, small enough to run on every build.
/// </summary>
[TestClass]
public class Jpeg2000DecoderTests
{
    private static readonly Jpeg2000DecodeOptions Serial = new() { Parallel = false };

    public static IEnumerable<object[]> Lossless => Jpeg2000Fixture.Names(fixture => fixture.Lossless);

    public static IEnumerable<object[]> HeldToLibopenjp2 => Jpeg2000Fixture.Names(fixture => fixture.HeldToLibopenjp2);

    public static IEnumerable<object[]> Every => Jpeg2000Fixture.Names(_ => true);

    [TestMethod]
    [DynamicData(nameof(Lossless))]
    [CoversNode("media-jpeg2000-codestream")]
    [CoversNode("media-jpeg2000-code-blocks")]
    [CoversNode("media-jpeg2000-transforms")]
    public void ALosslessImage_DecodesToTheSamplesItWasMadeFrom(string name)
    {
        var fixture = Jpeg2000Fixture.Named(name);

        var image = Jpeg2000Decoder.Decode(fixture.Bytes(), Serial);

        Assert.HasCount(fixture.Components, image.Components);
        for (var c = 0; c < fixture.Components; c++)
        {
            var component = image.Components[c];
            Assert.AreEqual((fixture.Width, fixture.Height, fixture.Precision, fixture.Signed),
                (component.Width, component.Height, component.Precision, component.Signed), $"component {c}'s shape");

            var expected = fixture.Plane(c);
            var actual = component.Samples.Span;
            for (var i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                    Assert.Fail($"component {c} differs first at ({i % fixture.Width}, {i / fixture.Width}): {actual[i]}, not {expected[i]}");
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(HeldToLibopenjp2))]
    [CoversNode("media-jpeg2000-transforms")]
    [CoversNode("media-jpeg2000-output")]
    public void AnImage_DecodesToWhatLibopenjp2DecodesItTo(string name)
    {
        var fixture = Jpeg2000Fixture.Named(name);

        // opj_decompress converts sYCC to RGB before it writes the samples out, so the decode is held to it after
        // the same conversion; the other images are in no colour space it converts.
        var image = Jpeg2000Decoder.Decode(fixture.Bytes(), Serial);
        image.ConvertYccToRgb();

        Assert.AreEqual(fixture.ExpectedHash(), Jpeg2000Fixture.HashOf(Planes(image)));
    }

    [TestMethod]
    [DynamicData(nameof(Every))]
    [CoversNode("media-jpeg2000-threading")]
    public void AParallelDecode_GivesTheSerialDecodesSamples(string name)
    {
        var bytes = Jpeg2000Fixture.Named(name).Bytes();

        var serial = Jpeg2000Decoder.Decode(bytes, Serial);
        var parallel = Jpeg2000Decoder.Decode(bytes, new Jpeg2000DecodeOptions { Parallel = true });

        Assert.AreEqual(Jpeg2000Fixture.HashOf(Planes(serial)), Jpeg2000Fixture.HashOf(Planes(parallel)));
    }

    [TestMethod]
    [CoversNode("media-jpeg2000-codestream")]
    public void AJp2File_SaysWhatColourSpaceItsComponentsAreIn()
    {
        Assert.AreEqual(Jpeg2000ColourSpace.Srgb, ColourSpaceOf("rgb8-jp2"));
        Assert.AreEqual(Jpeg2000ColourSpace.Grey, ColourSpaceOf("grey8-jp2"));
        Assert.AreEqual(Jpeg2000ColourSpace.Sycc, ColourSpaceOf("ycc8-jp2"), "three components without the colour transform are written as sYCC");
    }

    [TestMethod]
    [CoversNode("media-jpeg2000-codestream")]
    public void TheSameImage_DecodesAlikeFromAJp2FileAndABareCodestream()
    {
        var file = Jpeg2000Decoder.Decode(Jpeg2000Fixture.Named("rgb8-jp2").Bytes(), Serial);
        var codestream = Jpeg2000Decoder.Decode(Jpeg2000Fixture.Named("rgb8").Bytes(), Serial);

        Assert.AreEqual(Jpeg2000Fixture.HashOf(Planes(codestream)), Jpeg2000Fixture.HashOf(Planes(file)));
    }

    [TestMethod]
    [CoversNode("media-jpeg2000-codestream")]
    public void DataThatIsNotJpeg2000_IsRefused()
    {
        Assert.Throws<Jpeg2000Exception>(() => Jpeg2000Decoder.Decode(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<Jpeg2000Exception>(() => Jpeg2000Decoder.Decode("%PDF-1.7\n"u8.ToArray()));

        // A JP2 signature with nothing after it, and a codestream's first marker with nothing after it.
        Assert.Throws<Jpeg2000Exception>(() => Jpeg2000Decoder.Decode(Jpeg2000Fixture.Named("rgb8-jp2").Bytes()[..12]));
        Assert.Throws<Jpeg2000Exception>(() => Jpeg2000Decoder.Decode(Jpeg2000Fixture.Named("rgb8").Bytes()[..4]));
    }

    [TestMethod]
    [CoversNode("media-jpeg2000-codestream")]
    public void ACodestreamCutShort_IsRefusedRatherThanPadded()
    {
        var whole = Jpeg2000Fixture.Named("grey8-64x64-blocks").Bytes();

        // Strict, as libopenjp2 is by default: the tile's data ends before its packets do.
        Assert.Throws<Jpeg2000Exception>(() => Jpeg2000Decoder.Decode(whole[..(whole.Length / 2)], Serial));
    }

    private static Jpeg2000ColourSpace ColourSpaceOf(string name) =>
        Jpeg2000Decoder.Decode(Jpeg2000Fixture.Named(name).Bytes(), Serial).ColourSpace;

    private static IEnumerable<(int, int, ReadOnlyMemory<int>)> Planes(Jpeg2000Image image) =>
        image.Components.Select(component => (component.Width, component.Height, component.Samples));
}
