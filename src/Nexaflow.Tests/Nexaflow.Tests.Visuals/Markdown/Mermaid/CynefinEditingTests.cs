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
/// Writing in a Cynefin diagram through the editor that hosts it: an item carded in a domain, an item inside the disorder
/// cloud, and a movement's label are the characters written, so a press puts the caret among them and a keystroke changes them.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("cynefin-writing")]
public class CynefinEditingTests
{
    private const string Sense =
        "cynefin-beta\n  complex\n    \"Investigate root cause\"\n  confusion\n    \"Unclassified A\"\n  chaotic\n  chaotic --> complex : \"Stabilised\"";

    private static void InADocument(Action<InlineMarkdownEditor, RichTextBox, ContentElement> test) =>
        MarkdownEditorHarness.Run("Sense:\n\n```mermaid\n" + Sense + "\n```\n", (editor, rtb) =>
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
    public void TypingInACardedItemOneInTheDisorderAndAMovementsLabelChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a Cynefin diagram's words are written in");

            PressPast(diagram, "Investigate root cause");
            Write(rtb, "s");
            PressPast(diagram, "Unclassified A");
            Write(rtb, "!");
            PressPast(diagram, "Stabilised");
            Write(rtb, "?");

            StringAssert.Contains(diagram.Source, "\"Investigate root causes\"", diagram.Source);
            StringAssert.Contains(diagram.Source, "\"Unclassified A!\"", diagram.Source);
            StringAssert.Contains(diagram.Source, "\"Stabilised?\"", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "Investigate root causes", "and so does the document");
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}
