using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a gantt chart through the editor that hosts it: a task's name, in its bar or beside it, and a section's name are the
/// characters written, so a press puts the caret among them and a keystroke changes the chart.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("gantt-writing")]
public class GanttEditingTests
{
    private const string Plan = "gantt\n  section Build\n  Design the whole thing :a1, 2014-01-01, 30d\n  QA :after a1, 1d";

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

    [TestMethod]
    public void TypingInATasksNameInItsBarBesideItAndASectionsNameChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, chart) =>
        {
            Assert.IsFalse(chart.IsReadOnly, "a gantt chart's words are written in");

            PressPast(chart, "Design the whole thing");
            Write(rtb, "!");
            PressPast(chart, "QA");
            Write(rtb, "s");
            PressPast(chart, "Build");
            Write(rtb, "ing");

            StringAssert.Contains(chart.Source, "section Building\n  Design the whole thing! :a1", chart.Source);
            StringAssert.Contains(chart.Source, "QAs :after a1", chart.Source);
            Assert.AreEqual(0, chart.Diagnostics.Count, string.Join(" | ", chart.Diagnostics.Select(diagnostic => diagnostic.Message)));
            StringAssert.Contains(editor.Markdown, "QAs :after a1", "and so does the document");
        }));

    [TestMethod]
    public void AColonTypedIntoATasksNameIsNotWritten() => UiThread.Run(() =>
        InADocument((editor, rtb, chart) =>
        {
            PressPast(chart, "QA");
            Write(rtb, ":");

            StringAssert.Contains(chart.Source, "  QA :after a1, 1d", chart.Source);
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}
