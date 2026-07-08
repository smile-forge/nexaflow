using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Features.Font.Decoding;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Font;

/// <summary>
/// Verifies the WOFF → sfnt decode path: decoding the generated WOFF fixture must yield bytes that WPF
/// loads as a valid font with the expected family name and a non-zero glyph count. This is the decode
/// step the Font viewer runs when opening a <c>.woff</c> file.
/// </summary>
[TestClass]
public class WoffDecoderTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Decode_WoffFixture_ProducesLoadableSfnt()
    {
        var woffPath = TestSampleData.Files("font").Single(p => p.EndsWith(".woff"));

        byte[] sfnt;
        using (var fs = File.OpenRead(woffPath))
            sfnt = new WoffDecoder().DecodeToSfnt(fs);

        Assert.IsTrue(sfnt.Length > 0, "decoder produced no bytes.");
        // sfnt version 0x00010000 (TrueType) round-tripped from the WOFF 'flavor'.
        Assert.AreEqual(0x00010000u,
            (uint)(sfnt[0] << 24 | sfnt[1] << 16 | sfnt[2] << 8 | sfnt[3]),
            "decoded sfnt has an unexpected version.");

        // Write to a temp file and load it exactly as the viewer would.
        var temp = Path.Combine(Path.GetTempPath(), $"nexaflow-woff-test-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(temp, sfnt);
            var families = Fonts.GetFontFamilies(new Uri(temp));
            Assert.IsTrue(families.Count > 0, "decoded WOFF produced no font families.");
            Assert.IsTrue(families.First().GetTypefaces().First().TryGetGlyphTypeface(out var glyph),
                "could not resolve a GlyphTypeface from the decoded WOFF.");
            Assert.IsTrue(glyph.GlyphCount > 0, "decoded WOFF reported zero glyphs.");
            Assert.IsTrue(glyph.FamilyNames.Values.Any(v => v.Contains("Nexaflow")),
                "decoded WOFF family name did not round-trip.");
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }
}
