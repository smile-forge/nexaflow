using System;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Fonts;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Exceptions;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering.Transformations;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

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
    private static DefaultTexFont Font() => new(WpfMathFontProvider.Instance, 1.0);

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

    [TestMethod]
    public void ACharacterTheTextFontLacksSaysSoWhenItIsDrawn() => UiThread.Run(() =>
    {
        var environment = Environment();
        var info = Glyph.Letter('∅', TexUtilities.TextStyleName).Info(environment.TextFont, environment.Style).Value;

        var thrown = Assert.ThrowsExactly<TexCharacterMappingNotFoundException>(() => info.GetGlyphRun(20.0, 0.5, 1.0));
        Assert.AreEqual("The Arial font does not support '∅' (U+2205) character.", thrown.Message);
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
