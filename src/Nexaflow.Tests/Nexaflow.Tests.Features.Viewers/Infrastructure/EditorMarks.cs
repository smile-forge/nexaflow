using Nexaflow.Markdown.Editing;

namespace Nexaflow.Tests.Features.Infrastructure;

/// <summary>What a page's editor marks over its whole document — what it would draw, were all of it on screen.</summary>
public static class EditorMarks
{
    /// <summary>The marks <paramref name="editor"/>'s layers make with <paramref name="token"/> over the whole of its document: where each starts and how far it runs.</summary>
    public static IReadOnlyList<(int Start, int Length)> MarkedBy(ContentEditor editor, string token)
    {
        var document = editor.Document.Current;
        return [.. editor.Marks.Mark(new ContentMarkRequest(document, 0, document.Length)).Where(mark => mark.Token == token).Select(mark => (mark.Start, mark.Length))];
    }
}
