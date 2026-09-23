using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

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

    private static void InADocument(Action<MarkdownSurface, DocumentBlock> test) =>
        MarkdownEditorHarness.Run("Plan:\n\n```mermaid\n" + Plan + "\n```\n", editor =>
        {
            var chart = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(chart, "the diagram did not render as content");

            test(editor, chart!);
        });

    private static void PressPast(DocumentBlock chart, string words)
    {
        var piece = chart.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Words is { Maps: true } && piece.Sits().Start == chart.Source.IndexOf(words, chart.Start, System.StringComparison.Ordinal));

        chart.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        chart.EndPointerSelect();
    }

    private static void Write(MarkdownSurface editor, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(editor, text);
        MarkdownEditorHarness.Pump();
    }

    private static string Trouble(DocumentBlock chart) => string.Join(" | ", chart.Diagnostics.Select(diagnostic => diagnostic.Message));

    [TestMethod]
    public void TypingInACaptionAnAxisEndAndAPointsNameChangesThem() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            Assert.IsFalse(chart.IsReadOnly, "a quadrant chart's words are written in");

            PressPast(chart, "Plan");
            Write(editor, "s");
            PressPast(chart, "Later");
            Write(editor, "!");
            PressPast(chart, "Task A");
            Write(editor, "1");

            StringAssert.Contains(chart.Source, "quadrant-1 Plans", chart.Source);
            StringAssert.Contains(chart.Source, "--> Later!", chart.Source);
            StringAssert.Contains(chart.Source, "Task A1:::hot", chart.Source);
            Assert.AreEqual(0, chart.Diagnostics.Count, Trouble(chart));
            StringAssert.Contains(editor.Markdown, "Task A1:::hot", "and so does the document");
        }));

    [TestMethod]
    public void AColonTypedIntoAPointsNamePutsItInQuotes() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            PressPast(chart, "Task A");
            Write(editor, ":");

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
