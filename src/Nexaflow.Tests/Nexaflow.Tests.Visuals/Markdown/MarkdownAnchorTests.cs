using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// In-page links (<see cref="MarkdownAnchors"/>): every heading carries a GitHub-style id, a <c>#id</c> link is resolved
/// by the surface it sits in — scrolling to the heading — and is never handed to the host or the shell, while any other
/// relative link stays inert. UI category: FlowDocuments are built on an STA thread; no window opens.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("md-anchor-links")]
public class MarkdownAnchorTests
{
    private const string Doc =
        "# Intro\n\n[Go to searching](#searching) or [the web](https://example.com/x).\n\n" +
        "## Opening and closing help\n\nText.\n\n## Searching\n\nText.\n\n## Searching\n\nAgain.\n";

    [TestMethod]
    public void Headings_CarryGitHubStyleIds_ARepeatNumbered() => UiThread.Run(() =>
    {
        var doc = MarkdownFlowDocument.Build(Doc, StyleFormat.Dark);

        var ids = doc.Blocks.Select(MarkdownAnchors.GetId).Where(id => id is not null).ToList();

        CollectionAssert.AreEqual(new[] { "intro", "opening-and-closing-help", "searching", "searching-1" }, ids);
        Assert.IsNotNull(MarkdownAnchors.Find(doc, "Searching"), "an anchor matches whatever its case");
        Assert.IsNull(MarkdownAnchors.Find(doc, "nowhere"));
    });

    [TestMethod]
    public void AnInPageLink_IsTheViewsToResolve_NeverTheHosts() => UiThread.Run(() =>
    {
        var handed = new List<string>();
        var view = new SelectableMarkdownView { LinkNavigate = url => { handed.Add(url); return true; } };

        view.Markdown = Doc;

        Press(view, "Go to searching");
        Assert.AreEqual(0, handed.Count, "a bare #anchor means nothing outside the document");

        Press(view, "the web");
        CollectionAssert.AreEqual(new[] { "https://example.com/x" }, handed, "any other link is still the host's");
    });

    [TestMethod]
    public void ScrollToAnchor_FindsItsHeading_AndSaysWhenThereIsNone() => UiThread.Run(() =>
    {
        var view = new SelectableMarkdownView { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        view.Markdown = Doc;

        Assert.IsTrue(view.ScrollToAnchor("opening-and-closing-help"));
        Assert.IsTrue(view.ScrollToAnchor("Opening-And-Closing-Help"), "an anchor matches whatever its case");
        Assert.IsFalse(view.ScrollToAnchor("nowhere"));
    });

    /// <summary>Presses the words of a link on the surface the view is showing.</summary>
    private static void Press(SelectableMarkdownView view, string words)
    {
        var shown = ((MarkdownSurface)view.Content).Shown;

        shown.Measure(new Size(640, 2000));
        shown.Arrange(new Rect(0, 0, 640, 2000));

        var piece = shown.Laid.Root.SelfAndDescendants()
            .First(one => one.Words is { } said && said.Glyphs.Text.Contains(words));

        shown.BeginPointerSelect(new Point(piece.Bounds.X + (piece.Bounds.Width / 2),
                                           piece.Bounds.Y + (piece.Bounds.Height / 2)));
        shown.EndPointerSelect();
    }

    [TestMethod]
    public void ARelativeLinkThatIsNotAnAnchor_StaysInert() => UiThread.Run(() =>
    {
        var doc = MarkdownFlowDocument.Build("[notes](notes.md)\n", StyleFormat.Dark);

        Assert.IsNull(Links(doc).Single().NavigateUri, "nothing to navigate to, so nothing is handed to the shell");
    });

    private static void Click(Hyperlink link)
        => link.RaiseEvent(new RequestNavigateEventArgs(link.NavigateUri, null) { RoutedEvent = Hyperlink.RequestNavigateEvent });

    private static List<Hyperlink> Links(DependencyObject root)
    {
        var found = new List<Hyperlink>();
        void Walk(DependencyObject node)
        {
            if (node is Hyperlink link) found.Add(link);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
        }
        Walk(root);
        return found;
    }
}
