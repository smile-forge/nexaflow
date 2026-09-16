using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Xy;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in an xychart through the editor that hosts it: a category under its tick, an axis's title and a series' name in
/// its legend row are the characters written, so a press puts the caret among them and a keystroke changes the chart.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("xy-chart-writing")]
public class XyEditingTests
{
    private const string Sales = "xychart\n  x-axis \"Month\" [jan, feb]\n  bar \"Sold\" [1, 2]";

    private static void InADocument(Action<InlineMarkdownEditor, RichTextBox, ContentElement> test, string diagram = Sales) =>
        MarkdownEditorHarness.Run("Sales:\n\n```mermaid\n" + diagram + "\n```\n", (editor, rtb) =>
        {
            var chart = Find<ContentElement>(editor);
            Assert.IsNotNull(chart, "the diagram did not render as content");
            Assert.IsTrue(editor.FocusBlockAtCaret(), "the editor has the diagram to give the keys to");

            test(editor, rtb, chart!);
        });

    /// <summary>Presses just inside the end of words drawn for what starts where <paramref name="words"/> is written.</summary>
    private static void PressPast(ContentElement chart, string words)
    {
        var piece = chart.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Words is { Maps: true } && piece.Sits().Start == chart.Source.IndexOf(words, StringComparison.Ordinal));

        chart.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        chart.EndPointerSelect();
    }

    private static void Press(RichTextBox rtb, Key key)
    {
        MarkdownEditorHarness.RaiseKey(rtb, key);
        MarkdownEditorHarness.Pump();
    }

    private static void Write(RichTextBox rtb, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(rtb, text);
        MarkdownEditorHarness.Pump();
    }

    private static string Trouble(ContentElement chart) => string.Join(" | ", chart.Diagnostics.Select(diagnostic => diagnostic.Message));

    [TestMethod]
    public void TypingInACategoryChangesTheCategory() => UiThread.Run(() =>
        InADocument((editor, rtb, chart) =>
        {
            Assert.IsFalse(chart.IsReadOnly, "an xychart's words are written in");

            PressPast(chart, "jan");
            Write(rtb, "e");

            StringAssert.Contains(chart.Source, "[jane, feb]", chart.Source);
            StringAssert.Contains(editor.Markdown, "[jane, feb]", "and so does the document");
        }));

    [TestMethod]
    public void ASpaceTypedIntoACategoryPutsItInQuotes() => UiThread.Run(() =>
        InADocument((editor, rtb, chart) =>
        {
            PressPast(chart, "feb");
            Press(rtb, Key.Space);
            Write(rtb, "2");

            StringAssert.Contains(chart.Source, "[jan, \"feb 2\"]", chart.Source);
            Assert.AreEqual(0, chart.Diagnostics.Count, Trouble(chart));
        }));

    [TestMethod]
    public void TypingInAnAxisTitleAndALegendRowChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, chart) =>
        {
            PressPast(chart, "Month");
            Write(rtb, "s");
            PressPast(chart, "Sold");
            Write(rtb, "!");

            StringAssert.Contains(chart.Source, "x-axis \"Months\"", chart.Source);
            StringAssert.Contains(chart.Source, "bar \"Sold!\"", chart.Source);
            Assert.AreEqual(0, chart.Diagnostics.Count, Trouble(chart));
        }));

    [TestMethod]
    public void DeletingAWholeCategoryLeavesAHoleToWriteANewOneIn() => UiThread.Run(() =>
        InADocument((editor, rtb, chart) =>
        {
            PressPast(chart, "feb");
            for (var letter = 0; letter < "feb".Length; letter++) Press(rtb, Key.Back);

            StringAssert.Contains(chart.Source, "[jan, ]", chart.Source);
            Assert.AreEqual(1, chart.Laid.Holes.Count, "a hole stands where it goes");
            Assert.AreEqual(chart.Laid.Holes[0].Sits().Start, chart.Caret);

            Write(rtb, "mar");
            StringAssert.Contains(chart.Source, "[jan, mar]", chart.Source);
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}
