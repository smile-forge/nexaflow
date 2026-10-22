using System;
using System.Linq;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig.Graphics.Core;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Windows' colour engine as the reader's: device CMYK through the press it means, under the rendering intent asked
/// for, a relatively rendered press black compensated to the screen's black, and white left white.
/// </summary>
[TestClass]
[CoversNode("pdf-colour")]
public class PdfColourEngineTests
{
    [TestMethod]
    public void ARelativelyRenderedPressBlack_IsTheScreensBlack()
    {
        // Windows' own relative colorimetric rendering draws SWOP's every-ink black as a dark grey near 30 and its
        // black ink alone near 53; compensated, the first is black and the second the near-black a press's K is.
        RequirePress();
        var rich = PdfCmyk.Colour(1, 1, 1, 1, 255, RenderingIntent.RelativeColorimetric);
        var ink = PdfCmyk.Colour(0, 0, 0, 1, 255, RenderingIntent.RelativeColorimetric);

        Assert.IsTrue(rich.R <= 6 && rich.G <= 6 && rich.B <= 6, $"every ink is black, not {rich}");
        Assert.IsTrue(ink.R <= 42 && ink.G <= 42 && ink.B <= 42, $"black ink is near black, not {ink}");
    }

    [TestMethod]
    public void APressWhite_StaysWhite_UnderEveryIntent()
    {
        RequirePress();
        foreach (var intent in new[] { RenderingIntent.RelativeColorimetric, RenderingIntent.Perceptual, RenderingIntent.Saturation })
        {
            var paper = PdfCmyk.Colour(0, 0, 0, 0, 255, intent);
            Assert.IsTrue(paper.R >= 250 && paper.G >= 250 && paper.B >= 250, $"{intent}: no ink is the paper, not {paper}");
        }
    }

    [TestMethod]
    public void SamplesAndSingleColours_AgreeAfterCompensation()
    {
        // The two paths through the compensated transform: a whole image at a time, and a colour at a time.
        RequirePress();
        byte[] cmyk = [235, 162, 0, 0, 0, 0, 0, 255, 128, 64, 32, 16];
        var rgb = PdfCmyk.Convert(cmyk, 3, 1, RenderingIntent.RelativeColorimetric);

        for (var p = 0; p < 3; p++)
        {
            var single = PdfCmyk.Colour(cmyk[p * 4] / 255.0, cmyk[p * 4 + 1] / 255.0, cmyk[p * 4 + 2] / 255.0,
                cmyk[p * 4 + 3] / 255.0, 255, RenderingIntent.RelativeColorimetric);
            Assert.AreEqual(single.R, rgb[p * 3], 2, $"pixel {p} red");
            Assert.AreEqual(single.G, rgb[p * 3 + 1], 2, $"pixel {p} green");
            Assert.AreEqual(single.B, rgb[p * 3 + 2], 2, $"pixel {p} blue");
        }
    }

    [TestMethod]
    public void ALabProfile_IsReadWithItsOwnRanges()
    {
        var profile = PdfColourEngine.Shared.Profile(PdfLabProfile.Identity);

        Assert.IsNotNull(profile, "Windows reads the CIELAB profile the engine writes");
        CollectionAssert.AreEqual(new[] { 0.0, 100, -128, 127, -128, 127 }, profile.ComponentRanges.ToArray());
        Assert.IsTrue(profile.TryGetTransform(RenderingIntent.RelativeColorimetric, out var transform));

        var (r, g, b) = transform.ToRgb([50, 0, 0]);
        Assert.AreEqual(119 / 255.0, r, 2 / 255.0, "L* 50 is sRGB's 119");
        Assert.AreEqual(r, g, 1 / 255.0);
        Assert.AreEqual(r, b, 1 / 255.0);
    }

    private static void RequirePress()
    {
        if (PdfColourEngine.Shared.Installed("RSWOP.icm") is null) Assert.Inconclusive("Windows has no RSWOP.icm here");
    }
}

