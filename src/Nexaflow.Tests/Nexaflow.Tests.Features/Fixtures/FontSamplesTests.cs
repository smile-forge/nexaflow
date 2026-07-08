using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// Verifies the generated font fixtures are real, loadable fonts — so the font viewer's manual testing
/// (and the UI <see cref="SampleFileViewerTests"/>) always has an openable font. Unit-category: loads
/// via WPF's font APIs, no window required.
/// </summary>
[TestClass]
public class FontSamplesTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void TtfSample_LoadsAsAValidFont()
    {
        var path = TestSampleData.Files("font").Single(p => p.EndsWith(".ttf"));
        Assert.IsTrue(File.Exists(path), $"ttf sample not generated: {path}");

        var families = Fonts.GetFontFamilies(new Uri(path));
        Assert.IsTrue(families.Count > 0, "no font families found in the generated TTF.");

        var family = families.First();
        Assert.IsTrue(family.GetTypefaces().First().TryGetGlyphTypeface(out var glyph),
            "could not resolve a GlyphTypeface from the generated TTF.");
        Assert.IsTrue(glyph.GlyphCount > 0, "generated TTF reported zero glyphs.");
        Assert.IsTrue(glyph.CharacterToGlyphMap.Count > 0, "generated TTF maps no characters.");
        Assert.IsTrue(glyph.FamilyNames.Values.Any(v => v.Contains("Nexaflow")),
            "generated TTF family name did not round-trip.");
    }
}
