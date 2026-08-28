using System.Windows.Media;
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
    private static Color Xaml(string token) =>
        ColorLiterals.Parse(token, alphaFirst: true) ?? throw new AssertFailedException($"'{token}' was not read as a colour");

    private static Color Css(string token) =>
        ColorLiterals.Parse(token, alphaFirst: false) ?? throw new AssertFailedException($"'{token}' was not read as a colour");

    [TestMethod]
    public void SixDigitHex_ReadsTheSameInEitherDialect()
    {
        Assert.AreEqual(Color.FromRgb(0xFF, 0x3B, 0x30), Xaml("#FF3B30"));
        Assert.AreEqual(Color.FromRgb(0xFF, 0x3B, 0x30), Css("#ff3b30"));
    }

    [TestMethod]
    public void EightDigitHex_PutsAlphaWhereTheLanguagePutsIt()
    {
        // #80FF0000 — half-transparent red in XAML; opaque-ish #80FF00 with alpha 00 in CSS.
        Assert.AreEqual(Color.FromArgb(0x80, 0xFF, 0x00, 0x00), Xaml("#80FF0000"));
        Assert.AreEqual(Color.FromArgb(0x00, 0x80, 0xFF, 0x00), Css("#80FF0000"));
    }

    [TestMethod]
    public void ShorthandHex_IsExpandedByDoublingEachDigit()
    {
        Assert.AreEqual(Color.FromRgb(0xAA, 0xBB, 0xCC), Xaml("#abc"));
        Assert.AreEqual(Color.FromArgb(0xDD, 0xAA, 0xBB, 0xCC), Xaml("#dabc"));
        Assert.AreEqual(Color.FromArgb(0xDD, 0xAA, 0xBB, 0xCC), Css("#abcd"));
    }

    [TestMethod]
    public void QuotesAreIgnored_SoAnAttributeValueCanBePassedAsWritten()
    {
        Assert.AreEqual(Color.FromRgb(0xFF, 0x3B, 0x30), Xaml("\"#FF3B30\""));
    }

    [TestMethod]
    public void NamedColoursAreRecognised_CaseInsensitively()
    {
        Assert.AreEqual(Colors.Tomato, Css("tomato"));
        Assert.AreEqual(Colors.Tomato, Xaml("Tomato"));
        Assert.AreEqual(Colors.Transparent, Xaml("Transparent"));
    }

    [TestMethod]
    public void FunctionalNotation_BothTheCommaAndSpaceForms()
    {
        var red = Color.FromRgb(255, 59, 48);
        Assert.AreEqual(red, Css("rgb(255, 59, 48)"));
        Assert.AreEqual(red, Css("rgb(255 59 48)"));
        Assert.AreEqual(Color.FromArgb(128, 255, 59, 48), Css("rgba(255, 59, 48, 0.5)"));
        Assert.AreEqual(Color.FromArgb(128, 255, 59, 48), Css("rgb(255 59 48 / 0.5)"), "the modern slash-alpha form");
    }

    [TestMethod]
    public void PercentageChannelsAreScaled()
    {
        Assert.AreEqual(Color.FromRgb(255, 0, 0), Css("rgb(100%, 0%, 0%)"));
    }

    [TestMethod]
    public void HslIsConverted()
    {
        Assert.AreEqual(Color.FromRgb(255, 0, 0), Css("hsl(0, 100%, 50%)"));
        Assert.AreEqual(Color.FromRgb(0, 255, 0), Css("hsl(120 100% 50%)"));
        Assert.AreEqual(Color.FromRgb(128, 128, 128), Css("hsl(0, 0%, 50%)"));
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
