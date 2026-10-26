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
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

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

    [TestMethod]
    public void PagesMoved_KeepTheReaderOnThePageTheyWereReading()
    {
        UiThread.Run(() =>
        {
            var source = new Rearranging(10, new Size(600, 800));
            var element = Shown(source);
            element.GoToPage(6);

            source.Arrange([6, 0, 1, 2, 3, 4, 5, 7, 8, 9]);

        Assert.AreEqual(0, element.CurrentPage, "the page being read went first, and the reader with it");
        });
    }

    [TestMethod]
    public void ThePageBeingReadDeleted_TheReaderGoesToTheNextOneLeft()
    {
        UiThread.Run(() =>
        {
            var source = new Rearranging(10, new Size(600, 800));
            var element = Shown(source);
            element.GoToPage(6);

            source.Arrange([0, 1, 2, 3, 4, 5, 7, 8, 9]);

            Assert.AreEqual(6, element.CurrentPage, "page 7 is now sixth, and is read from its top");
            Assert.AreEqual(9 * 800 + 10 * element.PageGap, element.ExtentHeight, 1, "the scroll range lost a page");
        });
    }

    [TestMethod]
    public void ThePageBeingReadSplit_TheReaderGoesToTheFirstPageMadeFromIt_WhereverItWent()
    {
        UiThread.Run(() =>
        {
            var source = new Rearranging(3, new Size(600, 800));
            var element = Shown(source);

            // A booklet's first sheet, unfolded: its right half is read first, its left half last.
            source.Arrange([11, 1, 2, 10], new Dictionary<int, int[]> { [10] = [0], [11] = [0] });

            Assert.AreEqual(0, element.CurrentPage, "the sheet being read became the first page and the last; it is read on in the first");
        });
    }

    [TestMethod]
    public void APageInsertedBeforeTheReader_KeepsTheirPlace_AndOnlyPagesNearThemAreAskedFor()
    {
        UiThread.Run(() =>
        {
            var source = new Rearranging(10, new Size(600, 800));
            var element = Shown(source);
            element.GoToPage(6);
            source.Laid.Clear();

            source.Arrange([100, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);

            Assert.AreEqual(7, element.CurrentPage);
            Assert.IsTrue(source.Laid.Contains(7), "the page being read is asked for at its new place");
            Assert.IsTrue(source.Laid.Count < 6, $"asked for {source.Laid.Count} pages after the rearrangement");
            CollectionAssert.DoesNotContain(source.Laid.ToList(), 0, "the new page far above is not asked for");
        });
    }

    [TestMethod]
    public void TheFirstPageGone_AndTheRestReordered_TheReaderFollowsTheNextOneLeft()
    {
        UiThread.Run(() =>
        {
            var source = new Rearranging(3, new Size(600, 800));
            var element = Shown(source);

            source.Arrange([2, 1]);

            Assert.AreEqual(1, element.CurrentPage, "the first page is gone; the reader goes to the next one left, now second");
            Assert.AreEqual(2 * 800 + 3 * element.PageGap, element.ExtentHeight, 1);
        });
    }

    private static PagedLayoutElement Shown(IPagedLayoutSource source)
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

            var into = new LayoutBuilder(WpfMedium.OnThisThread);
            into.Open("page");
            into.Draw(new WashMark(new Rect(laidSize).ToLayout(), Brushes.White.Ink()));
            var words = WpfMedium.OnThisThread.Shape(text, new Typeface("Arial"), 12, Brushes.Black, null, CultureInfo.InvariantCulture);
            into.Open("text", new SourceSpan(0, text.Length), new LayoutPoint(20, 20));
            into.Words(new LayoutWords(words, default, Maps: true));
            into.Covers(new LayoutRect(0, 0, words.WidthWithSpace, words.Height));
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

    /// <summary>
    /// Pages made here that can be rearranged in place: each is known by the number it was made with, which its text says.
    /// </summary>
    private sealed class Rearranging(int count, Size size) : IRearrangingPagedSource
    {
        private List<int> _order = Enumerable.Range(0, count).ToList();

        public HashSet<int> Laid { get; } = [];

        public event EventHandler<PagesRearranged>? Rearranged;

        public int PageCount => _order.Count;

        public Size PageSize(int index) => size;

        /// <summary>
        /// Puts the pages made as <paramref name="order"/>, a number not made before being a new page — made, where
        /// <paramref name="madeFrom"/> says so, from the numbers it names.
        /// </summary>
        public void Arrange(List<int> order, IReadOnlyDictionary<int, int[]>? madeFrom = null)
        {
            var before = order.Select(page => _order.IndexOf(page) is var was and >= 0 ? was : (int?)null).ToList();
            var from = order
                .Select(page => madeFrom?.GetValueOrDefault(page) is { } numbers
                    ? (IReadOnlyList<int>)numbers.Select(number => _order.IndexOf(number)).ToList()
                    : [])
                .ToList();
            _order = order;
            Rearranged?.Invoke(this, new PagesRearranged(before, from));
        }

        public Task<LaidPage> LayAsync(int index, CancellationToken ct)
        {
            Laid.Add(index);
            var into = new LayoutBuilder(WpfMedium.OnThisThread);
            into.Open("page");
            into.Draw(new WashMark(new Rect(size).ToLayout(), Brushes.White.Ink()));
            into.Close();
            return Task.FromResult(new LaidPage(into.Seal(), size, $"page {_order[index]}"));
        }

        public Task<string> TextAsync(int index, CancellationToken ct) => Task.FromResult($"page {_order[index]}");
    }
}
