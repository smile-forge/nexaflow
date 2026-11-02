using Nexaflow.Markdown.Editing;

namespace Nexaflow.Tests.Features;

/// <summary>What a test writes in a page's document, as the reader writing it would.</summary>
internal static class DocumentWriting
{
    /// <summary>
    /// <paramref name="text"/> written over the whole of <paramref name="document"/> as one edit: a step that can be taken back, and a
    /// change to what was saved where it says something else.
    /// </summary>
    public static void WriteWhole(this EditedDocument document, string text) =>
        document.Write(ContentChange.Write(0, document.Current.Length, text));
}
