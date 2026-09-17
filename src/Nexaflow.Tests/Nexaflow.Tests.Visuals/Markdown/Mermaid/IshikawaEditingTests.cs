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
/// Writing in an ishikawa diagram through the editor that hosts it: the event, a boxed cause and a cause further in are the
/// characters written, so a press puts the caret among them and a keystroke changes the diagram.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("ishikawa-writing")]
public class IshikawaEditingTests
{
    private const string Photo = "ishikawa-beta\n  Blurry Photo\n  Process\n    Out of focus\n      Wrong mode\n  User\n    Shaky hands";

    private static void InADocument(Action<InlineMarkdownEditor, RichTextBox, ContentElement> test) =>
        MarkdownEditorHarness.Run("Photo:\n\n```mermaid\n" + Photo + "\n```\n", (editor, rtb) =>
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
    public void TypingInTheEventACauseAndACauseFurtherInChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "an ishikawa diagram's words are written in");

            PressPast(diagram, "Blurry Photo");
            Write(rtb, "s");
            PressPast(diagram, "Process");
            Write(rtb, "es");
            PressPast(diagram, "Out of focus");
            Write(rtb, "!");
            PressPast(diagram, "Wrong mode");
            Write(rtb, "?");

            StringAssert.Contains(diagram.Source, "  Blurry Photos\n  Processes\n    Out of focus!\n      Wrong mode?\n", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "Out of focus!", "and so does the document");
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}
