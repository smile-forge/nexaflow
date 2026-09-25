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
public class KanbanEditingTests : MermaidEditing
{
    private const string Plan = "kanban\n  Todo\n    id4[Create parsing tests that cover every case anybody could think of]@{ assigned: knsv }\n  done[Done]";

    /// <inheritdoc/>
    protected override string Source => Plan;

    [TestMethod]
    public void TypingInAColumnsTitleAndOnEachLineOfAWrappedCardTitleChangesThem() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            Assert.IsFalse(chart.IsReadOnly, "a kanban board's words are written in");

            PressPast(chart, "Todo");
            Write(editor, "s");
            PressPast(chart, "Done");
            Write(editor, "!");

            // The card's title wraps: a press at the end of its first line and at the end of its last each write there.
            var one = chart.Laid.Root.SelfAndDescendants().Where(piece => piece.Kind == "Title").Min(piece => piece.Bounds.Height);
            var wrapped = Title(chart);
            Assert.IsTrue(wrapped.Bounds.Height > one * 1.5, "the card's title wraps");
            chart.BeginPointerSelect(new Point(wrapped.Bounds.Right - 1, wrapped.Bounds.Top + (one / 2)));
            chart.EndPointerSelect();
            Write(editor, "X");

            wrapped = Title(chart);
            chart.BeginPointerSelect(new Point(wrapped.Bounds.Right - 1, wrapped.Bounds.Bottom - (one / 2)));
            chart.EndPointerSelect();
            Write(editor, "?");

            var title = chart.Source[(chart.Source.IndexOf("id4[", StringComparison.Ordinal) + 4)..chart.Source.IndexOf(']', chart.Source.IndexOf("id4[", StringComparison.Ordinal))];
            StringAssert.Contains(chart.Source, "  Todos\n", chart.Source);
            StringAssert.Contains(chart.Source, "done[Done!]", chart.Source);
            StringAssert.EndsWith(title, "of?", chart.Source);
            Assert.AreEqual("Create parsing tests that cover every case anybody could think of", title.Replace("X", "").Replace("?", ""), "X written once, on the first line");
            Assert.IsTrue(title.IndexOf('X') > 0 && title.IndexOf('X') < title.Length / 2, chart.Source);
            Assert.AreEqual(0, chart.Diagnostics.Count, string.Join(" | ", chart.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }));

    /// <summary>The long card's title, as it is drawn now: one run of words, broken into lines to fit its card.</summary>
    private static Piece Title(DocumentBlock chart) =>
        chart.Laid.Root.SelfAndDescendants().Single(piece => piece is { Kind: "Title", Words.Maps: true }
                                                            && piece.Part!.Start > chart.Source.IndexOf("id4[", StringComparison.Ordinal)
                                                            && piece.Part.Start < chart.Source.IndexOf("]@{", StringComparison.Ordinal));
}


