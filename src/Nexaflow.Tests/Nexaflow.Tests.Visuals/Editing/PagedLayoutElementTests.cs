using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Editing.Paged;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// The paged host, driven headless against pages made here: what it lays and when, where the reader is, what zoom does
/// to the scroll range, and what a selection across pages copies. Pages it has not been asked to lay never are.
/// </summary>
[TestClass]
[CoversNode("paged-layout")]
public class PagedLayoutElementTests
{
    private static readonly Size Viewport = new(800, 600);

    [TestMethod]
    public void OnlyThePagesNearTheViewportAreLaid_AndTheScrollRangeIsEveryPage()
    {
        UiThread.Run(() =>
        {
            var source = new Pages(100, new Size(600, 800));
            var element = Shown(source);

            Assert.IsTrue(element.ExtentHeight > 100 * 800, "the extent is every page, gaps and all");
            Assert.IsTrue(source.Laid.Count is > 0 and < 6, $"laid {source.Laid.Count} pages for one screen");
            CollectionAssert.DoesNotContain(source.Laid.ToList(), 50, "a page far below is not laid");

            element.GoToPage(50);
            Assert.IsTrue(source.Laid.Contains(50), "going to a page lays it");
        });
    }

    [TestMethod]
    public void GoingToAPage_MakesItThePageBeingRead_EvenWhereTheDocumentIsTooShortToScrollIt()
    {
        UiThread.Run(() =>
        {
            var tall = Shown(new Pages(10, new Size(600, 800)));
            tall.GoToPage(6);
            Assert.AreEqual(6, tall.CurrentPage);
            Assert.IsTrue(tall.VerticalOffset > 0);

            var shortOne = Shown(new Pages(3, new Size(600, 150)));
            shortOne.GoToPage(2);
            Assert.AreEqual(2, shortOne.CurrentPage, "three short pages fit the screen; the last can still be gone to");
        });
    }

    [TestMethod]
    public void Zooming_ScalesTheScrollRange()
    {
        UiThread.Run(() =>
        {
            var element = Shown(new Pages(4, new Size(600, 800)));
            var gaps = 5 * element.PageGap;
            Assert.AreEqual(4 * 800 + gaps, element.ExtentHeight, 1);

            element.Zoom = 2;
            element.UpdateLayout();

            // The pages double; the gaps between them are the same on screen at every size.
            Assert.AreEqual(2 * 4 * 800 + gaps, element.ExtentHeight, 1, "twice the size is twice the pages");
            Assert.AreEqual(2, element.Zoom);

            element.Zoom = 100;
            Assert.AreEqual(PagedLayoutElement.MaxZoom, element.Zoom, "zoom is held to its range");
        });
    }

    [TestMethod]
    public void SelectingEverything_CopiesEveryPagesText_APageToALine()
    {
        UiThread.Run(() =>
        {
            var source = new Pages(3, new Size(600, 800));
            var element = Shown(source);

            element.SelectAll();
            Assert.IsTrue(element.HasSelection);
            var copied = element.SelectedTextAsync().GetAwaiter().GetResult();

            Assert.AreEqual("page 0\npage 1\npage 2", copied, "pages not on screen give their text without being laid");
            Assert.IsFalse(source.Laid.Contains(2) && source.Texted.Contains(2), "a laid page's text is not read twice");
        });
    }

    [TestMethod]
    public void APageLaidAtAnotherSize_MovesThePagesAfterIt()
    {
        UiThread.Run(() =>
        {
            var source = new Pages(5, new Size(600, 800)) { Actual = index => index == 0 ? new Size(600, 1600) : new Size(600, 800) };
            var element = Shown(source);

            var gap = element.PageGap;
            Assert.AreEqual(1600 + 4 * 800 + 6 * gap, element.ExtentHeight, 1, "the first page's real height counts");
        });
    }

    private static PagedLayoutElement Shown(Pages source)
    {
        var element = new PagedLayoutElement { Source = source };
        element.Measure(Viewport);
        element.Arrange(new Rect(Viewport));
        element.UpdateLayout();
        return element;
    }

    /// <summary>Pages made here, each one run of text saying which page it is.</summary>
    private sealed class Pages(int count, Size size) : IPagedLayoutSource
    {
        public HashSet<int> Laid { get; } = [];
        public HashSet<int> Texted { get; } = [];
        public Func<int, Size>? Actual { get; init; }

        public int PageCount => count;

        public Size PageSize(int index) => size;

        public Task<LaidPage> LayAsync(int index, CancellationToken ct)
        {
            Laid.Add(index);
            var text = $"page {index}";
            var laidSize = Actual?.Invoke(index) ?? size;

            var into = new LayoutBuilder();
            into.Open("page");
            into.Draw(new WashMark(new Rect(laidSize), Brushes.White));
            var words = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Arial"), 12, Brushes.Black, 1.0);
            into.Open("text", new SourceSpan(0, text.Length), new Point(20, 20));
            into.Words(new LayoutWords(words, default, Maps: true));
            into.Covers(new Rect(0, 0, words.WidthIncludingTrailingWhitespace, words.Height));
            into.Close();
            into.Close();
            return Task.FromResult(new LaidPage(into.Seal(), laidSize, text));
        }

        public Task<string> TextAsync(int index, CancellationToken ct)
        {
            Texted.Add(index);
            return Task.FromResult($"page {index}");
        }
    }
}
