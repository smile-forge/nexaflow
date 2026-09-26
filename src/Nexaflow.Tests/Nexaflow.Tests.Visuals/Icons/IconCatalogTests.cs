using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Icons;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Icons;

namespace Nexaflow.Tests.Visuals.Icons;

/// <summary>
/// The catalog behind every icon picker: the bundled Fluent font's name map must load both faces, every name must
/// draw a glyph the font actually has, a saved <see cref="IconRef"/> must round-trip (and a bare string must stay an
/// emoji, as every icon saved before the Fluent sets was), and search must put the obvious answer first.
/// </summary>
[TestClass]
[CoversNode("icons-catalog")]
public class IconCatalogTests
{
    private sealed record Holder(IconRef Icon);

    [TestMethod]
    public void Fluent_LoadsBothFaces()
    {
        Assert.IsTrue(IconCatalog.All(IconSet.FluentRegular).Count > 2000);
        Assert.IsTrue(IconCatalog.All(IconSet.FluentFilled).Count > 2000);
        Assert.IsTrue(IconCatalog.Contains(IconRef.Fluent("home")));
        Assert.IsTrue(IconCatalog.Contains(IconRef.Fluent("home", filled: true)));
    }

    [TestMethod]
    public void Fluent_RegularAndFilledAreDifferentGlyphs()
        => Assert.AreNotEqual(IconCatalog.GlyphFor(IconRef.Fluent("home")), IconCatalog.GlyphFor(IconRef.Fluent("home", filled: true)));

    [TestMethod]
    public void Fluent_EveryGlyphIsInTheFont()
    {
        var typeface = new Typeface(IconCatalog.FluentFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Assert.IsTrue(typeface.TryGetGlyphTypeface(out var glyphs), "the bundled font did not load");

        var missing = IconCatalog.All(IconSet.FluentRegular).Concat(IconCatalog.All(IconSet.FluentFilled))
            .Where(e => !glyphs.CharacterToGlyphMap.ContainsKey(char.ConvertToUtf32(e.Glyph, 0)))
            .Select(e => e.Icon.ToString())
            .ToList();
        Assert.AreEqual(0, missing.Count, string.Join(", ", missing.Take(10)));
    }

    [TestMethod]
    public void UnknownFluentName_DrawsThePlaceholder()
    {
        Assert.IsFalse(IconCatalog.Contains(IconRef.Fluent("no_such_icon_anywhere")));
        Assert.AreEqual(IconCatalog.MissingGlyph, IconCatalog.GlyphFor(IconRef.Fluent("no_such_icon_anywhere")));
    }

    [TestMethod]
    public void Emoji_DrawsItself_InTheSurroundingFont()
    {
        Assert.AreEqual("📁", IconCatalog.GlyphFor(IconRef.Emoji("📁")));
        Assert.IsNull(IconCatalog.FontFor(IconRef.Emoji("📁")));
        Assert.IsNotNull(IconCatalog.FontFor(IconRef.Fluent("home")));
    }

    [TestMethod]
    public void Emoji_TableHasNoDuplicates()
    {
        var glyphs = IconCatalog.All(IconSet.Emoji).Select(e => e.Glyph).ToList();
        Assert.AreEqual(glyphs.Count, glyphs.Distinct().Count());
    }

    [TestMethod]
    [DataRow("📁")]
    [DataRow("fluent:arrow_clockwise")]
    [DataRow("fluent-filled:home")]
    public void IconRef_RoundTripsItsString(string text) => Assert.AreEqual(text, IconRef.Parse(text).ToString());

    [TestMethod]
    public void IconRef_BareStringIsAnEmoji() => Assert.AreEqual(IconSet.Emoji, IconRef.Parse("🧮").Set);

    [TestMethod]
    public void IconRef_Json()
    {
        Assert.AreEqual("""{"Icon":"fluent:home"}""", JsonSerializer.Serialize(new Holder(IconRef.Fluent("home"))));
        Assert.AreEqual(IconRef.Emoji("💾"), JsonSerializer.Deserialize<Holder>("""{"Icon":"💾"}""")!.Icon);
    }

    [TestMethod]
    public void Search_ExactNameComesFirst()
    {
        var first = IconCatalog.Search("home", IconSet.FluentRegular)[0];
        Assert.AreEqual(IconRef.Fluent("home"), first.Icon);
    }

    [TestMethod]
    public void Search_MatchesUnderscoredNamesByWords()
        => Assert.IsTrue(IconCatalog.Search("arrow clock", IconSet.FluentRegular).Any(e => e.Icon == IconRef.Fluent("arrow_clockwise")));

    [TestMethod]
    public void Search_FindsEmojiByKeyword()
        => Assert.AreEqual("🗑", IconCatalog.Search("trash", IconSet.Emoji)[0].Glyph);

    [TestMethod]
    public void Search_EmptyIsEverything()
        => Assert.AreEqual(IconCatalog.All().Count, IconCatalog.Search("  ").Count);

    [TestMethod]
    public void Search_NothingMatchesGibberish() => Assert.AreEqual(0, IconCatalog.Search("qzxqzxqzx").Count);
}
