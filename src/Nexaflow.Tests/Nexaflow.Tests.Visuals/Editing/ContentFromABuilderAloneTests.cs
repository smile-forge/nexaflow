using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

using Content = Nexaflow.Visuals.Text.Editing.ContentElement;
using Nexaflow.Visuals.Text.Markdown.Music;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// A builder is the whole of what a kind of content costs.
///
/// <para>
/// Neither a tune nor a barcode has an element, a caret, a selection or an edit model of its own any
/// more; each is a <see cref="Content"/> handed a builder. So this asks the thing that claim rests on —
/// that they take a caret, are typed into, and arrow along — of content assembled exactly as the
/// document assembles it, rather than of a hand-built one.
/// </para>
/// <para>
/// It exists because the claim was made and not checked. Both were said to have lost editing when their
/// elements were deleted; they had not, because what replaced them brought it back. And the one thing
/// that really was lost — where a barcode's value sits inside its fence — went unnoticed for exactly as
/// long as nobody asked.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-editing")]
public class ContentFromABuilderAloneTests
{
    [TestMethod]
    public void ATuneTakesACaretAndIsTypedInto() => UiThread.Run(() =>
    {
        var tune = Laid(MusicScore.Engraved(MusicDialect.Abc, "X:1\nL:1/8\nK:C\nCDEF|\n", MarkdownPalette.Dark, sourceStart: 0));

        Assert.IsTrue(tune.AcceptsCaret, "the engraver named parts of the source, so there is somewhere to stand");
        Assert.IsTrue(tune.Laid.Places.Count > 0);

        tune.TakeCaret(0);
        Assert.IsTrue(tune.HasCaret);

        var was = tune.Source;
        tune.Type('G');
        Assert.AreNotEqual(was, tune.Source, "a note typed into the tune reached its source");
        Assert.IsTrue(tune.MoveCaret(forward: true), "and the arrow walks the places the engraver declared");
    });

    [TestMethod]
    public void ABarcodeTakesACaretAndIsTypedInto() => UiThread.Run(() =>
    {
        var barcode = Laid(Barcode("format: CODE128\nvalue: HELLO123"));

        Assert.IsTrue(barcode.AcceptsCaret, "a CODE128 prints what was typed, so it can be typed into");

        barcode.TakeCaret(0);
        barcode.Type('9');

        Assert.AreEqual("9HELLO123", barcode.Source);
        Assert.IsTrue(barcode.MoveCaret(forward: true));
    });

    [TestMethod]
    public void ABarcodeKnowsWhereItsValueSitsInTheFence() => UiThread.Run(() =>
    {
        // The host reads SourceStart < 0 as "the whole block IS this content", which only a $$…$$
        // formula is, and puts the delimiters back on every edit. A barcode that did not say where its
        // value sat was turned into a formula by the first digit typed into it.
        const string source = "format: CODE128\nvalue: HELLO123";
        var barcode = Laid(Barcode(source));

        Assert.IsFalse(barcode.IsWholeBlock, "a barcode is a run inside its fence, not the fence");
        Assert.AreEqual(source.IndexOf("HELLO123", System.StringComparison.Ordinal), barcode.SourceStart);
        Assert.AreEqual("HELLO123".Length, barcode.SourceLength);
    });

    private static Content Barcode(string source) =>
        (Content)new BarcodeDiagramHandler().Render(source, DiagramRenderOptions.For(MarkdownPalette.Dark));

    /// <summary>Measured and arranged, because a caret is a place on a page that has been laid out.</summary>
    private static Content Laid(Content content)
    {
        content.Measure(new Size(900, double.PositiveInfinity));
        content.Arrange(new Rect(new Point(0, 0), content.DesiredSize));
        return content;
    }
}
