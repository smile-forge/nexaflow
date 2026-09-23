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
/// Driven through a real <see cref="MarkdownSurface"/> being written in, because that is the whole claim: the diagram is
/// pieces of the document's one laid tree, a press in it puts the document's caret among its words, and a key typed there
/// is asked of the diagram's own language before anything else. A diagram's own tests say what to press and what should
/// come of it; everything round that is the same for every diagram, and is here.
/// </para>
/// </summary>
public abstract class MermaidEditing
{
    /// <summary>The block being written in.</summary>
    protected abstract string Source { get; }

    /// <summary>Shows the block in a document, and hands back a view of it.</summary>
    internal void InADocument(Action<MarkdownSurface, DocumentBlock> test) =>
        MarkdownEditorHarness.Run("Below:\n\n```mermaid\n" + Source + "\n```\n", editor =>
        {
            var diagram = MarkdownEditorHarness.Block(editor);
            Assert.AreEqual(Source, diagram.Latex.TrimEnd('\n'), "the diagram is a block of the document");

            test(editor, diagram);
        });

    /// <summary>Presses just inside the end of the words the diagram draws for <paramref name="words"/>, putting the caret there.</summary>
    internal static void PressPast(DocumentBlock diagram, string words)
    {
        var at = diagram.Source.IndexOf(words, diagram.Start, StringComparison.Ordinal);
        var piece = diagram.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Words is { Maps: true } && piece.Sits().Start == at);

        diagram.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        diagram.EndPointerSelect();
    }

    /// <summary>Types <paramref name="text"/> wherever the caret is, and lets the editor catch up.</summary>
    protected static void Write(MarkdownSurface editor, string text)
    {
        MarkdownEditorHarness.Type(editor, text);
        MarkdownEditorHarness.Pump();
    }
}
