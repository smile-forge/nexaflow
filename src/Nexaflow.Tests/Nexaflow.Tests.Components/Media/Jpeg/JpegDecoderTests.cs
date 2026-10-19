using Nexaflow.Media.Jpeg;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// The JPEG decoder against images libjpeg-turbo encoded (<see cref="JpegFixture"/>): every one, sound or damaged,
/// must come back as exactly the samples libjpeg-turbo decodes it to, with a warning where libjpeg warns; a lossless
/// one as exactly the samples it was made from. Every JPEG in the PDF corpus is held to libjpeg-turbo by
/// <c>DctComparisonSweep</c> as well; these are the cases named, small enough to run on every build.
/// </summary>
[TestClass]
public class JpegDecoderTests
{
    private static readonly JpegDecodeOptions Serial = new() { Parallel = false };

    public static IEnumerable<object[]> Every => JpegFixture.Names(_ => true);

    public static IEnumerable<object[]> Lossless => JpegFixture.Names(fixture => fixture.Lossless && fixture.Damage == JpegDamage.None);

    public static IEnumerable<object[]> Decodable => JpegFixture.Names(fixture => !fixture.Expected().Refused);

    [TestMethod]
    [DynamicData(nameof(Every))]
    [CoversNode("media-jpeg-markers")]
    [CoversNode("media-jpeg-entropy")]
    [CoversNode("media-jpeg-transforms")]
    [CoversNode("media-jpeg-output")]
    public void AnImage_DecodesToWhatLibjpegTurboDecodesItTo(string name)
    {
        var fixture = JpegFixture.Named(name);
        var expected = fixture.Expected();

        if (expected.Refused)
        {
            Assert.ThrowsExactly<JpegException>(() => JpegDecoder.Decode(fixture.Bytes(), Serial));
            return;
        }

        var image = JpegDecoder.Decode(fixture.Bytes(), Serial);

        Assert.AreEqual((fixture.Width, fixture.Height, fixture.Components), (image.Width, image.Height, image.Components), "the image's shape");
        Assert.AreEqual(expected.Hash, JpegFixture.HashOf(image.Width, image.Height, image.Components, image.Samples.Span));
        Assert.AreEqual(expected.Warns, image.Warnings.Count > 0, $"warnings: {string.Join("; ", image.Warnings)}");
    }

    [TestMethod]
    [DynamicData(nameof(Lossless))]
    [CoversNode("media-jpeg-entropy")]
    public void ALosslessImage_DecodesToTheSamplesItWasMadeFrom(string name)
    {
        var fixture = JpegFixture.Named(name);

        var image = JpegDecoder.Decode(fixture.Bytes(), Serial);

        // The point transform drops each sample's low bits before coding, and the decoder shifts the rest back up.
        var dropped = (byte)~((1 << fixture.Encoding.LosslessPt) - 1);
        var source = fixture.Source();
        var samples = image.Samples.Span;
        Assert.AreEqual(source.Length, samples.Length);
        for (var i = 0; i < source.Length; i++)
        {
            if (samples[i] != (source[i] & dropped))
                Assert.Fail($"sample {i} (pixel {i / fixture.Components}) is {samples[i]}, not {source[i] & dropped}");
        }
    }

    [TestMethod]
    [DynamicData(nameof(Decodable))]
    [CoversNode("media-jpeg-threading")]
    public void AParallelDecode_GivesTheSerialDecodesSamples(string name)
    {
        var bytes = JpegFixture.Named(name).Bytes();

        var serial = JpegDecoder.Decode(bytes, Serial);
        var parallel = JpegDecoder.Decode(bytes, new JpegDecodeOptions { Parallel = true });

        Assert.IsTrue(serial.Samples.Span.SequenceEqual(parallel.Samples.Span));
        Assert.AreEqual(serial.Warnings.Count, parallel.Warnings.Count);
    }

    [TestMethod]
    [DataRow("grey", JpegColourSpace.Grey, JpegColourSpace.Grey, true, null)]
    [DataRow("ycc420", JpegColourSpace.YCbCr, JpegColourSpace.Rgb, true, null)]
    [DataRow("rgb", JpegColourSpace.Rgb, JpegColourSpace.Rgb, false, 0)]
    [DataRow("cmyk", JpegColourSpace.Cmyk, JpegColourSpace.Cmyk, false, 0)]
    [DataRow("ycck420", JpegColourSpace.Ycck, JpegColourSpace.Cmyk, false, 2)]
    [CoversNode("media-jpeg-markers")]
    public void TheColourSpace_IsReadFromTheMarkersAsLibjpegReadsThem(string name, JpegColourSpace source, JpegColourSpace output, bool jfif, int? adobe)
    {
        var bytes = JpegFixture.Named(name).Bytes();

        var header = JpegDecoder.Identify(bytes);
        var image = JpegDecoder.Decode(bytes);

        Assert.AreEqual((source, jfif, adobe), (header.SourceColourSpace, header.HasJfifMarker, header.AdobeTransform));
        Assert.AreEqual((source, output), (image.SourceColourSpace, image.ColourSpace));
    }

    [TestMethod]
    [DataRow("ycc420", JpegProcess.Baseline, false)]
    [DataRow("ycc420-progressive", JpegProcess.Progressive, false)]
    [DataRow("ycc420-arithmetic", JpegProcess.ExtendedSequential, true)]
    [DataRow("ycc420-arithmetic-progressive", JpegProcess.Progressive, true)]
    [DataRow("rgb-lossless-4", JpegProcess.Lossless, false)]
    [CoversNode("media-jpeg-markers")]
    public void TheCodingProcess_IsReadFromTheFrameMarker(string name, JpegProcess process, bool arithmetic)
    {
        var header = JpegDecoder.Identify(JpegFixture.Named(name).Bytes());

        Assert.AreEqual((process, arithmetic, 8), (header.Process, header.ArithmeticCoding, header.Precision));
    }

    [TestMethod]
    [CoversNode("media-jpeg-output")]
    public void ASourceColourSpaceGiven_OverridesTheMarkers()
    {
        // A YCbCr image read as RGB comes out as its coded components, untransformed — what a PDF's /ColorTransform 0
        // asks of a JPEG whose markers say otherwise.
        var bytes = JpegFixture.Named("ycc444").Bytes();

        var transformed = JpegDecoder.Decode(bytes);
        var untransformed = JpegDecoder.Decode(bytes, new JpegDecodeOptions { SourceColourSpace = JpegColourSpace.Rgb });

        Assert.AreEqual((JpegColourSpace.Rgb, JpegColourSpace.Rgb), (untransformed.SourceColourSpace, untransformed.ColourSpace));
        Assert.IsFalse(transformed.Samples.Span.SequenceEqual(untransformed.Samples.Span));
    }

    [TestMethod]
    [CoversNode("media-jpeg-markers")]
    public void ATwelveBitImage_IsRefused()
    {
        // The grey fixture's frame header made extended sequential at 12 bits, which libjpeg-turbo's 8-bit API refuses.
        var bytes = JpegFixture.Named("grey").Bytes();
        var frame = bytes.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0]);
        Assert.IsGreaterThanOrEqualTo(0, frame, "the grey fixture has no baseline frame header");
        bytes[frame + 1] = 0xC1;
        bytes[frame + 4] = 12;

        var refusal = Assert.ThrowsExactly<JpegException>(() => JpegDecoder.Decode(bytes));
        StringAssert.Contains(refusal.Message, "12");
    }

    [TestMethod]
    [DataRow(new byte[0])]
    [DataRow(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [DataRow(new byte[] { 0xFF, 0xD8 })]
    [CoversNode("media-jpeg-markers")]
    public void WhatIsNotAJpeg_IsRefused(byte[] bytes)
    {
        Assert.ThrowsExactly<JpegException>(() => JpegDecoder.Decode(bytes));
    }
}
