using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Music;

/// <summary>
/// A score inside the <em>selectable</em> surface. The block renderer's path was covered; this one was not,
/// which is where a text-tree crash was able to hide: the score's prose is now real FlowDocument text, and the
/// RichTextBox walks that text on every caret move, selection and focus change.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("music-block")]
public class MusicFlowDocumentTests
{
    private static string SampleDoc() =>
        File.ReadAllText(Path.Combine(TestSampleData.Root, "markdown", "music-abc.md"));

    /// <summary>Every pointer operation the RichTextBox performs on its own — walking the tree, selecting all
    /// of it, mapping offsets — over a document full of scores. A malformed text tree faults here.</summary>
    [TestMethod]
    public void TheWholeSampleDoc_SurvivesEveryTextPointerWalk() => UiThread.Run(() =>
    {
        var doc = MarkdownFlowDocument.Build(SampleDoc(), MarkdownPalette.Light);
        var rtb = new RichTextBox { Document = doc };
        var host = new Window { Content = rtb, Width = 900, Height = 600, ShowActivated = false };
        try
        {
            host.Show();
            rtb.UpdateLayout();

            // Select the lot, then step a caret through every position in the document.
            rtb.Selection.Select(doc.ContentStart, doc.ContentEnd);
            Assert.IsFalse(rtb.Selection.IsEmpty);

            int steps = 0;
            for (var p = doc.ContentStart; p is not null && p.CompareTo(doc.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward)!)
            {
                rtb.CaretPosition = p;
                p.GetCharacterRect(LogicalDirection.Forward);
                if (++steps > 20_000) break;
            }
            Assert.IsTrue(steps > 50, "the document should have real content to walk");

            // …and back by character offset, which is the path that faulted.
            for (int i = 0; i < 400; i++)
                doc.ContentStart.GetPositionAtOffset(i, LogicalDirection.Forward);
        }
        finally
        {
            host.Close();
        }
    });

    /// <summary>Focus round-trips through the score. Re-activating a window restores keyboard focus to whatever
    /// held it, and if that is an element embedded in the text tree the RichTextBox has to reconcile its caret
    /// with a position that isn't text — which is what faulted deep in the splay tree.</summary>
    [TestMethod]
    public void FocusRoundTrip_ThroughAnEmbeddedScore_DoesNotFaultTheTextTree() => UiThread.Run(() =>
    {
        var doc = MarkdownFlowDocument.Build(SampleDoc(), MarkdownPalette.Light);
        var rtb = new RichTextBox { Document = doc };
        var other = new Button { Content = "elsewhere" };
        var host = new Window
        {
            Content = new StackPanel { Children = { rtb, other } },
            Width = 900,
            Height = 600,
        };
        try
        {
            host.Show();
            rtb.UpdateLayout();

            var score = Descendants(rtb).OfType<Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreElement>().First();
            Assert.IsFalse(score.Focusable,
                "an engraved score must not take keyboard focus: it lives inside the RichTextBox's text tree, " +
                "and focus landing on it makes the caret reconciliation walk a node that has no text");

            score.Focus();                       // a no-op now, but the point is that it stays a no-op
            other.Focus();
            rtb.Focus();

            _ = rtb.CaretPosition;
            rtb.Selection.Select(doc.ContentStart, doc.ContentEnd);
            _ = rtb.Selection.Text;
        }
        finally
        {
            host.Close();
        }
    });

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    [TestMethod]
    public void ScoreProse_IsSelectableText_InTheDocument() => UiThread.Run(() =>
    {
        var doc = MarkdownFlowDocument.Build(SampleDoc(), MarkdownPalette.Light);
        string text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;

        StringAssert.Contains(text, "Speed the Plough", "a score title is text, not pixels");
        StringAssert.Contains(text, "Notes: see also Playford", "…and so are the notes under the score");
    });

    /// <summary>An empty <c>W:</c> line is a blank verse. It must not become an empty Run — a paragraph with no
    /// text symbols is exactly the kind of node the FlowDocument text tree trips over.</summary>
    [TestMethod]
    public void BlankVerseLines_DoNotProduceEmptyRuns() => UiThread.Run(() =>
    {
        var doc = MarkdownFlowDocument.Build(SampleDoc(), MarkdownPalette.Light);
        var empty = doc.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines.OfType<Run>())
            .Where(r => r.Text.Length == 0)
            .ToList();
        Assert.AreEqual(0, empty.Count, "no zero-length runs in the document");
    });
}
