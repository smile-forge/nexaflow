using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// A summary written out as a markdown document of its own, to be read from the top down: the document's title, what kind of
/// document it is, its abstract, then every section's heading and summary at the depth the section stands — so a reader folds
/// a section away or opens it to go from the whole to the part — each heading leading back to its section in the document
/// summarised.
///
/// <para>
/// Every word is spelled by the parser: a title as words (<see cref="MarkdownParser.SpellWords(string, bool)"/>), and what a
/// model wrote as paragraphs (<see cref="MarkdownParser.SpellParagraph"/>) — its emphasis, code and formulas kept, nothing in
/// it able to open a block, so a summary that begins with a hash is a summary, not a heading.
/// </para>
/// </summary>
public static class SummaryMarkdown
{
    /// <summary>The deepest a heading is written.</summary>
    private const int Deepest = 6;

    /// <param name="summary">The summary, from the whole document down.</param>
    /// <param name="gist">What the document is.</param>
    /// <param name="title">What to call it, where the summary has no title of its own.</param>
    /// <param name="source">The full path of the document summarised, which every section's heading leads back to; null for none.</param>
    public static string Write(SummaryNode summary, DocumentGist gist, string title, string? source)
    {
        var written = new StringBuilder();

        // A document whose whole body stands under one title is that section: its heading is the title, its summary the
        // abstract, and its sections the summary's outermost — rather than the whole said twice over.
        var whole = summary.Under is [{ } only] && only.Title == summary.Title ? only : null;

        written.Append("# ").Append(MarkdownParser.SpellWords(summary.Title.Length > 0 ? summary.Title : title, startsLine: false));
        if (source is not null && whole?.Section?.Anchor is { } anchor) written.Append(" [↗](<").Append(Back(source, anchor)).Append(">)");
        written.Append("\n\n");

        written.Append('*').Append(MarkdownParser.SpellWords($"{gist.Genre} · {gist.Audience}", startsLine: false).Trim()).Append("*\n\n");
        Paragraphs(written, summary.Summary);

        foreach (var section in whole?.Under ?? summary.Under) Section(written, section, 2, source);

        return written.ToString();
    }

    /// <summary>A section's heading at <paramref name="rank"/>, its summary, and the sections under it a rank deeper.</summary>
    private static void Section(StringBuilder written, SummaryNode section, int rank, string? source)
    {
        written.Append('#', Math.Min(rank, Deepest)).Append(' ').Append(MarkdownParser.SpellWords(section.Title, startsLine: false));

        if (source is not null && section.Section?.Anchor is { } anchor) written.Append(" [↗](<").Append(Back(source, anchor)).Append(">)");

        written.Append("\n\n");
        Paragraphs(written, section.Summary);

        foreach (var under in section.Under) Section(written, under, rank + 1, source);
    }

    /// <summary>What a model wrote, as the paragraphs it wrote it in — its inline markup kept, nothing in it able to open a block.</summary>
    private static void Paragraphs(StringBuilder written, string text)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n"))
            if (paragraph.Trim() is { Length: > 0 } said)
                written.Append(MarkdownParser.SpellParagraph(said)).Append("\n\n");
    }

    /// <summary>
    /// Where a heading leads back to: the document summarised, at the section its heading's name says — a saved reference
    /// (<see cref="ContentPath"/>), which lands where that heading is now however the document has changed round it.
    /// </summary>
    public static string Back(string source, string anchor) =>
        new Uri(Path.GetFullPath(source)).AbsoluteUri + "#" +
        Uri.EscapeDataString(new ContentPath([new ContentStep(MarkdownKinds.Heading, anchor)]).ToString());
}
