using Nexaflow.Media.Jpeg2000;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// What a caller asks of a decoded image: its samples packed as a PDF image's data is laid out, its sYCC converted
/// to RGB, its components brought to one precision.
/// </summary>
[TestClass]
[CoversNode("media-jpeg2000-output")]
public class Jpeg2000ImageTests
{
    private static readonly Jpeg2000DecodeOptions Serial = new() { Parallel = false };

    public static IEnumerable<object[]> Packed =>
    [
        ["grey1"], ["grey2"], ["grey4"], ["grey8-jp2"], ["grey8-one-sample"], ["grey16"], ["signed12"], ["rgb8"], ["rgb8-subsampled"],
    ];

    [TestMethod]
    [DynamicData(nameof(Packed))]
    public void PackedSamples_InterleaveTheComponentsAtTheirPrecision_EveryRowStartingOnAByte(string name)
    {
        var image = Decode(name);

        CollectionAssert.AreEqual(PackedOneBitAtATime(image), image.ToPackedSamples().ToArray());
    }

    [TestMethod]
    public void ASubsampledImage_IsPackedAtTheImagesOwnSize()
    {
        // Every component is sampled at every other point of the grid, so the image spans twice its components
        // less one, and each sample fills the points up to the next.
        var image = Decode("rgb8-subsampled");
        var component = image.Components[0];

        Assert.AreEqual((2 * component.Width - 1, 2 * component.Height - 1), (image.Width, image.Height));
        var packed = image.ToPackedSamples().Span;
        Assert.AreEqual(image.Width * image.Height * 3, packed.Length);
        Assert.AreEqual(packed[0], packed[3], "the second point of the first row repeats the first sample");
        Assert.AreEqual((byte)component.Samples.Span[1], packed[2 * 3], "the third point is the second sample");
    }

    [TestMethod]
    public void Sycc_ConvertsToRgb_AndNoOtherColourSpaceIsTouched()
    {
        var rgb = Decode("rgb8-jp2");
        var before = rgb.Components[0].Samples.ToArray();

        Assert.IsFalse(rgb.ConvertYccToRgb(), "sRGB is not converted");
        CollectionAssert.AreEqual(before, rgb.Components[0].Samples.ToArray());

        var ycc = Decode("ycc8-jp2");
        Assert.IsTrue(ycc.ConvertYccToRgb());
        Assert.AreEqual(Jpeg2000ColourSpace.Srgb, ycc.ColourSpace, "the converted components are RGB");
    }

    [TestMethod]
    public void UniformPrecision_LeavesAnImageOfOnePrecisionAsItIs()
    {
        var image = Decode("grey4");
        var before = image.Components[0].Samples.ToArray();

        image.MakeUniformPrecision();

        Assert.AreEqual(4, image.Components[0].Precision);
        CollectionAssert.AreEqual(before, image.Components[0].Samples.ToArray());
    }

    private static Jpeg2000Image Decode(string name) => Jpeg2000Decoder.Decode(Jpeg2000Fixture.Named(name).Bytes(), Serial);

    /// <summary>
    /// The layout written the slow, obvious way: for each point of the image, each component's sample nearest it —
    /// the one at <c>x * width / image width</c>, and likewise down — as its low bits, most significant first, and
    /// each row padded with zeros to a byte.
    /// </summary>
    private static byte[] PackedOneBitAtATime(Jpeg2000Image image)
    {
        var components = image.Components;
        var rowBytes = (image.Width * components.Sum(component => component.Precision) + 7) / 8;
        var packed = new byte[rowBytes * image.Height];

        for (var y = 0; y < image.Height; y++)
        {
            var bit = (long)y * rowBytes * 8;
            for (var x = 0; x < image.Width; x++)
            {
                foreach (var component in components)
                {
                    var row = (int)((long)y * component.Height / image.Height);
                    var column = (int)((long)x * component.Width / image.Width);
                    var sample = component.Samples.Span[row * component.Width + column];
                    for (var b = component.Precision - 1; b >= 0; b--, bit++)
                    {
                        if (((sample >> b) & 1) != 0)
                            packed[bit >> 3] |= (byte)(0x80 >> (int)(bit & 7));
                    }
                }
            }
        }

        return packed;
    }
}
