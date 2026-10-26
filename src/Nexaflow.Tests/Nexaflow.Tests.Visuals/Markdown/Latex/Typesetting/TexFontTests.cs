using System;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Fonts;
using Nexaflow.Markdown.Typesetting.Tex;
using Nexaflow.Markdown.Typesetting.Tex.Exceptions;
using Nexaflow.Markdown.Typesetting.Tex.Rendering.Transformations;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;
using Nexaflow.Markdown.Latex;
using Nexaflow.Markdown.Layout.Maths;
using Nexaflow.Markdown.Typesetting;
using Nexaflow.Markdown.Typesetting.Tex.Fonts;
using Nexaflow.Markdown.Typesetting.Tex.Rendering;

using Nexaflow.Markdown.Layout;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// The drawing data a formula is set from: the Computer Modern metrics, the system text font, and the transformations
/// a glyph is drawn through. What each says when asked for something it does not have.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class TexFontTests
{
    private static DefaultTexFont Font() => new(DefaultTexFontData.Read(WpfMathFontProvider.Instance), 1.0);

    [TestMethod]
    public void ACharacterInATextStyleHasCharInfo() => UiThread.Run(() =>
        Assert.IsTrue(Font().GetCharInfo('x', "text", TexStyle.Text).IsSuccess));

    [TestMethod]
    public void AnUnknownTextStyleIsATextStyleMappingNotFound() => UiThread.Run(() =>
        Assert.IsInstanceOfType<TextStyleMappingNotFoundException>(Font().GetCharInfo('x', "unknownStyle", TexStyle.Text).Error));

    [TestMethod]
    public void ASymbolHasCharInfo() => UiThread.Run(() =>
        Assert.IsTrue(Font().GetCharInfo("sqrt", TexStyle.Text).IsSuccess));

    [TestMethod]
    public void AnUnknownSymbolIsASymbolMappingNotFound() => UiThread.Run(() =>
        Assert.IsInstanceOfType<SymbolMappingNotFoundException>(Font().GetCharInfo("unknownSymbol", TexStyle.Text).Error));

    [TestMethod]
    public void AFontSlotHasCharInfo() => UiThread.Run(() =>
        Assert.IsTrue(Font().GetCharInfo(new CharFont('x', 1), TexStyle.Text).IsSuccess));

    [TestMethod]
    public void AnUnknownCharacterInAFontSlotIsACharacterMappingNotFound() => UiThread.Run(() =>
        Assert.IsInstanceOfType<TexCharacterMappingNotFoundException>(Font().GetCharInfo(new CharFont('й', 1), TexStyle.Text).Error));

    [TestMethod]
    public void AnUnsupportedMathsCharacterIsACharacterMappingNotFound() => UiThread.Run(() =>
        Assert.IsInstanceOfType<TexCharacterMappingNotFoundException>(
            Environment().MathFont.GetDefaultCharInfo('Å', TexStyle.Display).Error));

    /// <summary>
    /// Reading the maths fonts opens every face they list, so it happens once rather than for each formula: two formulae set
    /// at different sizes draw from the very same faces, and each is still set at its own size.
    /// </summary>
    [TestMethod]
    [CoversNode("latex-fonts-read-once")]
    public void EveryFormulaDrawsFromTheFacesReadOnce_AtItsOwnSize() => UiThread.Run(() =>
    {
        var small = TexFonts.Of(WpfHandles.Medium, "Arial").Environment(TexStyle.Display, 10.0).MathFont;
        var large = TexFonts.Of(WpfHandles.Medium, "Arial").Environment(TexStyle.Display, 30.0).MathFont;

        Assert.AreSame(small.GetCharInfo('x', "text", TexStyle.Text).Value.Font, large.GetCharInfo('x', "text", TexStyle.Text).Value.Font,
                       "the second formula opened the faces again");
        Assert.AreEqual(10.0, small.Size, "a formula is set at its own size");
        Assert.AreEqual(30.0, large.Size, "a formula is set at its own size");
    });

    [TestMethod]
    public void ACharacterTheTextFontLacksSaysSoWhenItIsDrawn() => UiThread.Run(() =>
    {
        var laid = Laying.Formula(@"\text{∅}", 20.0);

        Assert.IsTrue(laid.ShowsSource, "a formula that cannot be drawn is shown as it is written");
        StringAssert.Contains(laid.Trouble.Single().Message, "The Arial font does not support '∅' (U+2205) character.");
    });

    [TestMethod]
    public void ATranslationScales()
    {
        var scaled = (Transformation.Translate)new Transformation.Translate(1.0, 2.0).Scale(10.0);
        Assert.AreEqual(10.0, scaled.X);
        Assert.AreEqual(20.0, scaled.Y);
    }

    [TestMethod]
    public void ARotationDoesNotScale()
    {
        var rotation = new Transformation.Rotate(90.0);
        Assert.AreEqual<Transformation>(rotation, rotation.Scale(10.0));
    }
}
