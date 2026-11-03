using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What stands beside a surface's page: the lines on the page as the engine says them — their numbers, and where their rows stand, laid
/// whole or a unit at a time — and the margins on either side, standing outward from the page in the order listed and drawn at the
/// lines' tops.
/// <para>Interactive desktop only — the editor builds its document during a render pass.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("content-margins")]
public class ContentMarginTests
{
    [TestMethod]
    public void TheLinesOnThePage_AreSaidWithTheirNumbers_EachStandingWhereItsRowsDo() => UiThread.Run(() =>
    {
        var paragraph = string.Join(' ', Enumerable.Range(0, 80).Select(word => $"word{word}"));

        Shown($"first\n{paragraph}\nthird", surface =>
        {
            var lines = surface.LinesShown();

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, lines.Select(line => line.Number).ToArray());
            Assert.IsTrue(lines[1].Height >= 3 * lines[0].Height - 0.5, $"the wrapped line stands {lines[1].Height:0.#} tall, not as tall as its rows");

            for (var at = 1; at < lines.Count; at++)
                Assert.AreEqual(lines[at - 1].Top + lines[at - 1].Height, lines[at].Top, 0.5, $"line {lines[at].Number} does not start where the one before ends");
        });
    });

    [TestMethod]
    public void ScrolledFarDownALongDocument_TheLinesSaidAreTheOnesOnThePage() => UiThread.Run(() =>
    {
        var text = string.Concat(Enumerable.Range(0, 30_000).Select(line => $"line {line} of the document\n"));

        Shown(text, surface =>
        {
            // The page has a height to stand anywhere only once it is laid out, which a loaded machine can take a while over.
            Settle(surface);
            Assert.IsTrue(surface.ShowFrom(ContentDocument.Of(text).OffsetOf(19_999)), "the page could not stand at line 20,000");

            Settle(surface);

            var lines = surface.LinesShown();

            Assert.IsTrue(lines.Count > 5, $"only {lines.Count} lines were said: {string.Join(", ", lines.Select(line => $"{line.Number}@{line.Top:0}+{line.Height:0}"))}; page from {surface.ShownFrom}");
            Assert.AreEqual(20_000, lines[0].Number, 1, "the first line said is not the one the page starts at");
            CollectionAssert.AreEqual(Enumerable.Range(lines[0].Number, lines.Count).ToArray(), lines.Select(line => line.Number).ToArray(), "the lines said are not one after another");
        });
    });

    [TestMethod]
    [CoversNode("windowed-reading")]
    public void MovedWithinAUnitLaidInPart_EveryLineOnThePageIsLaidAndNumbered() => UiThread.Run(() =>
    {
        // Unit after unit of short lines: the line gone to is in the first unit, well past what its first screen laid.
        var text = string.Concat(Enumerable.Range(0, 30_000).Select(line => $"line {line} of the document\n"));

        Shown(text, surface =>
        {
            Assert.IsTrue(surface.ShowFrom(ContentDocument.Of(text).OffsetOf(599)));
            Settle(surface);

            var lines = surface.LinesShown();
            var row = lines.Count > 0 ? lines[0].Height : 1;

            Assert.AreEqual(600, lines.FirstOrDefault().Number, 1, "the page does not start at the line gone to");
            Assert.IsTrue(lines.Count >= 400 / row - 2, $"only {lines.Count} of the lines on the page were laid: the screen moved within a unit laid in part, and nothing laid what came onto it");
        });
    });

    [TestMethod]
    public void MarginsStandOutwardFromThePage_InTheOrderListed_AndAreDrawnAtTheLinesTops() => UiThread.Run(() =>
    {
        var (near, far, after, beyond) = (new Marked(10), new Marked(20), new Marked(10), new Marked(20));

        Shown("one\ntwo\nthree", surface =>
        {
            double Left(FrameworkElement margin) => margin.TranslatePoint(default, surface).X;

            Assert.IsTrue(Left(far) < Left(near), "the second margin leading the page does not stand outside the first");
            Assert.IsTrue(Left(after) < Left(beyond), "the second margin trailing the page does not stand outside the first");
            Assert.AreEqual(near.ActualWidth, 10, 0.5, "a margin is not as wide as it measures");

            var shift = surface.TranslatePoint(default, near).Y;
            var lines = surface.LinesShown();

            CollectionAssert.AreEqual(lines.Select(line => line.Number).ToArray(), near.Drew.Select(line => line.Number).ToArray());
            for (var at = 0; at < lines.Count; at++) Assert.AreEqual(lines[at].Top + shift, near.Drew[at].Top, 0.5, "a line is drawn away from its top");
        }, surface =>
        {
            surface.LeadingMargins.Add(near);
            surface.LeadingMargins.Add(far);
            surface.TrailingMargins.Add(after);
            surface.TrailingMargins.Add(beyond);
        });
    });

    [TestMethod]
    public void AMarginTakenAway_StandsBesideNothing() => UiThread.Run(() =>
    {
        var margin = new Marked(10);

        Shown("one\ntwo", surface =>
        {
            Assert.AreSame(surface, margin.Holder);

            surface.LeadingMargins.Remove(margin);
            MarkdownEditorHarness.Pump();

            Assert.IsNull(margin.Holder, "it still stands beside the surface it was taken from");
            Assert.IsNull(VisualTreeHelper.GetParent(margin), "it is still on the page");
        }, surface => surface.LeadingMargins.Add(margin));
    });

    [TestMethod]
    public void ContentLaidWithoutLines_HasNoneToSayBesideIt() => UiThread.Run(() =>
        Shown("# A heading\n\nSome words.\n", surface => Assert.AreEqual(0, surface.LinesShown().Count, "a rendered document said lines"), typed: false));

    /// <summary>
    /// <paramref name="text"/> in a surface in a window, shown as typed with its lines wrapped unless <paramref name="typed"/> says not —
    /// what <paramref name="configure"/> does to it done first — and <paramref name="test"/> run on it once it is laid out and drawn.
    /// </summary>
    internal static void Shown(string text, Action<MarkdownSurface> test, Action<MarkdownSurface>? configure = null, bool typed = true)
    {
        var surface = new MarkdownSurface { IsReadOnly = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (typed) (surface.WrittenIn, surface.WrapsLines) = (ContentLanguages.AsTyped, true);
        configure?.Invoke(surface);

        // Shown but never activated, so the machine's own keyboard focus is left where it was.
        var window = new Window
        {
            Width = 600, Height = 400, Content = surface, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000,
        };

        try
        {
            window.Show();
            surface.Document = new EditedDocument(text);
            surface.UpdateLayout();
            MarkdownEditorHarness.Pump();

            test(surface);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Lets <paramref name="surface"/> lay out and draw what it now shows, over a few passes — each one bounded, so a surface that never
    /// settles fails the test rather than hanging it.
    /// </summary>
    internal static void Settle(MarkdownSurface surface)
    {
        for (var pass = 0; pass < 10; pass++)
        {
            surface.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle,
                                                                         System.Threading.CancellationToken.None, TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>A margin as wide as it is told, keeping the lines it was last drawn for.</summary>
    private sealed class Marked(double wide) : ContentMargin
    {
        public List<ShownLine> Drew { get; } = [];

        /// <summary>The surface it stands beside.</summary>
        public MarkdownSurface? Holder => Surface;

        protected override Size MeasureOverride(Size availableSize) => new(wide, 0);

        protected override void Draw(DrawingContext drawing, IReadOnlyList<ShownLine> lines)
        {
            Drew.Clear();
            Drew.AddRange(lines);
        }
    }
}
