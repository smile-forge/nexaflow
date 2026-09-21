using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// What writing in a diagram is tested through: the block shown in a document, a press just past some words it draws, and
/// a keystroke.
///
/// <para>
/// Driven through a real <see cref="InlineMarkdownEditor"/>, because that is the whole claim: the caret has to be adopted
/// by the editor and the keystroke routed back into the block, neither of which the block can do for itself. A diagram's
/// own tests say what to press and what should come of it; everything round that is the same for every diagram, and is
/// here.
/// </para>
/// </summary>
public abstract class MermaidEditing
{
    /// <summary>The block being written in.</summary>
    protected abstract string Source { get; }

    /// <summary>Shows the block in a document, and hands back the element it drew with the caret in it.</summary>
    protected void InADocument(Action<InlineMarkdownEditor, ContentElement> test) =>
        MarkdownEditorHarness.Run("A diagram:\n\n```mermaid\n" + Source + "\n```\n", editor =>
        {
            var diagram = Find<ContentElement>(editor);
            Assert.IsNotNull(diagram, "the diagram did not render as content");
            Assert.IsTrue(editor.FocusBlockAtCaret(), "the editor has the diagram to give the keys to");

            test(editor, diagram!);
        });

    /// <summary>Presses just inside the end of the words the diagram draws for <paramref name="words"/>, putting the caret there.</summary>
    protected static void PressPast(ContentElement diagram, string words)
    {
        var piece = diagram.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Words is { Maps: true } && piece.Sits().Start == diagram.Source.IndexOf(words, StringComparison.Ordinal));

        diagram.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        diagram.EndPointerSelect();
    }

    /// <summary>Types <paramref name="text"/> wherever the caret is, and lets the editor catch up.</summary>
    protected static void Write(InlineMarkdownEditor editor, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(editor, text);
        MarkdownEditorHarness.Pump();
    }

    /// <summary>The first thing of its kind in the editor's visual tree.</summary>
    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}
