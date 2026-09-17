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
/// Writing in a timeline through the editor that hosts it: a period's name, an event written on its line, one written on
/// a line going on from it, and a section's name are the characters written, so a press puts the caret among them and a
/// keystroke changes the timeline.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("timeline-writing")]
public class TimelineEditingTests
{
    private const string Social =
        "timeline\n  section Early days\n    2002 : LinkedIn\n         : Friendster\n    2004 : Facebook";

    private static void InADocument(Action<InlineMarkdownEditor, RichTextBox, ContentElement> test) =>
        MarkdownEditorHarness.Run("Social:\n\n```mermaid\n" + Social + "\n```\n", (editor, rtb) =>
        {
            var diagram = Find<ContentElement>(editor);
            Assert.IsNotNull(diagram, "the diagram did not render as content");
            Assert.IsTrue(editor.FocusBlockAtCaret(), "the editor has the diagram to give the keys to");

            test(editor, rtb, diagram!);
        });

    private static void PressPast(ContentElement diagram, string words)
    {
        var piece = diagram.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Words is { Maps: true } && piece.Sits().Start == diagram.Source.IndexOf(words, StringComparison.Ordinal));

        diagram.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        diagram.EndPointerSelect();
    }

    private static void Write(RichTextBox rtb, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(rtb, text);
        MarkdownEditorHarness.Pump();
    }

    [TestMethod]
    public void TypingInASectionAPeriodAndItsEventsChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a timeline's words are written in");

            PressPast(diagram, "Early days");
            Write(rtb, "!");
            PressPast(diagram, "LinkedIn");
            Write(rtb, "!");
            PressPast(diagram, "Friendster");
            Write(rtb, "?");

            StringAssert.Contains(diagram.Source, "section Early days!", diagram.Source);
            StringAssert.Contains(diagram.Source, "2002 : LinkedIn!", diagram.Source);
            StringAssert.Contains(diagram.Source, ": Friendster?", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "LinkedIn!", "and so does the document");
        }));

    [TestMethod]
    public void AColonTypedIntoAnEventGoesInAsTheEntityCodeForIt() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Facebook");
            Write(rtb, ":");

            StringAssert.Contains(diagram.Source, "Facebook#colon;", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count, "and the line still reads as one period");
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}
