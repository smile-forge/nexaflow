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
/// Writing in a git graph through the editor that hosts it: a branch's name is the characters written in its label, so a
/// press puts the caret among them and a keystroke changes the branch — and what a commit says about itself is pressed
/// where it is drawn, turned or not.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("gitgraph-writing")]
public class GitEditingTests
{
    private const string History =
        "gitGraph\n  commit id: \"Alpha\"\n  branch develop\n  commit\n  checkout main\n  merge develop";

    private static void InADocument(Action<InlineMarkdownEditor, RichTextBox, ContentElement> test) =>
        MarkdownEditorHarness.Run("History:\n\n```mermaid\n" + History + "\n```\n", (editor, rtb) =>
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
    public void TypingInABranchsLabelChangesTheBranchItIsMade() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a git graph's branch names are written in");

            PressPast(diagram, "develop");
            Write(rtb, "ing");

            StringAssert.Contains(diagram.Source, "branch developing", diagram.Source);
            StringAssert.Contains(editor.Markdown, "branch developing", "and so does the document");
        }));

    [TestMethod]
    public void WhatACommitSaysAboutItselfIsPressedWhereItIsDrawn() => UiThread.Run(() =>
        InADocument((_, _, diagram) =>
        {
            var id = diagram.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == "Id");
            var at = new Point(id.Bounds.X + (id.Bounds.Width / 2), id.Bounds.Y + (id.Bounds.Height / 2));

            diagram.BeginPointerSelect(at);
            diagram.EndPointerSelect();

            Assert.AreEqual("\"Alpha\"", diagram.Source.Substring(id.Sits().Start, id.Sits().Length), "the id stands for what writes it");
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}
