using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a quadrant chart through the editor that hosts it: a caption, an axis's end and a point's name are the
/// characters written, so a press puts the caret among them and a keystroke changes the chart.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("quadrant-graph-writing")]
public class QuadrantEditingTests
{
    private const string Plan = "quadrantChart\n  x-axis Urgent --> Later\n  quadrant-1 Plan\n  Task A:::hot: [0.3, 0.6]\n  classDef hot color: #ff0000";

    private static void InADocument(Action<InlineMarkdownEditor, RichTextBox, ContentElement> test) =>
        MarkdownEditorHarness.Run("Plan:\n\n```mermaid\n" + Plan + "\n```\n", (editor, rtb) =>
        {
            var chart = Find<ContentElement>(editor);
            Assert.IsNotNull(chart, "the diagram did not render as content");
            Assert.IsTrue(editor.FocusBlockAtCaret(), "the editor has the diagram to give the keys to");

            test(editor, rtb, chart!);
        });

    private static void PressPast(ContentElement chart, string words)
    {
        var piece = chart.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Words is { Maps: true } && piece.Sits().Start == chart.Source.IndexOf(words, StringComparison.Ordinal));

        chart.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        chart.EndPointerSelect();
    }

    private static void Write(RichTextBox rtb, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(rtb, text);
        MarkdownEditorHarness.Pump();
    }

    private static string Trouble(ContentElement chart) => string.Join(" | ", chart.Diagnostics.Select(diagnostic => diagnostic.Message));

    [TestMethod]
    public void TypingInACaptionAnAxisEndAndAPointsNameChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, chart) =>
        {
            Assert.IsFalse(chart.IsReadOnly, "a quadrant chart's words are written in");

            PressPast(chart, "Plan");
            Write(rtb, "s");
            PressPast(chart, "Later");
            Write(rtb, "!");
            PressPast(chart, "Task A");
            Write(rtb, "1");

            StringAssert.Contains(chart.Source, "quadrant-1 Plans", chart.Source);
            StringAssert.Contains(chart.Source, "--> Later!", chart.Source);
            StringAssert.Contains(chart.Source, "Task A1:::hot", chart.Source);
            Assert.AreEqual(0, chart.Diagnostics.Count, Trouble(chart));
            StringAssert.Contains(editor.Markdown, "Task A1:::hot", "and so does the document");
        }));

    [TestMethod]
    public void AColonTypedIntoAPointsNamePutsItInQuotes() => UiThread.Run(() =>
        InADocument((editor, rtb, chart) =>
        {
            PressPast(chart, "Task A");
            Write(rtb, ":");

            StringAssert.Contains(chart.Source, "\"Task A:\":::hot: [0.3, 0.6]", chart.Source);
            Assert.AreEqual(0, chart.Diagnostics.Count, Trouble(chart));
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}
