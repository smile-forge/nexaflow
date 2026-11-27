using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Tests.Visuals.Editor;

/// <summary>
/// Recognising a colour written in source, so the editor can show what it looks like. The case worth pinning
/// down is the 8-digit hex literal: XAML reads <c>#AARRGGBB</c> and CSS reads <c>#RRGGBBAA</c>, so the same
/// eight characters are two different colours and getting it wrong is silent.
/// </summary>
[TestClass]
[CoversNode("vtext-highlighting")]
public class ColorLiteralTests
{
    private static LayoutColor Xaml(string token) =>
        ColorLiterals.Parse(token, alphaFirst: true) ?? throw new AssertFailedException($"'{token}' was not read as a colour");

    private static LayoutColor Css(string token) =>
        ColorLiterals.Parse(token, alphaFirst: false) ?? throw new AssertFailedException($"'{token}' was not read as a colour");

    [TestMethod]
    public void SixDigitHex_ReadsTheSameInEitherDialect()
    {
        Assert.AreEqual(LayoutColor.Of(0xFF, 0x3B, 0x30), Xaml("#FF3B30"));
        Assert.AreEqual(LayoutColor.Of(0xFF, 0x3B, 0x30), Css("#ff3b30"));
    }

    [TestMethod]
    public void EightDigitHex_PutsAlphaWhereTheLanguagePutsIt()
    {
        // #80FF0000 — half-transparent red in XAML; opaque-ish #80FF00 with alpha 00 in CSS.
        Assert.AreEqual(new LayoutColor(0x80, 0xFF, 0x00, 0x00), Xaml("#80FF0000"));
        Assert.AreEqual(new LayoutColor(0x00, 0x80, 0xFF, 0x00), Css("#80FF0000"));
    }

    [TestMethod]
    public void ShorthandHex_IsExpandedByDoublingEachDigit()
    {
        Assert.AreEqual(LayoutColor.Of(0xAA, 0xBB, 0xCC), Xaml("#abc"));
        Assert.AreEqual(new LayoutColor(0xDD, 0xAA, 0xBB, 0xCC), Xaml("#dabc"));
        Assert.AreEqual(new LayoutColor(0xDD, 0xAA, 0xBB, 0xCC), Css("#abcd"));
    }

    [TestMethod]
    public void QuotesAreIgnored_SoAnAttributeValueCanBePassedAsWritten()
    {
        Assert.AreEqual(LayoutColor.Of(0xFF, 0x3B, 0x30), Xaml("\"#FF3B30\""));
    }

    [TestMethod]
    public void NamedColoursAreRecognised_CaseInsensitively()
    {
        Assert.AreEqual(LayoutColors.Tomato, Css("tomato"));
        Assert.AreEqual(LayoutColors.Tomato, Xaml("Tomato"));
        Assert.AreEqual(LayoutColors.Transparent, Xaml("Transparent"));
    }

    [TestMethod]
    public void FunctionalNotation_BothTheCommaAndSpaceForms()
    {
        var red = LayoutColor.Of(255, 59, 48);
        Assert.AreEqual(red, Css("rgb(255, 59, 48)"));
        Assert.AreEqual(red, Css("rgb(255 59 48)"));
        Assert.AreEqual(new LayoutColor(128, 255, 59, 48), Css("rgba(255, 59, 48, 0.5)"));
        Assert.AreEqual(new LayoutColor(128, 255, 59, 48), Css("rgb(255 59 48 / 0.5)"), "the modern slash-alpha form");
    }

    [TestMethod]
    public void PercentageChannelsAreScaled()
    {
        Assert.AreEqual(LayoutColor.Of(255, 0, 0), Css("rgb(100%, 0%, 0%)"));
    }

    [TestMethod]
    public void HslIsConverted()
    {
        Assert.AreEqual(LayoutColor.Of(255, 0, 0), Css("hsl(0, 100%, 50%)"));
        Assert.AreEqual(LayoutColor.Of(0, 255, 0), Css("hsl(120 100% 50%)"));
        Assert.AreEqual(LayoutColor.Of(128, 128, 128), Css("hsl(0, 0%, 50%)"));
    }

    [TestMethod]
    public void ThingsThatAreNotColoursAreNotColours()
    {
        // A swatch under a binding path or an ordinary word would be worse than none at all.
        foreach (var token in new[] { "Title", "#nothex", "#12345", "rgb(1,2)", "hsl(a,b,c)", "", "  ",
                                      "SendCommand", "42", "#", "url(x.png)" })
            Assert.IsNull(ColorLiterals.Parse(token, alphaFirst: true), $"'{token}' should not read as a colour");
    }
}
