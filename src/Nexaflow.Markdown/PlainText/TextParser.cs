using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Lines;

namespace Nexaflow.Markdown.PlainText;

/// <summary>
/// Plain text, read: one run of its characters, held as written — and, once it is to be laid, cut into lines, each into its words and
/// the spaces between them (<see cref="InLines"/>), the pieces whatever looks into the text — a spelling, a name — finds there.
///
/// <para>
/// Nothing in it is anything but characters: no grammar, nothing that opens and closes. So it is read a stretch at a time as everything
/// read line by line is (<see cref="LineStretches"/>), and a stretch holds one run of characters that stands for them in the document,
/// cut into lines only for the stretches being laid.
/// </para>
/// </summary>
public static class TextParser
{
    /// <summary>The name a tree of plain text is known by (<see cref="BlockNode"/>), and the word a fence of it is written under.</summary>
    public const string Language = "text";

    /// <summary>Text, read as written: one run of its characters, which a stage cuts into lines (<see cref="TextIntoLines"/>).</summary>
    public static ContentNode Parse(string? source) =>
        new BlockNode(Language, [ContentNode.Leaf(Kinds.Verbatim, source ?? string.Empty, Roles.Body, offset: 0)], TextKinds.Text, offset: 0);

    /// <summary>A parse reading text a stretch at a time, as everything read line by line is (<see cref="LineStretches"/>).</summary>
    public static Func<ContentStretch, ContentParse> Windowed() => stretch => LineStretches.Read(stretch, Parse);

    /// <summary>
    /// <paramref name="tree"/>, a stretch of text, with every body of it cut at its line ends into lines (<see cref="TextKinds.Line"/>),
    /// each its words (<see cref="TextKinds.Word"/>) and the runs of space between them (<see cref="Kinds.Space"/>), and the line end
    /// closing it (<see cref="LineKinds.LineEnd"/>), a return before a line feed the line end's.
    /// </summary>
    public static ContentNode InLines(ContentNode tree)
    {
        List<ContentNode>? parts = null;

        for (var index = 0; index < tree.Children.Count; index++)
        {
            var body = tree.Children[index];

            if (body.Role != Roles.Body || body.Offset is not { } from)
            {
                parts?.Add(body);
                continue;
            }

            parts ??= [.. tree.Children.Take(index)];
            Lines(body.TextAsRead(), from, parts);
        }

        return parts is null ? tree : tree.With(parts);
    }

    /// <summary>The lines of <paramref name="text"/>, read at <paramref name="at"/>, added to <paramref name="into"/>.</summary>
    private static void Lines(string text, int at, List<ContentNode> into)
    {
        var start = 0;

        do
        {
            var feed = text.IndexOf('\n', start);
            var end = feed < 0 ? text.Length : feed + 1;

            into.Add(Line(text, start, end, at));
            start = end;
        }
        while (start < text.Length);
    }

    /// <summary>
    /// The line of <paramref name="text"/> from <paramref name="start"/> to <paramref name="end"/>, read at <paramref name="at"/>: its words and
    /// the runs of space between them, and its line end a piece of its own.
    /// </summary>
    private static ContentNode Line(string text, int start, int end, int at)
    {
        var written = end > start && text[end - 1] == '\n' ? end - 1 > start && text[end - 2] == '\r' ? end - 2 : end - 1 : end;
        var parts = new List<ContentNode>();

        for (var from = start; from < written;)
        {
            var space = text[from] is ' ' or '\t';
            var to = from + 1;

            while (to < written && (text[to] is ' ' or '\t') == space) to++;

            parts.Add(ContentNode.Leaf(space ? Kinds.Space : TextKinds.Word, text[from..to], offset: at + from));
            from = to;
        }

        if (end > written) parts.Add(ContentNode.Leaf(LineKinds.LineEnd, text[written..end], Roles.Separator, offset: at + written));

        return ContentNode.Branch(TextKinds.Line, parts, Roles.Element, offset: at + start);
    }
}
