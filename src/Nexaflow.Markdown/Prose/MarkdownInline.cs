using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The text of a block, read into the constructs a writer spelled with punctuation — emphasis, code spans, links.
///
/// <para>
/// The parser a paragraph and a heading are read by, a definition's term, a figure's caption and a table's cell: what is in
/// them is the same language, whatever holds it. The words are read over the lines they are written on, each from past
/// what its containers put at its head — a quote's <c>&gt;</c>, an item's indentation (<see cref="MarkdownLines"/>) — so
/// those marks are trivia between the words and never words. What makes a block that block — a heading's hashes, the line
/// under it — is the source that falls before or after the words, kept where it was written.
/// </para>
/// <para>
/// <strong>Every character comes from the source.</strong> The words are matched as CommonMark matches them
/// (<see cref="MarkdownDelimiters"/>), and every piece is cut out of the source where it stands. A stretch that does not
/// print back as what it was cut from is shown as it was typed rather than repaired into something nobody wrote.
/// </para>
/// </summary>
public static class MarkdownInline
{
    /// <summary>
    /// <paramref name="source"/> as the words and marks it is made of: a paragraph's, every line of it words — or anything
    /// else that is nothing but words, a term, a caption, a cell.
    /// </summary>
    /// <param name="at">Where <paramref name="source"/>'s first character stands in the document holding it.</param>
    /// <param name="lead">
    /// What stands in front of <paramref name="source"/> on the line it starts on: a paragraph in a quote carries the quote's
    /// <c>&gt;</c> on every line after its first, and is read as it reads there — see <see cref="MarkdownLines"/>.
    /// </param>
    public static ContentNode Read(string? source, int at = 0, string? lead = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(Kinds.Words, [], offset: at);

        var lines = new List<(int From, int To)>();
        var written = MarkdownLines.Of(text, lead);

        for (var index = 0; written.TryLine(index, out var line); index++) lines.Add((line.Content, line.End));

        // The line ending closing the block, and any blank line after it, are no line of its words.
        while (lines.Count > 0 && text.AsSpan(lines[^1].From, lines[^1].To - lines[^1].From).IsWhiteSpace()) lines.RemoveAt(lines.Count - 1);

        return Words(text, lines, at);
    }

    /// <summary>
    /// A heading's source as the words and marks it is made of: its words, between the hashes opening it and any closing it —
    /// or above the line underlining it — which the scan finds as it would where the heading stands.
    /// </summary>
    /// <param name="at">Where <paramref name="source"/>'s first character stands in the document holding it.</param>
    /// <param name="lead">What stands in front of <paramref name="source"/> on the line it starts on — see <see cref="MarkdownLines"/>.</param>
    public static ContentNode Heading(string? source, int at = 0, string? lead = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(Kinds.Words, [], offset: at);

        var scanned = MarkdownScanner.Scan(MarkdownLines.Of(text, lead), document: false);
        var heading = scanned.Children.FirstOrDefault(block => block.Kind == ScannedKind.Heading);

        return Words(text, heading?.Lines ?? [], at);
    }

    /// <summary>
    /// The words written on <paramref name="lines"/>, whatever stands round them kept where it was written, checked to print
    /// as what they were read from — and, where a link in them waits on a name the document may define, the list they were
    /// matched from, for whoever knows the names to match again (<see cref="WithReferences"/>).
    /// </summary>
    private static ContentNode Words(string text, IReadOnlyList<(int From, int To)> lines, int at)
    {
        var read = new Cut(text, at);
        var parts = new List<ContentNode>();
        MarkdownDelimiters? waiting = null;

        if (lines.Count > 0)
        {
            read.Gap(parts, lines[0].From);
            parts.AddRange(MarkdownDelimiters.Read(text, lines, at, parts.Count, out waiting));
            read.Accounted(lines[^1].To);
        }

        read.Gap(parts, read.Length);

        if (waiting is not null) parts.Add(ContentNode.Holding(MarkdownKinds.Delimiters, Roles.Derived, waiting));

        return MarkdownParser.Checked(Kinds.Words, parts, text, Roles.Element, at);
    }
}
