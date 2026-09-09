using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Coverage for <see cref="ContentBuilder"/> — the one promise every builder makes: laying something out
/// always produces a layout.
///
/// <para>
/// Asserted here rather than through a formula or a tune, because it is not either of their rules. A
/// builder that hands back nothing makes everything downstream carry a second state, and the point of the
/// base class is that no builder is in a position to do so however badly its own reading goes.
/// </para>
/// <para>
/// Needs an STA thread for WPF's font machinery — the fallback sets characters. It opens no window.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-source-map")]
public class ContentBuilderTests
{
    /// <summary>A builder that reads nothing, however it is asked.</summary>
    private sealed class Unwilling(string source, Func<Laid?> read) : ContentBuilder(source)
    {
        protected override Laid? Read() => read();

        protected override FormattedText Characters(string text) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 13, Brushes.Black, 1.0);
    }

    [TestMethod]
    public void ABuilderThatCanReadNothingStillLaysTheSourceOut() => UiThread.Run(() =>
    {
        var laid = new Unwilling("a+b", () => null).Lay();

        Assert.IsTrue(laid.ShowsSource, "the source is shown as its own characters");
        Assert.AreEqual(3, laid.Root.Sits().Length, "standing for every character of it");
        Assert.IsTrue(laid.Size.Width > 0 && laid.Size.Height > 0, "and it takes up room");
        Assert.AreEqual(0, laid.Trouble.Count, "nothing went wrong — there was simply nothing to read");
    });

    [TestMethod]
    public void EveryCharacterIsAPlaceEvenWhenNoneOfItCouldBeRead() => UiThread.Run(() =>
    {
        // The reason the fallback is a layout rather than a drawing: unreadable source is exactly what
        // somebody is in the middle of fixing, so the caret has to be able to walk it and a selection has
        // to be able to take part of it.
        var laid = new Unwilling("a+b", () => null).Lay();

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, laid.Stops.ToArray());
    });

    [TestMethod]
    public void ABuilderThatThrowsBecomesALayoutSayingSo() => UiThread.Run(() =>
    {
        var laid = new Unwilling("x^", () => throw new InvalidOperationException("ran off the end")).Lay();

        Assert.IsTrue(laid.ShowsSource, "the source is still on the page");

        var trouble = laid.Trouble.Single();
        Assert.AreEqual(DiagnosticSeverity.Error, trouble.Severity);
        Assert.AreEqual((0, 2), (trouble.Start, trouble.Length), "covering all of it — it has no better idea");
        StringAssert.Contains(trouble.Message, "ran off the end");
    });

    [TestMethod]
    public void EmptySourceIsAPlaceToPutTheCaretRatherThanNothingAtAll() => UiThread.Run(() =>
    {
        var laid = new Unwilling("", () => null).Lay();

        Assert.IsTrue(laid.Size.Height > 0, "a line's height, so a caret can be drawn against it");
        CollectionAssert.AreEqual(new[] { 0 }, laid.Stops.ToArray(), "with exactly one place to stand");
        Assert.AreEqual(0, laid.Root.Sits().Length, "and the blank standing in for it names no source");
    });
}
