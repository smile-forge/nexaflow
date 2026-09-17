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
/// Writing in a kanban board through the editor that hosts it: a column's title and a card's — on any of its lines where it wraps — are the
/// characters written, so a press puts the caret among them and a keystroke changes the board.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("kanban-writing")]
public class KanbanEditingTests
{
    private const string Plan = "kanban\n  Todo\n    id4[Create parsing tests that cover every case anybody could think of]@{ assigned: knsv }\n  done[Done]";

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
    public void TypingInAColumnsTitleAndOnEachLineOfAWrappedCardTitleChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, chart) =>
        {
            Assert.IsFalse(chart.IsReadOnly, "a kanban board's words are written in");

            PressPast(chart, "Todo");
            Write(rtb, "s");
            PressPast(chart, "Done");
            Write(rtb, "!");

            // The card's title wraps: a press at the end of its first line and at the end of its last each write there.
            PressPast(chart, "Create");
            Write(rtb, "X");
            var lines = chart.Laid.Root.SelfAndDescendants().Where(piece => piece is { Kind: "Title", Words.Maps: true } && piece.Part!.Start > chart.Source.IndexOf("id4[", StringComparison.Ordinal) && piece.Part.Start < chart.Source.IndexOf("]@{", StringComparison.Ordinal)).ToList();
            Assert.IsTrue(lines.Count > 1, "the card's title wraps");
            chart.BeginPointerSelect(new Point(lines[^1].Bounds.Right - 1, lines[^1].Bounds.Y + (lines[^1].Bounds.Height / 2)));
            chart.EndPointerSelect();
            Write(rtb, "?");

            var title = chart.Source[(chart.Source.IndexOf("id4[", StringComparison.Ordinal) + 4)..chart.Source.IndexOf(']', chart.Source.IndexOf("id4[", StringComparison.Ordinal))];
            StringAssert.Contains(chart.Source, "  Todos\n", chart.Source);
            StringAssert.Contains(chart.Source, "done[Done!]", chart.Source);
            StringAssert.EndsWith(title, "of?", chart.Source);
            Assert.AreEqual("Create parsing tests that cover every case anybody could think of", title.Replace("X", "").Replace("?", ""), "X written once, on the first line");
            Assert.IsTrue(title.IndexOf('X') > 0 && title.IndexOf('X') < title.Length / 2, chart.Source);
            Assert.AreEqual(0, chart.Diagnostics.Count, string.Join(" | ", chart.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }));
    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}


