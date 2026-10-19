using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Media.Jpeg;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Where a PDF reads a JPEG's colour differently from libjpeg, <c>DCTDecode</c> tells the decoder so: the markers
/// first, then the image dictionary's <c>/ColorTransform</c>, then PDF's default. Null is libjpeg's own reading.
/// </summary>
[TestClass]
[CoversNode("pdf-image-filters")]
public class PdfDctFilterTests
{
    private static readonly int[] Numbered = [1, 2, 3];
    private static readonly int[] NamedRgb = ['R', 'G', 'B'];
    private static readonly int[] Four = [1, 2, 3, 4];

    private static JpegHeader Header(int[] ids, bool jfif = false, int? adobe = null, JpegProcess process = JpegProcess.Baseline) =>
        new(8, 8, 8, process, false, ids, jfif, adobe, JpegColourSpace.Unknown);

    private static DictionaryToken Dictionary(int? colorTransform) => new(colorTransform is { } value
        ? new Dictionary<NameToken, IToken> { [NameToken.Create("ColorTransform")] = new NumericToken(value) }
        : []);

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(null)]
    public void AnAdobeMarker_IsReadAsLibjpegReadsIt_WhateverColorTransformSays(int? colorTransform)
    {
        Assert.IsNull(PdfDctFilter.SourceColourSpace(Header(Numbered, adobe: 0), Dictionary(colorTransform)));
        Assert.IsNull(PdfDctFilter.SourceColourSpace(Header(Four, adobe: 2), Dictionary(colorTransform)));
    }

    [TestMethod]
    public void ALosslessImage_HasNoColourTransformToDecide()
    {
        Assert.IsNull(PdfDctFilter.SourceColourSpace(Header(NamedRgb, process: JpegProcess.Lossless), Dictionary(1)));
    }

    [TestMethod]
    public void AJfifMarker_MeansYCbCr_EvenAgainstColorTransformZero()
    {
        // Some producers write a JFIF YCbCr JPEG with /ColorTransform 0, and drawing it untransformed turns it green.
        Assert.AreEqual(JpegColourSpace.YCbCr, PdfDctFilter.SourceColourSpace(Header(NamedRgb, jfif: true), Dictionary(0)));
    }

    [TestMethod]
    public void ThreeComponentsNotNamedRgb_MeanYCbCr_EvenAgainstColorTransformZero()
    {
        Assert.AreEqual(JpegColourSpace.YCbCr, PdfDctFilter.SourceColourSpace(Header(Numbered), Dictionary(0)));
    }

    [TestMethod]
    [DataRow(0, JpegColourSpace.Rgb)]
    [DataRow(1, JpegColourSpace.YCbCr)]
    [DataRow(null, JpegColourSpace.YCbCr)]
    public void ComponentsNamedRgb_FollowColorTransform_TransformedByDefault(int? colorTransform, JpegColourSpace expected)
    {
        Assert.AreEqual(expected, PdfDctFilter.SourceColourSpace(Header(NamedRgb), Dictionary(colorTransform)));
    }

    [TestMethod]
    [DataRow(0, JpegColourSpace.Cmyk)]
    [DataRow(1, JpegColourSpace.Ycck)]
    [DataRow(null, JpegColourSpace.Cmyk)]
    public void FourComponentsWithoutAnAdobeMarker_FollowColorTransform_UntransformedByDefault(int? colorTransform, JpegColourSpace expected)
    {
        Assert.AreEqual(expected, PdfDctFilter.SourceColourSpace(Header(Four), Dictionary(colorTransform)));
    }

    [TestMethod]
    public void OneComponent_IsLeftToLibjpeg()
    {
        Assert.IsNull(PdfDctFilter.SourceColourSpace(Header([1]), Dictionary(1)));
    }
}
